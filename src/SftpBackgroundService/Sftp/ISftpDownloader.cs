namespace SftpBackgroundService.Sftp;

/// <summary>Downloads every new matching file from the remote directory in one pass.</summary>
public interface ISftpDownloader
{
    /// <summary>
    /// Connects, downloads each new matching file, and disconnects. A connection or listing failure throws.
    /// A failure on an individual file is logged and counted in <see cref="SftpDownloadResult.FailedCount"/>;
    /// the other files are still downloaded and returned.
    /// </summary>
    Task<SftpDownloadResult> DownloadNewFilesAsync(CancellationToken cancellationToken);
}

/// <param name="DownloadedFiles">Full local paths of the files that landed in the download folder, in download order.</param>
/// <param name="SkippedCount">Remote files that already existed locally and were not downloaded.</param>
/// <param name="FailedCount">Remote files whose download failed and will be retried on the next run.</param>
public sealed record SftpDownloadResult(IReadOnlyList<string> DownloadedFiles, int SkippedCount, int FailedCount);
