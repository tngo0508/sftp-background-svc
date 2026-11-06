namespace SftpBackgroundService.Queue;

/// <summary>Producer/consumer hand-off between the scheduler (producer) and the single background consumer.</summary>
public interface IBackgroundTaskQueue
{
    ValueTask QueueBackgroundWorkItemAsync(Func<CancellationToken, ValueTask> workItem);

    ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(CancellationToken cancellationToken);
}
