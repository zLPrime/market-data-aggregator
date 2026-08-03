using System.Threading.Channels;

namespace MarketData.Core.Abstractions;

/// <summary>
/// A live connection to one exchange simulator. Each connector runs independently:
/// a fault on one source must never stall the others (spec 2.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>The connector owns its outbound channel</b> and exposes only the read side
/// via <see cref="Ticks"/>. This is the central design decision of the pipeline:
/// the channel's lifetime is bound to the connector's <em>run</em> lifetime, NOT to
/// the underlying socket's lifetime. A socket drop is a transient event handled by
/// the internal reconnect loop — it must never <c>Complete()</c> the channel, or the
/// source would be silently killed forever. The channel is completed exactly once,
/// when <see cref="RunAsync"/> returns on shutdown.
/// </para>
/// <para>
/// Consequences that fall out of this shape:
/// there is exactly one writer per channel (the connector's read loop), so channel
/// completion and ordering are trivial to reason about; and all reconnect / backoff /
/// idle-timeout logic stays sealed inside the connector, so adding a new exchange is
/// a new implementation of this interface with no change to the host wiring.
/// </para>
/// </remarks>
public interface IExchangeConnector
{
    /// <summary>
    /// Stable identifier of this exchange, stamped onto every
    /// <see cref="NormalizedTick.Source"/> this connector emits. Also used as the
    /// key for per-source logging and metrics.
    /// </summary>
    string Source { get; }

    /// <summary>
    /// The read side of the connector-owned inbound stream of normalized ticks.
    /// Completes only when <see cref="RunAsync"/> has returned and the last buffered
    /// tick has been read (enabling an orderly drain on shutdown).
    /// </summary>
    ChannelReader<NormalizedTick> Ticks { get; }

    /// <summary>
    /// Runs the connect → read → (on drop) backoff → reconnect loop until
    /// <paramref name="cancellationToken"/> is signalled. Repeatable reconnection is
    /// required (spec 2.2) — this loop reconnects many times over its lifetime.
    /// </summary>
    /// <remarks>
    /// Contract:
    /// <list type="bullet">
    ///   <item>Does not throw on transient socket errors — it logs and reconnects.</item>
    ///   <item>Returns (completing <see cref="Ticks"/>) only on cancellation or an
    ///   unrecoverable condition; a normal shutdown cancels the token.</item>
    ///   <item>Observes <paramref name="cancellationToken"/> in every wait, including
    ///   backoff delays (hard rule: every background loop observes a token).</item>
    /// </list>
    /// </remarks>
    Task RunAsync(CancellationToken cancellationToken);
}
