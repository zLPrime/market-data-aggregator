using Simulators.Faults;

namespace Trading.Tests.Faults;

public sealed class CommandParserTests
{
    private readonly FaultController _controller = new();
    private CommandParser Parser => new(_controller);

    [Fact]
    public void Drop_command_forces_a_drop()
    {
        var token = _controller.DropToken;

        var result = Parser.Execute("drop");

        Assert.True(token.IsCancellationRequested);
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
    public void Unrecognized_or_empty_input_returns_a_message_without_throwing(string line)
    {
        var result = Parser.Execute(line);

        Assert.False(string.IsNullOrWhiteSpace(result)); // a helpful message, never an exception
    }
}
