using SftpBackgroundService.Queue;

namespace SftpBackgroundService.Tests;

public class DefaultBackgroundTaskQueueTests
{
    [Fact]
    public async Task DequeuesWorkItemsInFifoOrder()
    {
        var queue = new DefaultBackgroundTaskQueue(capacity: 10);
        var order = new List<int>();

        await queue.QueueBackgroundWorkItemAsync(_ => { order.Add(1); return ValueTask.CompletedTask; });
        await queue.QueueBackgroundWorkItemAsync(_ => { order.Add(2); return ValueTask.CompletedTask; });

        await (await queue.DequeueAsync(CancellationToken.None))(CancellationToken.None);
        await (await queue.DequeueAsync(CancellationToken.None))(CancellationToken.None);

        Assert.Equal([1, 2], order);
    }

    [Fact]
    public async Task DequeueAsync_HonoursCancellationWhenEmpty()
    {
        var queue = new DefaultBackgroundTaskQueue(capacity: 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await queue.DequeueAsync(cts.Token));
    }

    [Fact]
    public async Task QueueBackgroundWorkItemAsync_RejectsNull()
    {
        var queue = new DefaultBackgroundTaskQueue(capacity: 1);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await queue.QueueBackgroundWorkItemAsync(null!));
    }
}
