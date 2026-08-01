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
    private const string Help = "commands: drop | dup on|off | status | help";

    /// <summary>Parses and applies <paramref name="line"/>, returning the result/echo. Never throws on bad input.</summary>
    public string Execute(string line)
    {
        var tokens = (line ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return "empty command; type 'help'";

        var command = tokens[0].ToLowerInvariant();
        return command switch
        {
            "drop" => Drop(),
            "dup" => Toggle("dup", tokens, controller.SetDuplicate),
            "status" => Status(),
            "help" => Help,
            _ => $"unknown command '{command}'; type 'help'",
        };
    }

    private string Drop()
    {
        controller.RequestDrop();
        return "dropped live connection(s)";
    }

    private string Status() => $"dup={(controller.DuplicateEnabled ? "on" : "off")}";

    /// <summary>Applies an <c>&lt;name&gt; on|off</c> command, or returns usage text for anything else.</summary>
    private static string Toggle(string name, string[] tokens, Action<bool> set)
    {
        var arg = tokens.Length == 2 ? tokens[1].ToLowerInvariant() : null;
        if (arg is not ("on" or "off"))
            return $"usage: {name} on|off";

        var on = arg == "on";
        set(on);
        return $"{name} {(on ? "on" : "off")}";
    }
}
