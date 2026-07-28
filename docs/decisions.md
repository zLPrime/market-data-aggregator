# Engineering Decision Log

A running log of key engineering decisions, filled in per subsystem as we build.
Newest entries at the top.

Each entry follows this template:

---

## YYYY-MM-DD — <short title>

**Decision:** <what we decided to do>

**Alternatives considered:** <other options we weighed, briefly>

**Why:** <the reasoning / trade-offs that made us pick this>

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
