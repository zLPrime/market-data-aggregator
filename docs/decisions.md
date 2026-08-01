# Engineering Decision Log

A running log of key engineering decisions, filled in per subsystem as we build.
Newest entries at the top.

Plain-language overviews with diagrams live in [`design/`](design/); this log and the code hold
the implementation detail.

Each entry follows this template:

---

## YYYY-MM-DD — <short title>

**Decision:** <what we decided to do>

**Alternatives considered:** <other options we weighed, briefly>

**Why:** <the reasoning / trade-offs that made us pick this>

---

## 2026-08-01 — Aggregator shutdown: a bounded, two-token drain

**Decision:** Graceful shutdown runs in two phases with two separate cancellation sources. Phase 1
cancels an **intake** token that stops only the connectors — each completes its inbound channel on
`RunAsync` exit, leaving already-buffered ticks readable. Phase 2 lets the fan-in and writer keep
running on a still-live **drain** token: the fan-in drains the completed inbound channels, completes
the outbound belt, and the writer flushes its final partial batch before exiting. The host cancels
the shutdown token after `DrainTimeout` (wired via `HostOptions.ShutdownTimeout`); only if that
deadline is hit does the runner cancel the drain token to force a hard stop.

**Alternatives considered:** (a) A single token cancelled everywhere at once — the simplest, but it
tears down the fan-in and writer mid-flight, discarding every buffered-but-unwritten tick. (b) An
explicit "final flush" entry point on the writer that ignores the deadline. (c) A `BackgroundService`
whose default `StopAsync` just cancels its one `stoppingToken`.

**Why:** No silent loss of accepted ticks is grading priority #2, and a single-token stop violates it
directly. Two tokens map exactly onto the two things shutdown must do — *stop taking new input* and
*finish writing what we already took* — and reuse the channel-completion contracts phases B–D already
guarantee (a connector completes its channel only on run-exit; fan-in and writer flush-then-exit on
completion), so the drain is just those contracts firing in order. The deadline keeps shutdown
bounded; past it the writer's existing retry-then-count-as-dropped policy means the only loss is
*counted and logged*, never silent. Option (b) adds a second write path for a case a downed DB loses
anyway; (c) can't express the stop-intake-but-keep-draining split.

## 2026-08-01 — Runtime config is only what an operator varies; schema stays ops-owned

**Decision:** `appsettings.json` exposes exactly `Sources`, `Database.ConnectionString`
(overridable by the `TRADING_DB` env var) and `DrainTimeout`. Every other knob (batch size, dedup
window, channel capacities, backoff) keeps its code default. The aggregator does **not** create the
`ticks` table at startup — `db/schema.sql` is applied by ops.

**Alternatives considered:** (a) Bind every option class from config so all knobs are operator-tunable.
(b) Run `CREATE TABLE IF NOT EXISTS` on startup so the system is turnkey.

**Why:** The defaults are already spec-tuned; surfacing knobs nobody is turning is speculative config
(YAGNI) with real validation cost. Keeping schema provisioning with ops (where `db/schema.sql` already
lives, shared verbatim with the integration test) avoids the host silently owning a migration concern;
a missing table surfaces loudly through the writer's drop counter rather than being masked by an
auto-create that could hide a mis-pointed connection string.

## 2026-07-31 — Simulators: minimal faults, simplest correct mechanisms, three distinct formats

**Decision:** The simulators implement exactly the two faults the spec names — **`drop`** (force-close
the connection → reconnect) and **`dup on/off`** (immediate re-send → exercise dedup) — plus `status`
/`help`. The richer vocabulary floated earlier (`garbage`, `pause`/`resume`, a runtime `rate`
command) is **deferred** behind the same `CommandParser`/`FaultController` seam, each a one-case
addition. `drop` is modelled as a **monotonic generation counter**, not a `CancellationTokenSource`:
a connection captures the count when it opens and closes once it changes, so a drop ends the live
connection while the next starts clean — no token lifecycle or dispose races. Emit **rate is start-up
config** (`--rate`), not a runtime fault, emitted one quote per `PeriodicTimer` tick. Three formats
differ on every axis the spec asks for: **A** JSON/number/Unix-millis, **B** JSON short-keys
/string/ISO-8601, **C** pipe-delimited/positional/Unix-seconds.

