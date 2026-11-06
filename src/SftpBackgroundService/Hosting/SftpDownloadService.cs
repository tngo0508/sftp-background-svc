using SftpBackgroundService.Options;
using SftpBackgroundService.Processing;
using SftpBackgroundService.Scheduling;
using SftpBackgroundService.Sftp;

namespace SftpBackgroundService.Hosting;

/// <summary>
/// Runs the SFTP download once per day at the configured time (and optionally once at startup),
/// then processes each downloaded file in order.
/// </summary>
public sealed class SftpDownloadService(
    ISftpDownloader downloader,
    IFileProcessor fileProcessor,
    IOptions<ScheduleOptions> scheduleOptions,
    TimeProvider timeProvider,
    ILogger<SftpDownloadService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = scheduleOptions.Value;
        var timeZone = DailySchedule.ResolveTimeZone(schedule.TimeZone);

        if (schedule.RunOnStartup)
        {
            await RunOnceAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var nextRun = DailySchedule.NextRun(now, schedule.RunAt, timeZone);
            var delay = nextRun - now;

            logger.LogInformation("Next SFTP download at {NextRun:O} (in {Delay})", nextRun, delay);

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting SFTP download run.");

        SftpDownloadResult result;
        try
        {
            result = await downloader.DownloadNewFilesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed run must not kill the service; the next scheduled run will retry.
            logger.LogError(ex, "SFTP download run failed.");
            return;
        }

        logger.LogInformation(
            "SFTP download run finished. Downloaded {Downloaded}, skipped {Skipped}.",
            result.DownloadedFiles.Count,
            result.Skipped);

        // The SFTP session is already closed at this point, so slow processing never holds a connection open.
        foreach (var file in result.DownloadedFiles)
        {
            await ProcessFileAsync(file, cancellationToken);
        }
    }

    private async Task ProcessFileAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            await fileProcessor.ProcessAsync(file, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One bad file must not stop the remaining files from being processed.
            logger.LogError(ex, "Processing {File} failed.", file);
        }
    }
}
