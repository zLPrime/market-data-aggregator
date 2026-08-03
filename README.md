# Trading System — Real-Time Exchange Data Aggregator

Financial exchanges publish a non-stop stream of price quotes. This project simulates the system that
lives behind such feeds — it connects to several exchanges at once, merges their live data into one
clean stream, stores it, and keeps running when individual exchanges drop, stall, or send duplicates.

Real exchanges aren't available to build against, so the repo ships both halves: a handful of stand-in
exchange servers (the *simulators*), each streaming quotes in its own format and each able to misbehave
on command, and the *aggregator* — the main program that collects, normalizes, deduplicates, and
stores everything they send.

The task and its acceptance criteria are in [`docs/spec.md`](docs/spec.md). This README covers what
the system does, how to run it, and the engineering decisions that matter. To **watch it work
end-to-end from a fresh clone**, follow [`docs/runbook.md`](docs/runbook.md).

## What it does

The system has two halves:

- **Simulators** (`src/Simulators`) — 2–3 WebSocket servers, each emitting the same kind of quote
  (ticker, price, volume, timestamp) in a **deliberately different format**, so the aggregator's
  parsing/normalization is actually exercised:
  - **Format A** — JSON, numeric price/volume, Unix-millis timestamp
  - **Format B** — JSON with short keys (`s,p,v,t`), price as a string, ISO-8601 timestamp
  - **Format C** — pipe-delimited positional fields, Unix-seconds timestamp
  - Each simulator can inject faults on command (`drop` a connection, re-emit `dup`licates) so
    you can test the aggregator's resilience yourself.

- **Aggregator** (`src/Aggregator`) — the main part. It:
  - opens a **parallel WebSocket connection to every source**, each handled independently — one
    bad source never stalls the others;
  - **reconnects automatically** with exponential backoff (repeatedly, not once) and detects a
    hung-but-open socket via an idle timeout;
  - **normalizes** every format to one `NormalizedTick(Source, Ticker, Price, Volume, Timestamp)`;
  - **deduplicates** concurrently across sources with a bounded time window;
  - **batches** ticks into Postgres via binary `COPY` (never one row per tick);
  - handles **DB write failure without silent loss** — bounded retry, then a counted drop;
  - **drains accepted ticks to the DB on Ctrl+C** within a bounded timeout;
  - **logs** connect/disconnect/reconnect/error events and prints a **once-per-second stats line**
    (throughput, dedup/written/dropped counts, channel fill %, tracked keys, connections up).

### Pipeline

```
simulators ──ws──▶ connectors ──▶ fan-in + deduplicator ──▶ batching writer ──▶ Postgres
 (A/B/C)          (1 per source,    (N pump tasks, one         (size/time
                   reconnect,        shared dedup window,        trigger,
                   idle timeout)     shared outbound channel)    binary COPY)
```

Every stage boundary is a bounded `Channel<T>`. Channel occupancy is the backpressure signal.

## Project layout

```
TradingSystem.slnx            solution (.NET 10 .slnx format)
global.json                   pins the SDK to 10.0.302
Directory.Build.props         shared build settings (net10.0, nullable, CS4014-as-error)
db/schema.sql                 canonical ticks table (applied by ops; loaded verbatim by the e2e test)
src/Core         (Trading.Core)   core abstractions — no dependencies
src/Aggregator   (Aggregator)     the aggregator host
src/Simulators   (Simulators)     the exchange simulators
tests/Tests      (Trading.Tests)  xUnit tests (unit + hermetic end-to-end)
docs/                             spec, plan, decision log, per-subsystem design notes, runbook
```

## Requirements

- **.NET 10 SDK** (`10.0.302`, pinned in `global.json`)
- **Docker** — to run Postgres, and for the DB-backed tests (which skip cleanly when Docker is absent)

## Quick start

```bash
# 1. Start Postgres and create the ticks table
docker run --name trading-db -e POSTGRES_PASSWORD=dev -e POSTGRES_DB=ticks -p 5432:5432 -d postgres:17
docker exec -i trading-db psql -U postgres -d ticks < db/schema.sql
```

```bash
# 2. Start the three simulators (each in its own terminal)
dotnet run --project src/Simulators -- --port 9001 --format A
dotnet run --project src/Simulators -- --port 9002 --format B
dotnet run --project src/Simulators -- --port 9003 --format C
```