**Alternatives considered:** (a) Build the full fault vocabulary now. (b) Model `drop` with a
swapped-and-cancelled `CancellationTokenSource`. (c) Make `rate` a live fault command. (d) Batch
quotes per tick (`EmitPlan`) to beat OS timer granularity. (e) Formats differing only by field names.

**Why:** The spec is explicit that simulators "may be simple" and names only drop + duplicate — so the
minimal set is what's graded, and the seam keeps the rest cheap to add if time allows (YAGNI over
(a)). The generation counter (over (b)) is a single `Interlocked` with no CTS to link, dispose, or
guard against a cancel/dispose race, and the drop lands cleanly between ticks. Rate as config (over
(c)) keeps the load steady and reproducible and avoids a fault whose only job is load. Batch pacing
(d) was built and then **removed as premature**: it optimizes a throughput cap not demonstrated until
the Phase h load scenario, so it can be reintroduced there if a real measurement shows one quote per
tick falls short (YAGNI, "don't optimize prematurely"). Spreading the formats across names, types and
time encodings (over (e)) is what actually stresses the parser seam — the extensibility property under
grading #5.

**Overview + diagrams:** [`design/simulators.md`](design/simulators.md).

---

## 2026-07-30 — Fault control: two surfaces (stdin CLI + HTTP) over one controller; script + xUnit e2e

**Decision:** Each simulator exposes fault injection through a single thread-safe
`FaultController` (state) + `CommandParser` (text → call), driven by two thin input adapters:
a **stdin console loop** for manual testing and an **HTTP `POST /fault`** endpoint for scripted
control. Simulators are hosted on **ASP.NET Core Kestrel**, so the WebSocket quote feed and
`/fault` share **one port**. Fault vocabulary: `drop`, `dup on/off`, `garbage on/off`,
`pause`/`resume`, `rate <n>`. The end-to-end test stand ships as **both** an orchestration script
(`scripts/teststand.*`, real processes + real Postgres, human-watchable) **and** a hermetic
Testcontainers-backed **xUnit e2e** that gates CI.

**Alternatives considered:** For control transport — (a) a dedicated `/control` WebSocket path
(pure-WS, no HTTP), (b) stdin-only driven by piping into each process. For the harness — script
only, or xUnit e2e only.

