using Simulators.Faults;

namespace Trading.Tests.Faults;

public sealed class FaultControllerTests
{
    [Fact]
    public void Duplicate_flag_toggles()
    {
        var controller = new FaultController();

        Assert.False(controller.DuplicateEnabled); // off by default

        controller.SetDuplicate(true);
        Assert.True(controller.DuplicateEnabled);

        controller.SetDuplicate(false);
        Assert.False(controller.DuplicateEnabled);
    }

    [Fact]
    public void RequestDrop_cancels_a_token_captured_before_it()
    {
        var controller = new FaultController();
        var token = controller.DropToken;

        controller.RequestDrop();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void A_token_captured_after_a_drop_is_not_cancelled()
    {
        // Drop closes the connections live at the time; the next connection must start clean,
        // otherwise the aggregator could never reconnect after a drop.
        var controller = new FaultController();
        controller.RequestDrop();

        var reconnectToken = controller.DropToken;

        Assert.False(reconnectToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Concurrent_drops_and_reads_are_safe()
    {
        // Faults arrive on stdin + HTTP while the feed reads state — hammer the controller from
        // many threads and assert it never throws and settles into a clean, uncancelled state.
        var controller = new FaultController();
        const int workers = 8;
        using var barrier = new Barrier(workers);

        var tasks = new Task[workers];
        for (var w = 0; w < workers; w++)
        {
            tasks[w] = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (var i = 0; i < 1000; i++)
                {
                    controller.RequestDrop();
                    _ = controller.DropToken.IsCancellationRequested;
                    controller.SetDuplicate(i % 2 == 0);
                }
            });
        }

        await Task.WhenAll(tasks); // no exception escapes

        Assert.False(controller.DropToken.IsCancellationRequested); // last swap left a fresh source
    }
}
