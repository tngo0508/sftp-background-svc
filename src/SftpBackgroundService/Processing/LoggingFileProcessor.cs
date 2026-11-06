namespace SftpBackgroundService.Processing;

/// <summary>Placeholder processor: logs the file. Replace or decorate with parsing, database import, archiving, etc.</summary>
public sealed class LoggingFileProcessor(ILogger<LoggingFileProcessor> logger) : IFileProcessor
{
    public Task ProcessAsync(string filePath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(filePath);
        logger.LogInformation("Processing {File} ({Size} bytes, modified {Modified:O})",
            info.Name, info.Exists ? info.Length : 0, info.LastWriteTimeUtc);
        return Task.CompletedTask;
    }
}