```bash
# 3. Start the aggregator (reads src/Aggregator/appsettings.json)
dotnet run --project src/Aggregator
```

You'll see a stats line once per second; press **Ctrl+C** to drain and stop. For the full guided
walkthrough — including injecting faults and verifying the results in the database — see
[`docs/runbook.md`](docs/runbook.md).

### Configuration

`src/Aggregator/appsettings.json` exposes only what an operator varies: the `Sources` list,
`Database.ConnectionString`, and `DrainTimeout`. Override the connection string without editing the
file via the **`TRADING_DB`** environment variable. Every other knob (batch size, dedup window,
channel capacities, backoff) keeps a spec-tuned code default — surfacing unused config is avoided on
purpose. The aggregator does **not** create the table; `db/schema.sql` is applied by ops (see above).

## Key engineering decisions

Fuller rationale and the alternatives weighed for each are in
[`docs/decisions.md`](docs/decisions.md); plain-language overviews with diagrams live in
[`docs/design/`](docs/design/). The essentials:

### Deduplication — full-tuple key, two-generation window
- **Key:** `(Source, Ticker, Price, Volume, Timestamp)` — all five identity fields. `Source` is in
  the key, so the same quote from two exchanges is two observations, not a duplicate; keeping
  price/volume avoids discarding two genuine quotes that happen to share a millisecond. A duplicate
  is therefore a **re-send within one source** (what a reconnect or the `dup` fault produces).
- **Window:** keys live in two time-bucketed generations, each spanning the window, so a key is
  remembered for **1×–2× the window**; rotation drops the whole oldest generation in O(1) — no sweep,
  no background task, bounded memory.
- **Concurrency:** check-and-record is a single `ConcurrentDictionary.TryAdd`; the two generations
  sit in one immutable snapshot read once per call, so reads are lock-free and only the (rare)
  rotation is serialized. This is the shared state hammered by all sources at once.
- **Trade-off:** an exact re-send arriving more than two windows later is treated as new — acceptable,
  since real re-sends follow a reconnect within seconds.

### DB writes — batched binary COPY
`BatchingTickWriter` accumulates ticks and flushes on a **size or time trigger**, writing each batch
as one binary `COPY` through the low-level `NpgsqlTickStore`. The store is a dumb "one COPY per batch,
throw on failure" adapter; the writer owns *when* and *whether* to persist. This split keeps "how to
persist" swappable from "the write-failure policy" (SOLID).

### DB error behavior — bounded retry, then a counted drop
On a failed batch write the writer **retries with capped exponential backoff up to
`MaxWriteAttempts`**, then increments an explicit **`Dropped`** counter, logs at Error, and moves on.
Two clean regimes result:
- a **transient** outage costs only latency and backpressure (retries hold the batch, the outbound
  channel fills, sockets slow) with **no loss**;
- a **sustained** outage past the retry budget is a **counted, logged** drop — never a silent one.

This is the spec's "no silent loss" bar met with a conscious bounded-loss decision, not a swallowed
exception. (Alternatives — infinite retry, an unbounded spare buffer — were rejected; see the log.)

### Reconnection — supervised loop, backoff reset on data, idle detection
Each connector runs a supervise → connect → read → backoff → reconnect loop that survives repeatedly.
- **Backoff resets on the first received frame**, not on connect — an endpoint that connects then
  instantly drops escalates its backoff instead of hot-looping at the floor delay.
- **Hung-socket detection:** one armed/disarmed linked `CancellationTokenSource` per connection times
  the wait *for a frame*; it's disarmed during processing, so a slow downstream can't be misread as a
  dead socket. If it fires, the receive is cancelled and the loop reconnects.
- The inbound channel is completed **exactly once**, on loop exit at shutdown — never on a socket
  drop, so a reconnect can't be mistaken for end-of-stream and silently kill a source.

### Concurrency & shutdown
- **Isolation:** one connector + one inbound channel per source; the fan-in runs **one pump task per
  source** so a quiet source never blocks a busy one.
- **Graceful shutdown is a bounded, two-token drain.** Ctrl+C cancels an *intake* token (connectors
  stop, completing their channels) while a still-live *drain* token lets the fan-in and writer finish:
  inbound channels drain → outbound completes → the writer flushes its last partial batch. If the
  `DrainTimeout` deadline is exceeded, the drain is force-cancelled (see the limitation below).
