using SftpBackgroundService.Processing;
using SftpBackgroundService.Sftp;

namespace SftpBackgroundService;

/// <summary>Process exit codes. Windows Task Scheduler shows the value as "Last Run Result".</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int DownloadFailed = 1;
    public const int SomeFilesFailedProcessing = 2;
    public const int StartupFailed = 3;
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
            "Download finished. Downloaded {Downloaded}, skipped {Skipped}.",
            result.DownloadedFiles.Count,
            result.Skipped);

        // The SFTP session is already closed here, so slow processing never holds a connection open.
        var failed = 0;
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
                failed++;
                logger.LogError(ex, "Processing {File} failed.", file);
            }
        }

        if (failed > 0)
        {
            logger.LogWarning("Run finished with {Failed} of {Total} files failing to process.",
                failed, result.DownloadedFiles.Count);
            return ExitCodes.SomeFilesFailedProcessing;
        }

        logger.LogInformation("Run finished successfully.");
        return ExitCodes.Success;
    }
}
