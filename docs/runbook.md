# End-to-End Runbook

How to go from a fresh clone to **watching the system work** — steady flow into Postgres, a source
drop and reconnect, duplicates filtered out, and a clean drain on shutdown.

Two ways to do it:

- **A. Automated** — one command, hermetic, proves the assembled pipeline. Start here.
- **B. Manual** — run the real processes and watch them react to faults you inject by hand.

---

## Prerequisites

- **.NET 10 SDK** — `dotnet --version` should print `10.0.302` (pinned in `global.json`).
- **Docker** — running. Used for Postgres and the DB-backed tests.

Faults in the manual walkthrough are typed straight into each simulator's terminal — no extra tools.
Shell commands below are run from the repository root; on native Windows PowerShell, replace
`< file` redirects with `Get-Content file |`.

---

## A. Automated end-to-end test

The hermetic end-to-end test assembles the **real** pipeline — in-process simulators over real
WebSockets → real connectors and parsers → real fan-in/deduplicator → real batching writer → real
Postgres in a throwaway Testcontainers container — and asserts on the database rows and the metrics
snapshot.

```bash
dotnet test
```

With Docker running, the end-to-end scenarios execute; without it, they **skip cleanly** and the rest
of the suite still passes. The end-to-end scenarios covered:

1. steady load lands in the DB — `Written` equals the row count, all sources present;
2. a source is dropped (`POST /fault drop`) and reconnects, while the **other** sources' connection
   counts stay unchanged (real isolation, not just a counter that kept climbing);
3. duplicates (`dup on`) are filtered — no duplicate key tuple in the DB, `Deduplicated > 0`;
4. a DB outage drives `Dropped` up and the backpressure gauge up, then recovery persists again;
5. graceful shutdown under load drains every accepted tick.

Scenarios 4 and 5 assert the no-silent-loss invariant `Received == Deduplicated + Written + Dropped`.

That's the CI-gating proof. The manual walkthrough below is for *seeing* it happen.

---

## B. Manual walkthrough

You'll need **five terminals**: one for the database step, three for simulators, one for the
aggregator. (Simulators and the aggregator run in the foreground and print logs.)

### 1. Start Postgres and create the table

```bash
docker run --name trading-db -e POSTGRES_PASSWORD=dev -e POSTGRES_DB=ticks -p 5432:5432 -d postgres:17
docker exec -i trading-db psql -U postgres -d ticks < db/schema.sql
```

The second command should print `CREATE TABLE`. The connection string in
`src/Aggregator/appsettings.json` already matches this container (`localhost:5432`, db `ticks`, user
`postgres`, password `dev`); override it with the `TRADING_DB` env var if yours differs.

### 2. Start the three simulators

Each in its own terminal. They serve the WebSocket feed and the `/fault` control endpoint on one port.

```bash
dotnet run --project src/Simulators -- --port 9001 --format A
```
```bash
dotnet run --project src/Simulators -- --port 9002 --format B
```
```bash
dotnet run --project src/Simulators -- --port 9003 --format C
```

Each logs `simulator format X on port 900N at 250 ticks/s` and then `fault console ready — type
'help' for commands`. That console is how you inject faults in steps 4–5: just type a command into
the simulator's terminal and press Enter; it echoes the result. Three simulators at 250/s sum to
~750 ticks/s — inside the spec's 500–1000/s load scenario.

### 3. Start the aggregator

```bash
dotnet run --project src/Aggregator
```

Within a second you'll see connect logs for all three sources, then a **stats line once per second**:

```
stats recv/s=742 recv=51234 parse=0 dedup=1180 written=50054 dropped=0 fill=3% keys=6 conns=3/3
```

Read it as: ~742 ticks/s in, `parse` errors 0, `dedup` filtered so far, `written` to the DB,
`dropped` (0 = no loss), outbound channel `fill` 3% (backpressure), `keys` tracked by the
deduplicator (bounded — it plateaus, it doesn't grow forever), `conns` live/configured. **Healthy
signs:** `conns=3/3`, `dropped=0`, `parse=0`, and `keys`/`fill` holding steady rather than climbing.

Confirm rows are actually landing:

```bash
docker exec trading-db psql -U postgres -d ticks -c "SELECT count(*), count(distinct source) FROM ticks;"
```

The count rises between runs and `distinct source` is `3` — all formats normalized and stored.

### 4. Inject a drop → watch reconnect + source isolation

In **simulator A's terminal**, type `drop` and press Enter — it echoes `dropped live connection(s)`.

In the aggregator log: source `exchange-a` disconnects, then **reconnects with backoff** — while
`exchange-b` and `exchange-c` keep flowing (their counts never dip). The stats line returns to
`conns=3/3`. Type `drop` a few more times; reconnect works repeatedly, not just once.

### 5. Inject duplicates → watch deduplication

In **simulator B's terminal**, type `dup on` (it echoes `dup on`) to make it re-emit every quote twice.

`dedup` on the stats line climbs faster (duplicates are being filtered), while `written` still counts
only unique ticks — the duplicates never reach the database. Type `dup off` to stop.

Verify the DB holds **no** duplicate identity tuple:

```bash
docker exec trading-db psql -U postgres -d ticks -c \
  "SELECT source,ticker,price,volume,ts,count(*) FROM ticks GROUP BY 1,2,3,4,5 HAVING count(*)>1;"
```

Zero rows returned = deduplication held under a live duplicate stream.

Other console commands: `status` (reports whether `dup` is on) and `help`. The exact same commands
are also reachable over HTTP (`POST /fault` with the command as the body) — that's how the automated
tests drive faults without a terminal.

### 6. (Optional) See DB-error handling

Stop Postgres while the aggregator runs:

```bash
docker stop trading-db
```

The writer retries with backoff (channel `fill` rises — backpressure holding data, not losing it);
past the retry budget, `dropped` starts counting **and it's logged** — loss is counted, never silent.
Bring it back:

```bash
docker start trading-db
```

Writes resume and `dropped` stops climbing.

### 7. Graceful shutdown → drain

Press **Ctrl+C** in the aggregator terminal. It stops taking new input, drains the ticks already
accepted, flushes the final batch, and prints a last stats line — all within `DrainTimeout` (10s by
default). On this clean path, `Received == Deduplicated + Written + Dropped`: no accepted tick is
lost. Stop each simulator with Ctrl+C.

### 8. Clean up

```bash
docker rm -f trading-db
```

---

## Troubleshooting

- **`conns=0/3` or connect errors** — a simulator isn't up on that port, or a firewall is blocking
  `127.0.0.1:900N`. Check the simulator terminals started cleanly.
- **`dropped` climbing from the start** — the aggregator can't reach Postgres. Confirm the container
  is up (`docker ps`) and the table exists (step 1); check `TRADING_DB` if you overrode it.
- **`relation "ticks" does not exist`** in the drop logs — the schema step was skipped; re-run step 1.
  The aggregator does not create the table on purpose.
- **`dotnet test` end-to-end scenarios skipped** — Docker isn't running. That's expected and not a
  failure; start Docker to exercise them.
- **Typed fault command does nothing** — make sure you typed it into a *simulator* terminal (not the
  aggregator's), and that the simulator printed `fault console ready` at startup.
