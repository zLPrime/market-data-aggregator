# Build Plan — Phased Roadmap

The system is built in phases, each shipped as its own pull request against `master`
(see the contribution workflow in [`../CLAUDE.md`](../CLAUDE.md)). This file is the
durable source of truth for **what the phases are, where we are, and the concurrency
design intent per phase** — so work can be picked up in a fresh session from the repo
alone.

Engineering decisions made along the way are logged separately in
[`decisions.md`](decisions.md).

## Legend
- ✅ done (merged to `master`)
- 🔧 in progress / in review
- ⬜ not started
- 👉 next up

## Progress at a glance

| Phase | Description | Status | PR |
|-------|-------------|--------|----|
| a | Core abstractions (interfaces only) | ✅ | #1 |
| b | WebSocket connector: reconnect + backoff + idle-timeout | ✅ | #2 |
| c | Deduplicator (stress test **before** implementation) | ✅ | #4 |
| d | Batched DB writer (fault-injection test **before** implementation) + fan-in | ✅ | #10 |
| e | Remaining 2–3 exchange simulators + fault control (CLI + HTTP endpoint) | ✅ | #12 |
| f | Aggregator host wiring (composition root) + graceful shutdown / drain | ✅ | #13 |
| g | Monitoring counters + backpressure indicator + console stats line | ✅ | #14 |
| h | Integration test stand: hermetic xUnit e2e (orchestration script deferred) | 🔧 | — |
| i | README | ⬜ | — |

---

## Phase detail (with `Channel<T>` design notes)

Each phase notes where a `Channel<T>` is involved, since the channels are the backbone
of the concurrency model.

### a — Core abstractions ✅
Interfaces only: `IExchangeConnector`, `NormalizedTick`, `IDeduplicator`, `ITickStore`.
- **Channel:** decided the seam — `IExchangeConnector` exposes a
  `ChannelReader<NormalizedTick>` (connector owns its channel); `ITickStore` consumes a
  batch. No channels instantiated yet.
- Decision logged: *connectors own their inbound channel*.

### b — WebSocket connector ✅
One production connector: supervise → connect → read → backoff → reconnect loop, idle
detection, Format A parser, validated against a real loopback fake exchange. Also added
transport (`IWebSocketConnection`) and format (`IMessageParser`) seams.
- **Channel:** per-connector **inbound bounded** `Channel<NormalizedTick>`
  (`SingleWriter`, `SingleReader`, `FullMode.Wait`). Completed exactly once on loop exit,
  never on a socket drop.
- Decisions logged: idle-via-linked-CTS, backoff-reset-on-first-frame, two-seam design.

### c — Deduplicator ✅
Thread-safe `IDeduplicator` filtering duplicates arriving concurrently from multiple
sources. Stress test written BEFORE the implementation (N producers hammering one
deduplicator; assert no duplicates escape and no races), plus a rotation-boundary race test.
- **Key:** `(Source, Ticker, Price, Volume, Timestamp)` — dedup re-sends *within* a source.
- **Window:** two time-bucketed generations (retention 1x–2x), O(1) eviction; `TrackedKeys`
  gauge witnesses bounded memory. Key + window to be documented in the README (Phase i).
- **Fan-in moved to Phase d** — it needs the outbound channel + DB-writer consumer to exist.

### d — Batched DB writer ✅
Persist ticks to Postgres with batching (size + time triggers). **Fault-injection test
written BEFORE the implementation** (DB unavailable / write error → no silent loss).
- **`BatchingTickWriter`** owns the batching (size or time trigger via a per-batch flush CTS)
  and the write-failure policy: **retry with capped backoff up to `MaxWriteAttempts`, then count
  the batch in an explicit `Dropped` gauge** — a bounded-loss decision, never a swallowed
  exception. `Written`/`Dropped` counters feed Phase g. The low-level `NpgsqlTickStore` stays a
  dumb "one binary COPY per batch, throw on failure" adapter (SOLID).
- **`FanIn` (moved from c):** one pump task per connector reader, each draining through the shared
  thread-safe deduplicator into the **shared outbound bounded** channel it owns; output completed
  exactly once on drain/cancel. Concurrent no-loss/no-dup asserted under contention.
- **Channel:** the fan-in owns the shared outbound `Channel<NormalizedTick>`; its fill level is
  the backpressure signal (see Phase g). The writer drains batches from it.
