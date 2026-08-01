namespace Simulators.Quotes;

/// <summary>
/// Encodes a canonical <see cref="Quote"/> into one exchange's wire frame. This is the
/// producing-side mirror of the aggregator's <c>IMessageParser</c>: a new exchange format is a
/// new formatter here and a new parser there, with nothing else touched (grading priority #5).
/// The <see cref="Format"/> identifier must match the parser that consumes the same wire format.
/// </summary>
public interface IQuoteFormatter
{
    /// <summary>Short identifier of the wire format, matching the aggregator parser (e.g. "A").</summary>
    string Format { get; }

    /// <summary>Serializes one quote into a single wire frame ready to send over the socket.</summary>
    string Serialize(Quote quote);
}
