using System.IO.Enumeration;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using SftpBackgroundService.Options;

namespace SftpBackgroundService.Sftp;

/// <summary>
/// SSH.NET-based downloader. Each file is written to a staging folder and then moved into the
/// download folder in one step, so nothing else ever observes a half-written file.
/// A failure on one file is logged and counted; the remaining files are still downloaded.
/// </summary>
public sealed class SftpDownloader(
    IOptions<SftpOptions> options,
    ILogger<SftpDownloader> logger) : ISftpDownloader
{
    private const string StagingFolderName = ".staging";
    private const string PartialExtension = ".part";

    public async Task<SftpDownloadResult> DownloadNewFilesAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var downloadDir = Path.GetFullPath(settings.LocalDownloadDirectory);
        var stagingDir = Path.Combine(downloadDir, StagingFolderName);
        Directory.CreateDirectory(downloadDir);
        Directory.CreateDirectory(stagingDir);
        RemoveStalePartialFiles(stagingDir);

        var downloaded = new List<string>();
        var skipped = 0;
        var failed = 0;

        using var client = new SftpClient(BuildConnectionInfo(settings));
        client.HostKeyReceived += (_, e) => VerifyHostKey(e, settings.HostKeyFingerprint);

        logger.LogInformation("Connecting to sftp://{User}@{Host}:{Port}{RemoteDir}",
            settings.Username, settings.Host, settings.Port, settings.RemoteDirectory);

        await client.ConnectAsync(cancellationToken);
        try
        {
            await foreach (var entry in client.ListDirectoryAsync(settings.RemoteDirectory, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!entry.IsRegularFile ||
                    !FileSystemName.MatchesSimpleExpression(settings.FilePattern, entry.Name, ignoreCase: true))
                {
                    continue;
                }

                var finalPath = Path.Combine(downloadDir, entry.Name);
                if (settings.SkipIfLocalExists && File.Exists(finalPath))
                {
                    logger.LogDebug("Skipping {File}: already present locally.", entry.Name);
                    skipped++;
                    continue;
                }

                try
                {
                    await DownloadOneAsync(client, entry, stagingDir, finalPath, settings.DeleteRemoteAfterDownload, cancellationToken);
                    downloaded.Add(finalPath);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One bad transfer must not lose the files that already landed; the runner still processes them.
                    failed++;
                    logger.LogError(ex, "Downloading {File} failed.", entry.Name);
                }
            }
        }
        finally
        {
            client.Disconnect();
        }

        return new SftpDownloadResult(downloaded, skipped, failed);
    }

    private async Task DownloadOneAsync(
        SftpClient client,
        ISftpFile entry,
        string stagingDir,
        string finalPath,
        bool deleteRemote,
        CancellationToken cancellationToken)
    {
        var stagingPath = Path.Combine(stagingDir, entry.Name + PartialExtension);
        logger.LogInformation("Downloading {File} ({Size} bytes)", entry.Name, entry.Length);

        try
        {
            await using (var output = new FileStream(
                             stagingPath, FileMode.Create, FileAccess.Write, FileShare.None,
                             bufferSize: 81920, useAsync: true))
            {
                await client.DownloadFileAsync(entry.FullName, output, cancellationToken);
            }

            File.Move(stagingPath, finalPath, overwrite: true);
        }
        catch
        {
            // Never leave a half-written .part behind; the file is retried on the next run.
            File.Delete(stagingPath);
            throw;
        }

        if (deleteRemote)
        {
            await client.DeleteFileAsync(entry.FullName, cancellationToken);
            logger.LogDebug("Deleted remote {File}", entry.FullName);
        }
    }

    private void RemoveStalePartialFiles(string stagingDir)
    {
        // Leftovers from a run that was killed mid-transfer. Each is re-downloaded by this run.
        foreach (var stale in Directory.EnumerateFiles(stagingDir, "*" + PartialExtension))
        {
            logger.LogWarning("Removing stale partial download {File}.", stale);
            File.Delete(stale);
        }
    }

    private static ConnectionInfo BuildConnectionInfo(SftpOptions settings)
    {
        var methods = new List<AuthenticationMethod>();

        if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrEmpty(settings.PrivateKeyPassphrase)
                ? new PrivateKeyFile(settings.PrivateKeyPath)
                : new PrivateKeyFile(settings.PrivateKeyPath, settings.PrivateKeyPassphrase);
            methods.Add(new PrivateKeyAuthenticationMethod(settings.Username, keyFile));
        }

        if (!string.IsNullOrEmpty(settings.Password))
        {
            methods.Add(new PasswordAuthenticationMethod(settings.Username, settings.Password));
        }

        return new ConnectionInfo(settings.Host, settings.Port, settings.Username, methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds),
        };
    }

    private void VerifyHostKey(HostKeyEventArgs e, string? expectedFingerprint)
    {
        if (string.IsNullOrWhiteSpace(expectedFingerprint))
        {
            // No pin configured: trust on first use. Set Sftp:HostKeyFingerprint in production.
            logger.LogWarning(
                "No Sftp:HostKeyFingerprint configured. Server key SHA256:{Fingerprint} accepted without verification.",
                e.FingerPrintSHA256);
            e.CanTrust = true;
            return;
        }

        var expected = expectedFingerprint.Trim();
        if (expected.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
        {
            expected = expected["SHA256:".Length..];
        }

        e.CanTrust = string.Equals(e.FingerPrintSHA256, expected, StringComparison.Ordinal);
        if (!e.CanTrust)
        {
            logger.LogError("Host key mismatch. Expected SHA256:{Expected} but server presented SHA256:{Actual}.",
                expected, e.FingerPrintSHA256);
        }
    }
}
