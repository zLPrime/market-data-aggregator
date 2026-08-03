using Microsoft.Extensions.Hosting;

namespace MarketData.Tests.Fakes;

/// <summary>
/// Minimal <see cref="IHostApplicationLifetime"/> for tests: records whether the pipeline asked the
/// host to stop (its response to an unexpected stage fault) and surfaces the stopping token.
/// </summary>
public sealed class FakeApplicationLifetime : IHostApplicationLifetime, IDisposable
{
    private readonly CancellationTokenSource _started = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _stopped = new();

    /// <summary>True once <see cref="StopApplication"/> has been called.</summary>
    public bool StopRequested { get; private set; }

    public CancellationToken ApplicationStarted => _started.Token;
    public CancellationToken ApplicationStopping => _stopping.Token;
    public CancellationToken ApplicationStopped => _stopped.Token;

    public void StopApplication()
    {
        StopRequested = true;
        _stopping.Cancel();
    }

    public void Dispose()
    {
        _started.Dispose();
        _stopping.Dispose();
        _stopped.Dispose();
    }
}
