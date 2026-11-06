namespace SftpBackgroundService.Options;

/// <summary>Cross-field checks that data annotations cannot express.</summary>
public sealed class SftpOptionsValidator : IValidateOptions<SftpOptions>
{
    public ValidateOptionsResult Validate(string? name, SftpOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Password) && string.IsNullOrWhiteSpace(options.PrivateKeyPath))
        {
            failures.Add("Either Sftp:Password or Sftp:PrivateKeyPath must be set.");
        }

        if (!string.IsNullOrWhiteSpace(options.PrivateKeyPath) && !File.Exists(options.PrivateKeyPath))
        {
            failures.Add($"Sftp:PrivateKeyPath '{options.PrivateKeyPath}' does not exist.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
