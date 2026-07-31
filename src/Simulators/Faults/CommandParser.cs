namespace Simulators.Faults;

/// <summary>
/// Turns one line of fault-command text into a call on the <see cref="FaultController"/> and
/// returns a human-readable result. This is the single place command syntax lives, shared by both
/// input adapters (the stdin loop and the HTTP <c>/fault</c> endpoint) so the manual and scripted
/// paths can never diverge (SOLID). Adding a fault command is a new case here plus a controller
/// method — nothing else changes.
/// </summary>
public sealed class CommandParser(FaultController controller)
{
    /// <summary>Parses and applies <paramref name="line"/>, returning the result/echo. Never throws on bad input.</summary>
    public string Execute(string line) => throw new NotImplementedException();
}
