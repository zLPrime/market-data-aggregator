using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Simulators.Faults;

namespace Simulators.Hosting;

/// <summary>
/// The manual fault surface: a background loop reading command lines from stdin and feeding them to
/// the shared <see cref="CommandParser"/>, echoing each result. The HTTP <c>/fault</c> endpoint is
/// the scripted twin; both go through the same parser so manual and scripted control stay identical.
/// </summary>
/// <remarks>
/// A <see cref="BackgroundService"/> so the host tracks and awaits it (no fire-and-forget). It ends
/// on cancellation or when stdin reaches EOF (e.g. input is redirected/closed).
/// </remarks>
public sealed class StdinCommandService(CommandParser parser, ILogger<StdinCommandService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("fault console ready — type 'help' for commands");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var line = await Console.In.ReadLineAsync(stoppingToken);
                if (line is null)
                    break; // stdin closed / EOF — nothing more to read

                Console.Out.WriteLine(parser.Execute(line));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
