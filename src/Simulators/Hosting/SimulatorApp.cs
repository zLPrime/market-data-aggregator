using Microsoft.AspNetCore.Builder;

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
    public static WebApplication Create(SimulatorOptions options, bool enableConsoleControl) =>
        throw new NotImplementedException();
}
