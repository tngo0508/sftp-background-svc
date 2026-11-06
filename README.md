# SftpBackgroundService

A .NET 10 Worker Service that connects to an SFTP server once a day, downloads new files, and processes each
one. It runs as a console app during development and as a Windows Service in production.

Inspired by the hosting and Windows Service setup from
[.NET 10 Background Service with File Watcher](https://thomasngoswe.com/2026/03/07/dotnet-10-background-service-file-watcher/),
with the `FileSystemWatcher` replaced by a daily scheduler plus an SFTP downloader. The post's
Channels-based queue is intentionally left out: with one producer that fires once a day and one consumer it
adds indirection without a payoff. Add it back if a second source of files appears.

## How it fits together

```
SftpDownloadService (BackgroundService)
  |  waits until Schedule:RunAt in Schedule:TimeZone (and optionally runs at startup)
  v
ISftpDownloader -> SftpDownloader (SSH.NET)
  |  lists Sftp:RemoteDirectory, matches Sftp:FilePattern,
  |  downloads to <LocalDownloadDirectory>/.staging/<name>.part, then moves into LocalDownloadDirectory
  v
IFileProcessor -> LoggingFileProcessor   <-- replace this with your real processing
     called once per downloaded file, in order, after the SFTP session has closed
```

| Folder | Contents |
| --- | --- |
| `src/SftpBackgroundService/Hosting` | `SftpDownloadService` (scheduler and orchestration) |
| `src/SftpBackgroundService/Sftp` | `ISftpDownloader`, `SftpDownloader` |
| `src/SftpBackgroundService/Processing` | `IFileProcessor`, `LoggingFileProcessor` placeholder |
| `src/SftpBackgroundService/Scheduling` | `DailySchedule`, pure next-run arithmetic |
| `src/SftpBackgroundService/Options` | `SftpOptions`, `ScheduleOptions`, validators |
| `tests/SftpBackgroundService.Tests` | xUnit tests (scheduler arithmetic, service loop) |

## Configuration

All settings live in `appsettings.json` and can be overridden per environment, by environment variables
(`Sftp__Password`), or by user-secrets.

```json
"Sftp": {
  "Host": "sftp.example.com",
  "Port": 22,
  "Username": "svc_user",
  "Password": null,                 // or
  "PrivateKeyPath": null,           // path to an OpenSSH/PEM key
  "PrivateKeyPassphrase": null,
  "HostKeyFingerprint": null,       // "SHA256:..." from ssh-keygen -lf; strongly recommended in prod
  "RemoteDirectory": "/outbound",
  "FilePattern": "*.csv",
  "LocalDownloadDirectory": "C:/Temp/SftpDownloads",   // forward slashes avoid JSON escaping
  "SkipIfLocalExists": true,
  "DeleteRemoteAfterDownload": false,
  "ConnectTimeoutSeconds": 30
},
"Schedule": {
  "RunAt": "02:00",                 // time of day
  "TimeZone": "Local",              // "Local", "UTC", or a system zone id
  "RunOnStartup": false             // appsettings.Development.json sets this to true
}
```

Either `Password` or `PrivateKeyPath` is required. Config is validated at startup; the host refuses to start
with a clear message if something is missing.

Keep secrets out of `appsettings.json`:

```bash
dotnet user-secrets set "Sftp:Password" "your-password" --project src/SftpBackgroundService
```

## Run locally

```bash
dotnet run --project src/SftpBackgroundService --environment Development
```

Development turns on `RunOnStartup`, so you see a download attempt immediately instead of waiting for 02:00.

## Test

```bash
dotnet test
```

## Add your processing logic

Implement `IFileProcessor` and swap the registration in `Program.cs`:

```csharp
builder.Services.AddSingleton<IFileProcessor, MyCsvImporter>();
```

`ProcessAsync` is called by the scheduler one file at a time, in download order, after the SFTP connection
has been closed. Files are already complete on disk when it is called. If it throws, the error is logged and
the remaining files are still processed.

## Install as a Windows Service

```bash
dotnet publish src/SftpBackgroundService -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

Then from an elevated prompt (the space after `binpath=` is required):

```bash
sc.exe create "SftpBackgroundService" binpath= "C:\path\to\publish\SftpBackgroundService.exe" start= auto
```

```bash
sc.exe start "SftpBackgroundService"
```

```bash
sc.exe stop "SftpBackgroundService"
```

```bash
sc.exe delete "SftpBackgroundService"
```

## Logging

Logging is Serilog, configured entirely from the `Serilog` section of `appsettings.json`.

- **Console sink** for `dotnet run`.
- **File sink** at `logs/sftp-service-YYYYMMDD.log` next to the executable. One file per day, rolled again at
  100 MB, and **files older than 30 days are deleted automatically** (`retainedFileTimeLimit`).

`Program.cs` sets the working directory to the exe folder on startup, so the relative `logs/` path lands beside
the binary whether you run from the console or as a Windows Service (where the default working directory is
`System32`). To put logs somewhere else, change `path` to an absolute value such as
`D:/Logs/SftpBackgroundService/sftp-service-.log`.

`appsettings.Development.json` raises the minimum level to `Debug`, which adds per-file skip decisions and
remote delete confirmations. Production stays at `Information` with Microsoft framework noise capped at
`Warning`.

Startup failures (bad config, missing credentials) are written through a bootstrap logger before the host is
built, so they reach the console even when the main configuration cannot load. The host exits with code 1.

## Behaviour notes

- A failed download run is logged and the service waits for the next scheduled time. It does not crash.
- Files are downloaded to a `.staging` subfolder and moved into place atomically, so a crash mid-transfer never
  leaves a partial file in the download folder.
- `SkipIfLocalExists` is the only de-duplication. If you need to re-process, delete the local copy or set it
  to `false`.
- Without `HostKeyFingerprint` the first server key is trusted and a warning is logged on every connection.
