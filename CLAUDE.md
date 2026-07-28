# Trading System — Real-Time Exchange Data Aggregator

Aggregates live quote streams from multiple WebSocket "exchange" simulators:
connects concurrently, normalizes heterogeneous formats, deduplicates,
batches to Postgres, reconnects with backoff, and drains on shutdown.

## Tech stack
- **.NET 10 (LTS)**
- **xUnit** for tests
- **PostgreSQL** via **Npgsql** (batched writes via binary COPY)

## Project layout
```
TradingSystem.slnx            # solution (.NET 10 .slnx format)
global.json                   # pins SDK to 10.0.302
Directory.Build.props         # shared build settings (net10.0, nullable, CS4014-as-error)
src/Core         (Trading.Core)   # core abstractions — no dependencies
src/Aggregator   (Aggregator)     # the aggregator host
src/Simulators   (Simulators)     # the exchange simulators
tests/Tests      (Trading.Tests)  # xUnit tests
```

## Build / run / test
```bash
dotnet build
dotnet test
dotnet run --project src/Aggregator                                    # the aggregator server
dotnet run --project src/Simulators -- --port 9001 --format A          # one simulator
```

## Start the database
```bash
docker run --name trading-db -e POSTGRES_PASSWORD=dev -e POSTGRES_DB=ticks \
  -p 5432:5432 -d postgres:17
```
(Connection string lives in `appsettings.json`; override via the `TRADING_DB` env var.)

## Run the exchange simulators
Each simulator is a WebSocket server emitting quotes in a distinct format
(different field names / types / time formats). Start 2–3 on separate ports:
```bash
dotnet run --project src/Simulators -- --port 9001 --format A
dotnet run --project src/Simulators -- --port 9002 --format B
dotnet run --project src/Simulators -- --port 9003 --format C
```
Fault injection (drop connection / emit duplicates) is triggered via a
control command/endpoint on each simulator — see README once implemented.

## Contribution workflow (one PR per phase)
Every build phase from the plan ships as its own pull request — never commit phase
work directly to the default branch.
- **Base branch:** `master` (this repo's default).
- **Branch per phase:** `phase/<letter>-<slug>` off `master` (e.g. `phase/b-ws-connector`).
- Commit the phase's work, push the branch to `origin`, and open a PR against `master`.
- Keep `master` releasable; each phase's PR should build and pass tests on its own.

## Grading priorities (drive every trade-off, in this order)
1. **Correctness of multithreading/async** — no races, safe shared state,
   disciplined Task / CancellationToken handling.
2. **Fault behavior** — reconnection, no silent data loss, graceful shutdown.
3. **DB handling** — batching, write-error handling.
4. **Deduplication correctness under concurrency.**
5. **Architectural extensibility** — a new exchange = no rewrite of existing code.
6. **Tests** — coverage of failure scenarios, not just happy path.

## Hard rules (non-negotiable)
- No `.Result` / `.Wait()` over async, anywhere.
- No fire-and-forget `Task`s: every background Task is tracked, awaited, and
  its failure logged.
- Every background loop observes a `CancellationToken`.
- No swallowed exceptions: every `catch` block either logs or increments a counter.
