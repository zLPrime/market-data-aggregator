using System.Threading.Channels;
using MarketData.Core.Abstractions;

namespace MarketData.Tests.Fakes;

/// <summary>
/// A hand-driven <see cref="IExchangeConnector"/> for host/pipeline tests. Owns an unbounded channel
/// the test writes ticks into, and mirrors the real connector's channel-ownership contract: it runs
/// until cancelled, then completes the channel <b>exactly once</b> on the way out — so a graceful
/// drain can read every buffered tick before it sees end-of-stream. Optionally faults on start to
/// exercise the pipeline's fault supervision.
/// </summary>
public sealed class FakeExchangeConnector : IExchangeConnector
{
    private readonly Channel<NormalizedTick> _channel =
        Channel.CreateUnbounded<NormalizedTick>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Exception? _fault;

    public FakeExchangeConnector(string source, Exception? fault = null)
    {
        Source = source;
        _fault = fault;
    }

    public string Source { get; }

    public ChannelReader<NormalizedTick> Ticks => _channel.Reader;

    /// <summary>Enqueue a tick as if it had just arrived from the exchange.</summary>
    public ValueTask EmitAsync(NormalizedTick tick) => _channel.Writer.WriteAsync(tick);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_fault is not null)
                throw _fault;

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            // Completed exactly once on exit — never mid-run — exactly as the real connector does,
            // so buffered ticks stay readable for the drain even when this run faulted.
            _channel.Writer.Complete();
        }
    }
}
