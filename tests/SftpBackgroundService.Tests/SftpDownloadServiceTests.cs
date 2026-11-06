using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SftpBackgroundService.Hosting;
using SftpBackgroundService.Options;
using SftpBackgroundService.Processing;
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
    public async Task DownloadedFiles_AreProcessedInOrder()
    {
        var downloader = Substitute.For<ISftpDownloader>();
        downloader.DownloadNewFilesAsync(Arg.Any<CancellationToken>())
            .Returns(new SftpDownloadResult([@"C:\in\a.csv", @"C:\in\b.csv"], 0));
        var processed = new List<string>();
        var allProcessed = new TaskCompletionSource();
        var processor = Substitute.For<IFileProcessor>();
        processor.ProcessAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                processed.Add(call.Arg<string>());
                if (processed.Count == 2) allProcessed.TrySetResult();
                return Task.CompletedTask;
            });
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(downloader, clock, runOnStartup: true, processor);

        await service.StartAsync(CancellationToken.None);
        await allProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal([@"C:\in\a.csv", @"C:\in\b.csv"], processed);
    }

    [Fact]
    public async Task ProcessingFailure_DoesNotSkipRemainingFiles()
    {
        var downloader = Substitute.For<ISftpDownloader>();
        downloader.DownloadNewFilesAsync(Arg.Any<CancellationToken>())
            .Returns(new SftpDownloadResult([@"C:\in\bad.csv", @"C:\in\good.csv"], 0));
        var goodProcessed = new TaskCompletionSource();
        var processor = Substitute.For<IFileProcessor>();
        processor.ProcessAsync(@"C:\in\bad.csv", Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("corrupt"));
        processor.ProcessAsync(@"C:\in\good.csv", Arg.Any<CancellationToken>())
            .Returns(_ => { goodProcessed.TrySetResult(); return Task.CompletedTask; });
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(downloader, clock, runOnStartup: true, processor);

        await service.StartAsync(CancellationToken.None);
        await goodProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(service.ExecuteTask);
        Assert.False(service.ExecuteTask.IsCompleted, "service loop should still be waiting for the next run");
        await service.StopAsync(CancellationToken.None);
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
        IFileProcessor? processor = null)
    {
        var schedule = new ScheduleOptions { RunAt = new TimeOnly(2, 0), TimeZone = "UTC", RunOnStartup = runOnStartup };
        return new SftpDownloadService(
            downloader,
            processor ?? Substitute.For<IFileProcessor>(),
            Microsoft.Extensions.Options.Options.Create(schedule),
            clock,
            NullLogger<SftpDownloadService>.Instance);
    }
}
