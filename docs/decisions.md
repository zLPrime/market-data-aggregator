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