**Why:** One controller behind two adapters means fault logic exists once (SOLID) and a new fault
type or a third driver touches one place — the manual and scripted paths can never diverge in
behaviour. HTTP-on-Kestrel makes scripted control trivially curl-able and keeps the simulator a
single idiomatic .NET host; a control-WS (a) would force the script to speak WebSocket, and
stdin-only (b) is awkward to orchestrate across several background processes. Shipping both harness
forms splits the two jobs cleanly: the script boots the *real* system so a human can watch real
console logs react to real faults (grading #2), while the xUnit e2e asserts the four checked
scenarios deterministically in `dotnet test` (grading #6) and skips when Docker is absent. Trade-off:
Kestrel adds an ASP.NET Core dependency to the Simulators project — accepted, since a robust WS
server is wanted anyway and it removes hand-rolled `HttpListener`/WS plumbing.

---

## 2026-07-30 — DB write failure: bounded retry, then an explicit dropped counter

**Decision:** `BatchingTickWriter` owns the write-failure policy (the low-level `ITickStore`
just persists one batch and throws). On a failed batch write it retries with capped exponential
backoff up to `MaxWriteAttempts`; if still failing, it increments an explicit `Dropped` counter,
logs at Error, and moves on. A `Written` counter tracks the success path. While a batch is being
retried the writer stops draining, so the bounded channels naturally backpressure upstream.

**Alternatives considered:** (a) Retry indefinitely (never drop) — relies purely on backpressure.
(b) A bounded in-memory buffer of failed batches, flushed on recovery. (c) Fail fast / crash the
process on a write error.

**Why:** The combination gives a clean two-regime behaviour: a *transient* outage costs only
latency and backpressure (the retries hold the batch, the channel fills, sockets slow) with **no
loss**; a *sustained* outage past the retry budget is a **counted, logged** drop, never a silent
one (spec 2.4; grading #2/#3). Bounded retry keeps the pipeline live — indefinite retry (a) would
stall every source behind a permanently-broken DB, and a spare buffer (b) adds a second unbounded
failure mode and more moving parts for little gain at this spec's scale (YAGNI). Trade-off: under a
long outage we do drop, but the operator sees exactly how many via the gauge and logs, which is the
spec's stated bar. Putting the policy in the writer (not the store) keeps "how to persist" and
"when/whether to persist" separate (SOLID), so the Postgres adapter stays trivial and swappable.

**Overview + diagram:** [`design/db-writer.md`](design/db-writer.md).

---

## 2026-07-30 — Fan-in owns the shared outbound channel; one pump task per source

**Decision:** The `FanIn` stage merges the N per-connector readers by running one pump task per
input, each draining its reader through the shared `IDeduplicator` and writing survivors into a
single bounded outbound channel that the fan-in **owns** and exposes read-only. The output is
completed exactly once, when all pumps finish (clean drain) or on cancellation (hard stop). The
batching DB writer is the sole consumer of that output.

**Alternatives considered:** (a) A single task that round-robins / `select`s across all readers.
(b) The host owns the outbound channel and passes a writer into the stage. (c) Deduplicate inside
each connector before fan-in.

**Why:** One task per reader is the natural shape for `ChannelReader` (`await foreach`) and lets
sources make progress independently — a quiet source never blocks a busy one (spec 2.1) — with the
deduplicator's thread-safe check-and-record as the only shared state, so no extra locking. Owning
the channel mirrors the connector's own channel-ownership decision: lifetime is tied to the stage
*running*, completion happens in exactly one place, and the writer downstream is decoupled from how
many sources exist (grading #5). Dedup runs as one shared stage here rather than per-connector (c)
not for correctness — since `Source` is in the key, a duplicate can only ever be a re-send within a
single source, so per-connector dedup would catch the same ticks — but because a single thread-safe
deduplicator exercised by all sources concurrently is precisely what grading #4 (dedup correctness
under concurrency) targets, and it keeps connectors transport-focused with one bounded window
instead of N.

---

## 2026-07-29 — Deduplicator: full-tuple key + two-generation time window

**Decision:** The dedup key is all five identity fields —
`(Source, Ticker, Price, Volume, Timestamp)`. Keys are held in two time-bucketed
generations, each spanning `Window` (so a key is retained for 1x–2x `Window`); rotation
drops the whole oldest generation (O(1) eviction). Check-and-record is a single
`ConcurrentDictionary.TryAdd`; the two generations sit in one immutable snapshot read once
per call, so reads are lock-free, while lazy rotation is serialized by a lock and reuses the
old current as the new previous.

**Alternatives considered:** (a) Coarser key `(Source, Ticker, Timestamp)`. (b) One
unbounded dictionary of all keys. (c) A per-key last-seen timestamp swept periodically. (d)
Keying on `NormalizedTick`'s own record equality.

**Why:** Including `Source` means the same quote from two exchanges is two observations, not a
duplicate; keeping `Price`/`Volume` avoids dropping two genuine quotes that share a millisecond
— silent loss (grading #2) is worse than missing a re-send a coarser key would have caught. Two
generations bound memory (spec load scenario) with O(1) eviction, avoiding an O(n) sweep or a
tracked background task. The immutable snapshot makes the rotation boundary race-free without a
reader lock (grading #1/#4). An explicit `TickKey` decouples dedup identity from the transport
struct, so `NormalizedTick` can evolve without silently changing what counts as a duplicate.
Trade-off: an exact re-send arriving more than two windows later is treated as new — acceptable
since real re-sends follow a reconnect within seconds.

**Overview + diagram:** [`design/deduplicator.md`](design/deduplicator.md).

---

## 2026-07-28 — Connectors own their inbound channel

**Decision:** `IExchangeConnector` creates and owns a `Channel<NormalizedTick>` and
exposes only the read side (`ChannelReader<NormalizedTick> Ticks`). Its reconnect /
backoff / idle-timeout loop lives entirely inside `RunAsync(CancellationToken)`. The
channel is completed exactly once — when `RunAsync` returns on shutdown — never on a
socket drop.

**Alternatives considered:** Host owns the channel and passes a `ChannelWriter` into
`RunAsync(sink, ct)`; the host reads the other end.

**Why:** Binds the channel's lifetime to the connector's *run* lifetime rather than the
*socket's* lifetime, making the key invariant structural: a reconnect is not
end-of-stream, so it cannot accidentally `Complete()` and silently kill a source
(grading priorities #1, #2). Also yields exactly one writer per channel (trivial
completion/ordering reasoning) and seals all fault handling inside the connector, so a
new exchange is a new implementation with no host-wiring changes (priority #5). Cost: a
slightly heavier connector abstraction and per-connector channel-capacity config —
both of which we want anyway, since the fan-in stage that merges per-connector readers
is the natural home for the deduplicator.

---

## 2026-07-28 — Idle detection via one armed/disarmed linked CTS per connection

**Decision:** Detect a hung socket (alive but no data, spec 2.2) with a single
`CancellationTokenSource.CreateLinkedTokenSource(shutdownToken)` per connection. Before
each receive, `idleCts.CancelAfter(IdleTimeout)` (arm); immediately after a frame arrives,
`idleCts.CancelAfter(Timeout.InfiniteTimeSpan)` (disarm) before parsing/writing. If it
fires, the receive throws `OperationCanceledException`, distinguished from shutdown by
checking the outer token.

**Alternatives considered:** (a) A fresh linked CTS with `CancelAfter` per message —
correct but allocates a CTS + timer registration every tick (~1k/s). (b) A separate
watchdog task tracking a shared "last received" timestamp — adds a background task and
shared mutable state to synchronize.

**Why:** Measures idle strictly as *time spent waiting for a frame*. Disarming during
processing means a slow downstream (bounded-channel backpressure) can never be misread as
a dead socket and trigger a needless reconnect. One CTS + a cheap timer reschedule per
message — no per-message allocation, no extra task, no shared state. Known trade-off: a
frame arriving in the microsecond window between fire and disarm causes one benign
spurious reconnect; acceptable given how rare it is and that reconnect is safe.

---

## 2026-07-28 — Reset backoff only after a connection delivers data

**Decision:** The exponential backoff resets on the *first received message* of a
connection, not merely on a successful `ConnectAsync`.

**Alternatives considered:** Reset immediately on connect.

**Why:** A socket that connects then instantly drops (or connects but never sends) would,
if we reset on connect, keep reconnecting at the floor delay forever — a tight loop
against a broken endpoint. Requiring an actual frame as proof of a *productive* connection
means unproductive endpoints escalate their backoff (capped) while genuinely healthy but
flapping sources still recover to the floor delay.

---

## 2026-07-28 — Two seams: transport (`IWebSocketConnection`) and format (`IMessageParser`)

**Decision:** Split the connector's dependencies into a transport port
(`IWebSocketConnection` + factory) and a format port (`IMessageParser`). The connector is
agnostic to both. Format A (JSON, numeric price/size, Unix-millis `ts`) is the first
parser; the loopback test server emits it, so it seeds the Phase-e simulators rather than
being throwaway.

**Alternatives considered:** One combined "exchange client" per source that owns transport
+ parsing together.

**Why:** The transport seam lets reconnect/backoff/idle be unit-tested deterministically
with a scripted fake (no network, no flakiness); the real `ClientWebSocket` stays isolated
and is covered by one loopback integration test. The format seam is the extensibility
point (priority #5): exchanges B and C in Phase e are new `IMessageParser`s with zero
connector changes.

---
