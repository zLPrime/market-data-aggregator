using Aggregator.Hosting;
using Microsoft.Extensions.Hosting;

// Composition root for the aggregator. Reads appsettings.json (+ TRADING_DB), connects to the
// configured exchange simulators, deduplicates and batches quotes into Postgres, and drains
// cleanly on Ctrl+C. See docs/design/aggregator-host.md.
var host = AggregatorHost.Build(args);
await host.RunAsync();