- **DB test:** Testcontainers spins up a throwaway `postgres:17`, verifies a batch round-trips via
  binary COPY, and skips when Docker is absent so `dotnet test` stays green everywhere.
- Decisions logged: write-failure strategy, fan-in owns the outbound channel.

### e — Exchange simulators + fault control ✅
The remaining 2 WebSocket "exchange" simulators (formats **B**, **C**; **A** exists from Phase b),
each in a **distinctly different** format. Each simulator is an **ASP.NET Core Kestrel** host serving
the WebSocket quote feed and the HTTP control endpoint **on one port**.
- **Two symmetric seams.** A new exchange is a new `IQuoteFormatter` here + a new `IMessageParser` on
  the aggregator, and nothing else. A **round-trip test** (`formatter → wire → parser`) pins each
  format's two halves together. Formats: **A** JSON/number/Unix-millis, **B** JSON short-keys
  (`s,p,v,t`)/string/ISO-8601, **C** pipe-delimited/positional/Unix-seconds.
- **Quote source.** `QuoteGenerator` — seeded per-ticker random walk, injectable clock, pure/unit-
  tested. The feed emits one quote per `PeriodicTimer(1/rate)` tick; three simulators at `--rate` sum
  to the 500–1000 ticks/s load scenario. (Batch-per-tick pacing to beat OS timer granularity is
  deferred to Phase h, if a measurement there shows one-per-tick falls short.)
- **Fault control — two surfaces, one brain.** A thread-safe `FaultController` holds the mutable fault
  state; a `CommandParser` turns a text line into a call on it. Two thin adapters feed the *same*
  parser (SOLID):
  - **stdin CLI (manual):** a background `BackgroundService` reads `Console.In`; typing a command
    mutates fault state live and echoes the result.
  - **HTTP endpoint (scripted):** `POST /fault` with the same command as the body, so the Phase h
    test stand drives faults without a TTY. The WS feed and `/fault` share the single Kestrel port.
- **Fault vocabulary (minimal, per spec):** `drop` (force-close → reconnect) and `dup on/off` (re-emit
  each quote → exercises dedup), plus `status`/`help`. **Deferred** behind the same seam for later:
  `garbage on/off`, `pause`/`resume`, runtime `rate <n>`.
- **Channel:** none in the simulators. On the aggregator side each new simulator is a new
  `IMessageParser` + its own per-connector inbound channel — proving extensibility (a new exchange
  changes no existing code).
- Decisions logged: control transport (two surfaces, one controller); minimal vocabulary, drop as a
  generation counter, one-quote-per-tick (batching deferred), and the three format choices.

### f — Aggregator host wiring + graceful shutdown / drain ✅
Wire the aggregator **composition root** in `Program.cs` (currently a stub): read config
(simulator endpoints + DB conn string), build connectors → `FanIn` → `Deduplicator` →
`BatchingTickWriter` → `NpgsqlTickStore`, and run under a host lifetime with `Ctrl+C`
handling. This is the first point the system is manually runnable end-to-end.
On stop: stop intake, complete channels, and flush already-accepted ticks to the DB within
a bounded timeout. No silent loss of in-memory ticks on a clean shutdown.
- **Channel:** orderly completion cascade — inbound writers `Complete()` → dedup drains →
  outbound `Complete()` → DB writer drains remaining batches, all inside a drain timeout.

### g — Monitoring counters + backpressure indicator ✅
Counters for processed / written / dropped ticks (per source and aggregate); log key
events (connect/disconnect/errors). Expose a backpressure gauge. Surface it all to the
**console**: structured `ILogger` events for connect/disconnect/reconnect-with-backoff/
parse-error/batch-written/batch-dropped, plus a **once-per-second stats line** (recv/s,
deduped, written, dropped, channel fill %, tracked keys, conns up) — the live dashboard
watched during manual testing.
- **Channel:** read the outbound channel's occupancy (`Reader.Count` vs capacity) as the
  live backpressure indicator. Per-connector `Received` / `ParseErrors` counters already
  exist from Phase b.

### h — Integration test stand 🔧 IN PROGRESS
The end-to-end harness that runs the **real** aggregator pipeline against **live** in-process
simulators over a real Postgres — the CI-gating proof that the assembled system behaves.
- **Scope (kept deliberately simple):** ship the **hermetic xUnit e2e** only; the shell
  orchestration script is **deferred** (see below). The e2e is the higher-value, non-flaky
  piece and is what `dotnet test` / CI actually run.
