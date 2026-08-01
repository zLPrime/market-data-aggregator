using Simulators;
using Simulators.Hosting;

// Entry point for one exchange simulator. Usage:
//   dotnet run --project src/Simulators -- --port 9001 --format A [--rate 250] [--seed 42]
var options = SimulatorOptions.Parse(args);

var app = SimulatorApp.Create(options, enableConsoleControl: true);
app.Logger.LogInformation(
    "simulator format {Format} on port {Port} at {Rate} ticks/s", options.Format, options.Port, options.Rate);

await app.RunAsync();
