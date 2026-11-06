using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SftpBackgroundService.Hosting;
using SftpBackgroundService.Options;
using SftpBackgroundService.Processing;
using SftpBackgroundService.Queue;
using SftpBackgroundService.Sftp;

namespace SftpBackgroundService.Tests;

public class SftpDownloadServiceTests
{
    private static readonly SftpDownloadResult EmptyResult = new([], 0);

    [Fact]
    public async Task RunOnStartup_TriggersDownloadImmediately()
    {
        var downloader = Substitute.For<ISftpDownloader>();
        downloader.DownloadNewFilesAsync(Arg.Any<CancellationToken>()).Returns(EmptyResult);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(downloader, clock, runOnStartup: true);

        await service.StartAsync(CancellationToken.None);
        await Task.Yield();
        await service.StopAsync(CancellationToken.None);

        await downloader.Received(1).DownloadNewFilesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WaitsUntilScheduledTime_ThenDownloads()
    {
        var downloader = Substitute.For<ISftpDownloader>();
        var downloadStarted = new TaskCompletionSource();
        downloader.DownloadNewFilesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { downloadStarted.TrySetResult(); return EmptyResult; });
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 1, 0, 0, TimeSpan.Zero));
        var service = CreateService(downloader, clock, runOnStartup: false);

        await service.StartAsync(CancellationToken.None);
        await Task.Yield();
        await downloader.DidNotReceive().DownloadNewFilesAsync(Arg.Any<CancellationToken>());

        clock.Advance(TimeSpan.FromMinutes(59));
        await Task.Yield();
        await downloader.DidNotReceive().DownloadNewFilesAsync(Arg.Any<CancellationToken>());

        clock.Advance(TimeSpan.FromMinutes(1));
        await downloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        await downloader.Received(1).DownloadNewFilesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadedFiles_AreQueuedForProcessing()
    {
        var downloader = Substitute.For<ISftpDownloader>();
        downloader.DownloadNewFilesAsync(Arg.Any<CancellationToken>())
            .Returns(new SftpDownloadResult([@"C:\in\a.csv", @"C:\in\b.csv"], 0));
        var processor = Substitute.For<IFileProcessor>();
        var queue = new DefaultBackgroundTaskQueue(capacity: 10);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(downloader, clock, runOnStartup: true, queue, processor);

        await service.StartAsync(CancellationToken.None);
        await Task.Yield();

        // Drain the queue the way QueuedHostedService would.
        await (await queue.DequeueAsync(CancellationToken.None))(CancellationToken.None);
        await (await queue.DequeueAsync(CancellationToken.None))(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        await processor.Received(1).ProcessAsync(@"C:\in\a.csv", Arg.Any<CancellationToken>());
        await processor.Received(1).ProcessAsync(@"C:\in\b.csv", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadFailure_DoesNotStopTheService()
    {
        var downloader = Substitute.For<ISftpDownloader>();
        downloader.DownloadNewFilesAsync(Arg.Any<CancellationToken>())
            .Returns<SftpDownloadResult>(_ => throw new InvalidOperationException("boom"));
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(downloader, clock, runOnStartup: true);

        await service.StartAsync(CancellationToken.None);
        await Task.Yield();

        Assert.NotNull(service.ExecuteTask);
        Assert.False(service.ExecuteTask.IsCompleted, "service loop should still be waiting for the next run");
        await service.StopAsync(CancellationToken.None);
    }

    private static SftpDownloadService CreateService(
        ISftpDownloader downloader,
        TimeProvider clock,
        bool runOnStartup,
        IBackgroundTaskQueue? queue = null,
        IFileProcessor? processor = null)
    {
        var schedule = new ScheduleOptions { RunAt = new TimeOnly(2, 0), TimeZone = "UTC", RunOnStartup = runOnStartup };
        return new SftpDownloadService(
            downloader,
            queue ?? new DefaultBackgroundTaskQueue(capacity: 10),
            processor ?? Substitute.For<IFileProcessor>(),
            Microsoft.Extensions.Options.Options.Create(schedule),
            clock,
            NullLogger<SftpDownloadService>.Instance);
    }
}