- **xUnit e2e (CI-gating):** assembles the *real* graph with no fakes — N in-process
  `SimulatorApp`s (port 0) → real `WebSocketExchangeConnector`s + format parsers → real
  `FanIn`/`Deduplicator` → real `BatchingTickWriter` → real `NpgsqlTickStore` over a
  Testcontainers `postgres:17`. Asserts on DB rows + `PipelineMetricsSource.Capture()`.
  Covers **five** scenarios: (1) steady load lands in the DB (`Written` == rows, all sources
  present); (2) source drop + reconnect via `POST /fault drop`, with the untouched sources'
  connection counts **unchanged** (real isolation, not just "a counter kept climbing");
  (3) duplicates via `dup on` deduped (no duplicate key tuple in the DB, `Deduplicated > 0`);
  (4) DB outage — a store-seam fault makes writes fail, `Dropped` climbs, the outbound belt
  fills (backpressure gauge asserted), then recovery persists again; (5) graceful shutdown
  under load drains every accepted tick. Scenarios 4/5 assert the no-silent-loss accounting
  invariant `Received == Deduplicated + Written + Dropped`. Skips cleanly when Docker is absent.
  - **DB-outage mechanism:** injected at the `ITickStore` seam (a `ToggleableFaultStore`
    wrapping the real store), **not** by stopping the container — Testcontainers publishes an
    ephemeral host port that moves on restart, which the held `NpgsqlDataSource` can't follow.
    Healthy and recovery writes still hit the real database.
- **Known limitations / deferred e2e coverage** (for the Phase i README — each is covered by a
  narrower unit/integration test or needs a simulator feature we deliberately didn't build):
  - **Forced/bounded-deadline drain is untested, and its loss is *uncounted*.** All e2e drains
    are unbounded (`CancellationToken.None`). When the real drain deadline is exceeded, ticks the
    fan-in accepted-but-hadn't-written plus everything in the outbound channel are lost **without**
    appearing in `Dropped` (which counts only DB-write-failure loss). This is the accepted bounded
    hard-stop boundary from the Phase-f drain decision — a real behavior worth documenting.
  - **Hung/idle socket (spec 2.2)** — no e2e; needs a simulator "connect then stop sending" fault.
    Idle-timeout reconnect is unit-tested on the connector.
  - **Bad/garbage data (`ParseErrors` climbs, process survives)** — no e2e; the simulators have no
    `garbage` fault (deferred in Phase e). Parser rejection is unit-tested per format.
  - **Load magnitude + bounded memory (spec 1)** — e2e runs ~300 ticks/s briefly and does not assert
    the 500–1000/s target or that `TrackedKeys` / channel fill plateau under sustained load.
  - **Duplicates *after reconnect*** (literal spec 3) — the simulator doesn't replay quotes on
    reconnect, so a reconnect never *produces* duplicates; dedup is exercised via the `dup` fault.
  - **Concurrent/repeated drops** — e2e drops one source once; repeated drops are covered in
    `SimulatorIntegrationTests` (simulator-only, not through the full pipeline).
  - **Log-event assertions (spec 2.5)** — the e2e uses `NullLogger`; connect/disconnect/error
    logging is exercised where it matters via `RecordingLogger` in the connector/writer unit tests.
- **Deferred — orchestration script** (`scripts/teststand.ps1` + `.sh`): the process-level
  runbook that boots the real host + `docker` Postgres for a human to watch. Left for a
  follow-up because the xUnit e2e already gates CI and the script is shell-fragile; the
  README (Phase i) will document the manual runbook it would automate.
- **TDD note:** this is a **test-only** phase — the e2e exercises already-shipped production
  code, so the red/green split doesn't apply; it ships as test commits after docs.
- **Channel:** none new — this phase only exercises the assembled pipeline.

### i — README ⬜
How to run simulators, aggregator, and the DB. Key engineering decisions and trade-offs:
dedup model (key + window), DB write strategy, DB-error behavior, reconnection. Known
limitations (what was intentionally left out and why).

---

## Grading priorities (drive every trade-off)
See [`../CLAUDE.md`](../CLAUDE.md). In order: (1) multithreading/async correctness,
(2) fault behavior, (3) DB handling, (4) dedup under concurrency, (5) extensibility,
(6) failure-scenario test coverage.
