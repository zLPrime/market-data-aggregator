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
| c | Deduplicator (stress test **before** implementation) | 🔧 | — |
| d | Batched DB writer (fault-injection test **before** implementation) + fan-in | 👉 next | — |
| e | Remaining 2–3 exchange simulators + fault-injection endpoint | ⬜ | — |
| f | Graceful shutdown / drain | ⬜ | — |
| g | Monitoring counters + backpressure indicator | ⬜ | — |
| h | README | ⬜ | — |

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

### c — Deduplicator 🔧 IN REVIEW
Thread-safe `IDeduplicator` filtering duplicates arriving concurrently from multiple
sources. Stress test written BEFORE the implementation (N producers hammering one
deduplicator; assert no duplicates escape and no races), plus a rotation-boundary race test.
- **Key:** `(Source, Ticker, Price, Volume, Timestamp)` — dedup re-sends *within* a source.
- **Window:** two time-bucketed generations (retention 1x–2x), O(1) eviction; `TrackedKeys`
  gauge witnesses bounded memory. Key + window to be documented in the README (Phase h).
- **Fan-in moved to Phase d** — it needs the outbound channel + DB-writer consumer to exist.

### d — Batched DB writer ⬜
Persist ticks to Postgres with batching (size + time triggers). **Fault-injection test
written BEFORE the implementation** (DB unavailable / write error → no silent loss).
Write-failure strategy: retry / bounded buffer / explicit dropped-counter — a decision,
not a swallowed exception.
- **Fan-in (moved from c):** merge the N per-connector inbound readers, run each through the
  deduplicator, and write survivors to the shared outbound channel this writer drains.
- **Channel:** consumes the **shared outbound bounded** `Channel<NormalizedTick>`; its
  fill level is the backpressure signal (see Phase g). Drains batches from the reader.

### e — Exchange simulators ⬜
The remaining 2–3 WebSocket "exchange" simulators, each in a **distinctly different**
format (different field names / types / time encodings, e.g. `price` vs `p` vs `last`,
string vs numeric price). Plus a **fault-injection command/endpoint** on each simulator
(force disconnect, emit duplicates) to test aggregator resilience.
- **Channel:** none in the simulators. On the aggregator side each new simulator is a new
  `IMessageParser` + its own per-connector inbound channel — proving extensibility (a new
  exchange changes no existing code).

### f — Graceful shutdown / drain ⬜
On stop: stop intake, complete channels, and flush already-accepted ticks to the DB within
a bounded timeout. No silent loss of in-memory ticks on a clean shutdown.
- **Channel:** orderly completion cascade — inbound writers `Complete()` → dedup drains →
  outbound `Complete()` → DB writer drains remaining batches, all inside a drain timeout.

### g — Monitoring counters + backpressure indicator ⬜
Counters for processed / written / dropped ticks (per source and aggregate); log key
events (connect/disconnect/errors). Expose a backpressure gauge.
- **Channel:** read the outbound channel's occupancy (`Reader.Count` vs capacity) as the
  live backpressure indicator. Per-connector `Received` / `ParseErrors` counters already
  exist from Phase b.

### h — README ⬜
How to run simulators, aggregator, and the DB. Key engineering decisions and trade-offs:
dedup model (key + window), DB write strategy, DB-error behavior, reconnection. Known
limitations (what was intentionally left out and why).

---

## Grading priorities (drive every trade-off)
See [`../CLAUDE.md`](../CLAUDE.md). In order: (1) multithreading/async correctness,
(2) fault behavior, (3) DB handling, (4) dedup under concurrency, (5) extensibility,
(6) failure-scenario test coverage.
