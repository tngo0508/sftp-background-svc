namespace SftpBackgroundService.Processing;

/// <summary>Your business logic goes behind this interface. It is invoked once per downloaded file, on the queue consumer.</summary>
public interface IFileProcessor
{
    Task ProcessAsync(string filePath, CancellationToken cancellationToken);
}
