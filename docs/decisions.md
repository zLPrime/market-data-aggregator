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
