using SftpBackgroundService.Processing;
using SftpBackgroundService.Sftp;

namespace SftpBackgroundService;

/// <summary>Process exit codes. Windows Task Scheduler shows the value as "Last Run Result".</summary>
public static class ExitCodes
{
    public const int Success = 0;

    /// <summary>The connection or listing failed, or at least one file could not be downloaded.</summary>
    public const int DownloadFailed = 1;

    public const int SomeFilesFailedProcessing = 2;
    public const int StartupFailed = 3;
    public const int Cancelled = 4;
}

/// <summary>
/// One complete run: download every matching remote file, then process each one in order.
/// Invoked once per process; the scheduling is done by Windows Task Scheduler (or cron).
/// </summary>
public sealed class SftpDownloadRunner(
    ISftpDownloader downloader,
    IFileProcessor fileProcessor,
    ILogger<SftpDownloadRunner> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("SFTP download run started.");

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
            logger.LogError(ex, "SFTP download failed.");
            return ExitCodes.DownloadFailed;
        }

        logger.LogInformation(
            "Download finished. Downloaded {Downloaded}, skipped {Skipped}, failed {Failed}.",
            result.DownloadedFiles.Count,
            result.SkippedCount,
            result.FailedCount);

        // Files that did land are always processed, even when other downloads failed. Otherwise they would
        // sit in the download folder and be skipped as "already present" on every later run.
        // The SFTP session is already closed here, so slow processing never holds a connection open.
        var failedProcessing = 0;
        foreach (var file in result.DownloadedFiles)
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
                failedProcessing++;
                logger.LogError(ex, "Processing {File} failed.", file);
            }
        }

        if (result.FailedCount > 0)
        {
            logger.LogWarning("Run finished with {Failed} download(s) failing; they will be retried on the next run.",
                result.FailedCount);
            return ExitCodes.DownloadFailed;
        }

        if (failedProcessing > 0)
        {
            logger.LogWarning("Run finished with {Failed} of {Total} files failing to process.",
                failedProcessing, result.DownloadedFiles.Count);
            return ExitCodes.SomeFilesFailedProcessing;
        }

        logger.LogInformation("Run finished successfully.");
        return ExitCodes.Success;
    }
}
