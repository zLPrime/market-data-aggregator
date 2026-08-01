using Simulators.Faults;

namespace Trading.Tests.Faults;

public sealed class CommandParserTests
{
    private readonly FaultController _controller = new();
    private CommandParser Parser => new(_controller);

    [Fact]
    public void Drop_command_forces_a_drop()
    {
        var captured = _controller.DropGeneration;

        var result = Parser.Execute("drop");

        Assert.NotEqual(captured, _controller.DropGeneration);
        Assert.Contains("drop", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("dup on", true)]
    [InlineData("dup off", false)]
    [InlineData("DUP ON", true)]   // case-insensitive
    public void Dup_command_toggles_the_duplicate_flag(string line, bool expected)
    {
        Parser.Execute(line);

        Assert.Equal(expected, _controller.DuplicateEnabled);
    }

    [Theory]
    [InlineData("dup")]        // missing argument
    [InlineData("dup maybe")]  // invalid argument
    public void Malformed_dup_command_reports_usage_and_leaves_state_unchanged(string line)
    {
        var result = Parser.Execute(line);

        Assert.False(_controller.DuplicateEnabled);
        Assert.Contains("usage", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Status_command_reflects_current_state()
    {
        _controller.SetDuplicate(true);

        var result = Parser.Execute("status");

        Assert.Contains("dup", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("on", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Help_command_lists_the_commands()
    {
        var result = Parser.Execute("help");

        Assert.Contains("drop", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dup", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bogus")]
    public void Unrecognized_or_empty_input_points_to_help_without_throwing(string line)
    {
        var result = Parser.Execute(line); // never throws — both adapters feed it arbitrary text

        Assert.Contains("help", result, StringComparison.OrdinalIgnoreCase); // steers the user, unlike a valid command
    }
}
