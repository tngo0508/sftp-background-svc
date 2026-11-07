using System.ComponentModel.DataAnnotations;

namespace SftpBackgroundService.Options;

/// <summary>Connection and transfer settings for the remote SFTP server. Bound from the "Sftp" section.</summary>
public sealed class SftpOptions
{
    public const string SectionName = "Sftp";

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 22;

    [Required]
    public string Username { get; set; } = string.Empty;

    /// <summary>Password authentication. Prefer user-secrets or environment variables over appsettings.json.</summary>
    public string? Password { get; set; }

    /// <summary>Path to an OpenSSH/PEM private key. Used instead of, or in addition to, the password.</summary>
    public string? PrivateKeyPath { get; set; }

    public string? PrivateKeyPassphrase { get; set; }

    /// <summary>
    /// Optional SHA-256 host key fingerprint (base64, as printed by <c>ssh-keygen -lf</c>, with or without the "SHA256:" prefix).
    /// When set, the connection is refused if the server presents a different key.
    /// </summary>
    public string? HostKeyFingerprint { get; set; }

    [Required]
    public string RemoteDirectory { get; set; } = "/";

    /// <summary>Simple wildcard pattern matched against remote file names, e.g. "*.csv" or "report_*.xml".</summary>
    [Required]
    public string FilePattern { get; set; } = "*";

    /// <summary>Folder that downloaded files land in.</summary>
    [Required]
    public string LocalDownloadDirectory { get; set; } = string.Empty;

    /// <summary>Skip remote files whose name already exists locally. Set to false to always re-download.</summary>
    public bool SkipIfLocalExists { get; set; } = true;

    /// <summary>Delete the remote file after a successful download.</summary>
    public bool DeleteRemoteAfterDownload { get; set; }

    [Range(1, 3600)]
    public int ConnectTimeoutSeconds { get; set; } = 30;
}
