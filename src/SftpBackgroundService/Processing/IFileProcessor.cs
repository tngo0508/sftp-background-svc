namespace SftpBackgroundService.Processing;

/// <summary>
/// Your business logic goes behind this interface. It is invoked once per downloaded file, in download order,
/// after the SFTP session has closed. The file is complete on disk when it is called.
/// </summary>
public interface IFileProcessor
{
    Task ProcessAsync(string filePath, CancellationToken cancellationToken);
}
