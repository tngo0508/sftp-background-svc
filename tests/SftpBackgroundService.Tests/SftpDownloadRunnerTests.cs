using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SftpBackgroundService.Processing;
using SftpBackgroundService.Sftp;

namespace SftpBackgroundService.Tests;

public class SftpDownloadRunnerTests
{
    [Fact]
    public async Task Success_ProcessesFilesInOrder_AndReturnsZero()
    {
        var downloader = Substitute.For<ISftpDownloader>();
        downloader.DownloadNewFilesAsync(Arg.Any<CancellationToken>())
            .Returns(new SftpDownloadResult([@"C:\in\a.csv", @"C:\in\b.csv"], SkippedCount: 1, FailedCount: 0));
        var processed = new List<string>();
        var processor = Substitute.For<IFileProcessor>();
        processor.ProcessAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => { processed.Add(call.Arg<string>()); return Task.CompletedTask; });

        var exitCode = await CreateRunner(downloader, processor).RunAsync(CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal([@"C:\in\a.csv", @"C:\in\b.csv"], processed);
    }

    [Fact]
    public async Task NothingToDownload_ReturnsZero_WithoutCallingProcessor()
    {
        var downloader = Substitute.For<ISftpDownloader>();
        downloader.DownloadNewFilesAsync(Arg.Any<CancellationToken>())
            .Returns(new SftpDownloadResult([], SkippedCount: 3, FailedCount: 0));
        var processor = Substitute.For<IFileProcessor>();

        var exitCode = await CreateRunner(downloader, processor).RunAsync(CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        await processor.DidNotReceiveWithAnyArgs().ProcessAsync(default!, default);
    }

    [Fact]
    public async Task ConnectionFailure_ReturnsOne_AndSkipsProcessing()
    {
        var downloader = Substitute.For<ISftpDownloader>();
        downloader.DownloadNewFilesAsync(Arg.Any<CancellationToken>())
            .Returns<SftpDownloadResult>(_ => throw new InvalidOperationException("connection refused"));
        var processor = Substitute.For<IFileProcessor>();

        var exitCode = await CreateRunner(downloader, processor).RunAsync(CancellationToken.None);

        Assert.Equal(ExitCodes.DownloadFailed, exitCode);
        await processor.DidNotReceiveWithAnyArgs().ProcessAsync(default!, default);
    }

    [Fact]
    public async Task PartialDownloadFailure_StillProcessesLandedFiles_AndReturnsOne()
    {
        // Files that landed must be processed now; with SkipIfLocalExists they would never be seen again.
        var downloader = Substitute.For<ISftpDownloader>();
        downloader.DownloadNewFilesAsync(Arg.Any<CancellationToken>())
            .Returns(new SftpDownloadResult([@"C:\in\a.csv"], SkippedCount: 0, FailedCount: 1));
        var processor = Substitute.For<IFileProcessor>();

        var exitCode = await CreateRunner(downloader, processor).RunAsync(CancellationToken.None);

        Assert.Equal(ExitCodes.DownloadFailed, exitCode);
        await processor.Received(1).ProcessAsync(@"C:\in\a.csv", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessingFailure_StillProcessesRemainingFiles_AndReturnsTwo()
    {
        var downloader = Substitute.For<ISftpDownloader>();
        downloader.DownloadNewFilesAsync(Arg.Any<CancellationToken>())
            .Returns(new SftpDownloadResult([@"C:\in\bad.csv", @"C:\in\good.csv"], SkippedCount: 0, FailedCount: 0));
        var processor = Substitute.For<IFileProcessor>();
        processor.ProcessAsync(@"C:\in\bad.csv", Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("corrupt"));

        var exitCode = await CreateRunner(downloader, processor).RunAsync(CancellationToken.None);

        Assert.Equal(ExitCodes.SomeFilesFailedProcessing, exitCode);
        await processor.Received(1).ProcessAsync(@"C:\in\good.csv", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadAndProcessingBothFail_ReportsDownloadFailure()
    {
        var downloader = Substitute.For<ISftpDownloader>();
        downloader.DownloadNewFilesAsync(Arg.Any<CancellationToken>())
            .Returns(new SftpDownloadResult([@"C:\in\bad.csv"], SkippedCount: 0, FailedCount: 1));
        var processor = Substitute.For<IFileProcessor>();
        processor.ProcessAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("corrupt"));

        var exitCode = await CreateRunner(downloader, processor).RunAsync(CancellationToken.None);

        Assert.Equal(ExitCodes.DownloadFailed, exitCode);
    }

    [Fact]
    public async Task Cancellation_PropagatesInsteadOfBeingSwallowed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var downloader = Substitute.For<ISftpDownloader>();
        downloader.DownloadNewFilesAsync(Arg.Any<CancellationToken>())
            .Returns<SftpDownloadResult>(call => throw new OperationCanceledException(call.Arg<CancellationToken>()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateRunner(downloader, Substitute.For<IFileProcessor>()).RunAsync(cts.Token));
    }

    private static SftpDownloadRunner CreateRunner(ISftpDownloader downloader, IFileProcessor processor) =>
        new(downloader, processor, NullLogger<SftpDownloadRunner>.Instance);
}
