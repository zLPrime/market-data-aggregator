namespace MarketData.Core.Abstractions;

/// <summary>
/// Converts one raw exchange frame into a <see cref="NormalizedTick"/>. This is the
/// extensibility seam (grading priority #5): a new exchange format is a new
/// implementation of this interface — the connector never changes.
/// </summary>
public interface IMessageParser
{
    /// <summary>Short identifier of the wire format this parser handles (e.g. "A").</summary>
    string Format { get; }

    /// <summary>
    /// Attempts to interpret <paramref name="rawMessage"/> as a tick, stamping it with
    /// <paramref name="source"/>.
    /// </summary>
    /// <returns>
    /// <c>true</c> with <paramref name="tick"/> populated when the frame is a tick;
    /// <c>false</c> for a recognized non-tick frame (e.g. a heartbeat/control message)
    /// that should simply be ignored.
    /// </returns>
    /// <exception cref="FormatException">
    /// Thrown when the frame looks like a tick but is malformed. Deliberately an
    /// exception rather than a silent <c>false</c> so the connector can count it as a
    /// parse error — corrupt data must be visible, not silently dropped (spec 2.4).
    /// </exception>
    bool TryParse(string rawMessage, string source, out NormalizedTick tick);
}
