namespace SftpBackgroundService.Sftp;

public interface ISftpDownloader
{
    /// <summary>Connects, downloads every matching remote file into the local download folder, and disconnects.</summary>
    Task<SftpDownloadResult> DownloadNewFilesAsync(CancellationToken cancellationToken);
}

/// <param name="DownloadedFiles">Full local paths of the files written during this run.</param>
/// <param name="Skipped">Remote files that matched the pattern but were left alone (already present locally).</param>
public sealed record SftpDownloadResult(IReadOnlyList<string> DownloadedFiles, int Skipped);