- The repo's **hard rules** are enforced throughout: no `.Result`/`.Wait()`, no `async void`, no
  fire-and-forget tasks (every background Task is tracked and its failure observed), every loop honors
  a `CancellationToken`, and no `catch` swallows — each logs or increments a counter.

### Extensibility
Adding an exchange is two symmetric additions and no rewrite: a new `IQuoteFormatter` in the
simulator and a new `IMessageParser` in the aggregator. Everything downstream (connector, fan-in,
dedup, writer) is format-agnostic. A round-trip test pins each format's two halves together.

## Monitoring

Structured `ILogger` events fire for connect / disconnect / reconnect-with-backoff / parse-error /
batch-written / batch-dropped. On top of that, a `StatsReporter` prints one line per second:

```
stats recv/s=742 recv=51234 parse=0 dedup=1180 written=50054 dropped=0 fill=3% keys=6 conns=3/3
```

`fill` is the outbound channel's occupancy — the live **backpressure** indicator; `keys` is the
deduplicator's tracked-key gauge (witnesses bounded memory); `conns` is live/configured connections.
Under a clean drain, the no-silent-loss accounting invariant holds:
`Received == Deduplicated + Written + Dropped`.

## Testing

```bash
dotnet test
```

Beyond unit tests for normalization and deduplication, the suite includes the "breaking" scenarios
the spec asks for — the deduplicator's thread-safety under concurrent writers, and reconnect after a
drop without losing the other sources — plus a **hermetic end-to-end test** that assembles the *real*
pipeline (in-process simulators → real connectors/parsers → real fan-in/dedup → real batching writer →
real Postgres via Testcontainers) and asserts on DB rows and the metrics snapshot across five
scenarios: steady load lands in the DB, source drop + reconnect with the other sources unaffected,
duplicates deduped, a DB outage that drives `Dropped` up and backpressure up then recovers, and a
graceful shutdown that drains every accepted tick. DB-backed tests **skip cleanly when Docker is
absent**, so `dotnet test` stays green anywhere.

## Known limitations (intentionally deferred)

- **Forced-drain loss is uncounted.** On the normal shutdown path there is no loss. Only if the
  `DrainTimeout` is *exceeded* and the drain is force-cancelled are the in-flight ticks (accepted by
  the fan-in but unwritten, plus whatever sits in the outbound channel) discarded **without** being
  counted in `Dropped` (which counts only DB-write-failure loss). This is the accepted cost of
  bounding shutdown against a broken DB — a rare, operator-triggered boundary, documented rather than
  papered over.
- **Fault vocabulary is minimal** — only the two faults the spec names (`drop`, `dup on/off`) plus
  `status`/`help`. A richer set (`garbage`, `pause`/`resume`, runtime `rate`) is deferred behind the
  same `CommandParser`/`FaultController` seam, each a one-case addition.
- **No end-to-end coverage for a few spec corners** — each is covered by a narrower unit/integration
  test instead: hung/idle socket (unit-tested on the connector; simulators have no "connect then go
  silent" fault), bad/garbage data (parser rejection is unit-tested per format; no `garbage` fault),
  and sustained 500–1000 ticks/s load with a memory plateau (the e2e runs a short burst, not the load
  target). "Duplicates *after reconnect*" is exercised via the `dup` fault because the simulator
  doesn't replay quotes on reconnect.
- **No orchestration script.** The process-level test stand (`scripts/teststand.*`) is deferred; the
  hermetic xUnit e2e already gates CI, and [`docs/runbook.md`](docs/runbook.md) is the human-driven
  equivalent.
- **No schema migrations.** `db/schema.sql` is applied by ops; the aggregator never creates or
  migrates the table, so a mis-pointed connection string surfaces loudly through the drop counter
  instead of being masked by an auto-create.

## Further reading

- [`docs/spec.md`](docs/spec.md) — the authoritative task and acceptance criteria
- [`docs/plan.md`](docs/plan.md) — phased build plan and status
- [`docs/decisions.md`](docs/decisions.md) — engineering decision log (decision → alternatives → why)
- [`docs/design/`](docs/design/) — per-subsystem overviews with diagrams
- [`docs/runbook.md`](docs/runbook.md) — the end-to-end runbook
