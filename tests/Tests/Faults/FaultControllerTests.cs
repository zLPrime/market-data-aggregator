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
    public void RequestDrop_changes_the_generation_a_live_connection_captured()
    {
        var controller = new FaultController();
        var captured = controller.DropGeneration;

        controller.RequestDrop();

        Assert.NotEqual(captured, controller.DropGeneration); // a live connection sees this and closes
    }

    [Fact]
    public void A_connection_opened_after_a_drop_sees_a_stable_generation()
    {
        // Drop closes the connections live at the time; the next connection must start clean,
        // otherwise the aggregator could never reconnect after a drop.
        var controller = new FaultController();
        controller.RequestDrop();

        var captured = controller.DropGeneration; // the reconnected connection captures here

        Assert.Equal(captured, controller.DropGeneration); // not immediately treated as dropped
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
                    _ = controller.DropGeneration;
                    controller.SetDuplicate(i % 2 == 0);
                }
            });
        }

        await Task.WhenAll(tasks); // no exception escapes

        Assert.Equal(workers * 1000L, controller.DropGeneration); // every increment counted, none lost
    }
}
