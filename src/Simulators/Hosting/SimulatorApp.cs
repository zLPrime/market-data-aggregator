using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Simulators.Faults;
using Simulators.Quotes;

namespace Simulators.Hosting;

/// <summary>
/// Composition root for one exchange simulator: builds a Kestrel <see cref="WebApplication"/> that
/// serves the WebSocket quote feed and the <c>POST /fault</c> control endpoint on a single port,
/// with the fault controller, command parser, formatter and feed wired in. Exposed as a builder so
/// the integration test can host a real simulator in-process on an ephemeral port.
/// </summary>
public static class SimulatorApp
{
    /// <summary>
    /// Builds (but does not start) the simulator host. <paramref name="enableConsoleControl"/> adds
    /// the stdin fault console; the integration test leaves it off so it never reads the test runner's
    /// input.
    /// </summary>
    public static WebApplication Create(SimulatorOptions options, bool enableConsoleControl)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<FaultController>();
        builder.Services.AddSingleton<CommandParser>();
        builder.Services.AddSingleton<IQuoteFormatter>(_ => FormatterFor(options.Format));
        builder.Services.AddSingleton<QuoteFeed>();
        if (enableConsoleControl)
            builder.Services.AddHostedService<StdinCommandService>();

        var app = builder.Build();
        app.UseWebSockets();

        // Scripted control: the command body is the same text the stdin console accepts, so both
        // adapters share one parser and can never diverge.
        app.MapPost("/fault", async (HttpContext context, CommandParser parser) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var command = await reader.ReadToEndAsync(context.RequestAborted);
            return Results.Text(parser.Execute(command));
        });

        // The quote feed: a WebSocket upgrade on the root path. The app-stopping token flows into
        // the feed so shutdown drains the connection cleanly.
        app.MapGet("/", async (HttpContext context, QuoteFeed feed, IHostApplicationLifetime lifetime) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await feed.RunAsync(socket, lifetime.ApplicationStopping);
        });

        return app;
    }

    private static IQuoteFormatter FormatterFor(string format) => format switch
    {
        "A" => new FormatAFormatter(),
        "B" => new FormatBFormatter(),
        "C" => new FormatCFormatter(),
        _ => throw new ArgumentException($"unknown format '{format}' (expected A, B or C)", nameof(format)),
    };
}
