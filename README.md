# SftpBackgroundService

A .NET 10 console app that connects to an SFTP server, downloads new files, processes each one, and exits.
Windows Task Scheduler runs it once a day. Serilog writes a daily log file with 30-day retention.

Inspired by the hosting setup from
[.NET 10 Background Service with File Watcher](https://thomasngoswe.com/2026/03/07/dotnet-10-background-service-file-watcher/).
The long-running Worker Service version with an in-process daily scheduler is kept on the `worker-service`
branch; this branch is the simpler run-once shape.

## How it fits together

```
Windows Task Scheduler (daily at 02:00)
  |
  v
SftpBackgroundService.exe  ->  SftpDownloadRunner.RunAsync
  |
  v
ISftpDownloader -> SftpDownloader (SSH.NET)
  |  lists Sftp:RemoteDirectory, matches Sftp:FilePattern,
  |  downloads to <LocalDownloadDirectory>/.staging/<name>.part, then moves into LocalDownloadDirectory
  v
IFileProcessor -> LoggingFileProcessor   <-- replace this with your real processing
  |  called once per downloaded file, in order, after the SFTP session has closed
  v
exit code  (0 ok, 1 download failed, 2 some files failed processing, 3 startup/config error, 4 cancelled)
```

| Folder | Contents |
| --- | --- |
| `src/SftpBackgroundService` | `Program.cs` entry point, `SftpDownloadRunner` orchestration and exit codes |
| `src/SftpBackgroundService/Sftp` | `ISftpDownloader`, `SftpDownloader` |
| `src/SftpBackgroundService/Processing` | `IFileProcessor`, `LoggingFileProcessor` placeholder |
| `src/SftpBackgroundService/Options` | `SftpOptions` and its validator |
| `deploy` | `Register-SftpDownloadTask.ps1`, creates or removes the Task Scheduler task |
| `tests/SftpBackgroundService.Tests` | xUnit tests for the runner |

## Configuration

All settings live in `appsettings.json` and can be overridden by environment variables (`Sftp__Password`),
by `appsettings.Production.json`, or by user-secrets during development.

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
}
```

Either `Password` or `PrivateKeyPath` is required. Bad or missing configuration (including an empty
`FilePattern` or a `ConnectTimeoutSeconds` outside 1-3600) is logged as Fatal and the process exits with code 3
before touching the network.

Keep secrets out of `appsettings.json`:

```bash
dotnet user-secrets set "Sftp:Password" "your-password" --project src/SftpBackgroundService
```

Get the server's host key fingerprint so the app refuses to talk to an impostor:

```bash
ssh-keyscan sftp.example.com 2>/dev/null | ssh-keygen -lf -
```

## Run locally

```bash
dotnet run --project src/SftpBackgroundService --environment Development
```

One run, then it exits. Development raises the log level to Debug. The exit code is in `$LASTEXITCODE`
(PowerShell) or `$?` (bash).

## Test

```bash
dotnet test
```

## Add your processing logic

Implement `IFileProcessor` and swap the registration in `Program.cs`:

```csharp
builder.Services.AddSingleton<IFileProcessor, MyCsvImporter>();
```

`ProcessAsync` is called once per downloaded file, in order, after the SFTP connection has been closed.
Files are complete on disk when it is called. If it throws, the error is logged, the remaining files are
still processed, and the run exits with code 2.

## Deploy with Task Scheduler

**1. Publish** a self-contained single executable:

```bash
dotnet publish src/SftpBackgroundService -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

Copy the `publish` folder to the server, for example `C:\Services\SftpBackgroundService`.

**2. Configure.** Put the real host, path, and fingerprint in an `appsettings.Production.json` next to the exe,
and the password in a machine-scope environment variable so it is never on disk in plain text:

```powershell
[Environment]::SetEnvironmentVariable("Sftp__Password", "the-real-password", "Machine")
```

**3. Register the task** from an elevated PowerShell:

```powershell
.\deploy\Register-SftpDownloadTask.ps1 -ExePath "C:\Services\SftpBackgroundService\SftpBackgroundService.exe"
```

Add `-At "03:30"` for a different time or `-User "DOMAIN\svc_sftp"` to run as a dedicated account (you are
prompted for its password). The task is created with:

- **Start when available**: a run missed because the server was off happens as soon as it is back on.
- **Restart on launch failure** up to 3 times, 15 minutes apart. This only covers the task failing to start
  (for example the exe missing or the account unable to log on). A non-zero exit code is recorded as the
  last run result but is not retried; the next scheduled run picks the failed files up again.
- **Ignore new instance** if the previous run is still going.
- **2 hour execution limit** so a hung connection cannot run forever.

**4. Verify** by running it once by hand and checking the result:

```powershell
Start-ScheduledTask -TaskName "SFTP Daily Download"
```

```powershell
(Get-ScheduledTaskInfo -TaskName "SFTP Daily Download").LastTaskResult
```

`0` is success. Anything else maps to the exit codes above, and the log file has the details:

| Code | Meaning |
| --- | --- |
| 0 | Every new file was downloaded and processed. |
| 1 | The connection or listing failed, or one or more files could not be downloaded. Files that did land were still processed; the failed ones are retried next run. |
| 2 | Everything downloaded, but `IFileProcessor` threw for one or more files. |
| 3 | Bad or missing configuration. Nothing was attempted. |
| 4 | The run was cancelled (Ctrl+C). |

**Updating:** overwrite the publish folder. Nothing is locked between runs, so no stop/start is needed.

**Removing:**

```powershell
.\deploy\Register-SftpDownloadTask.ps1 -Unregister
```

## Logging

Logging is Serilog, configured entirely from the `Serilog` section of `appsettings.json`.

- **Console sink** for `dotnet run` and for Task Scheduler's own history view.
- **File sink** at `logs/sftp-download-YYYYMMDD.log` next to the executable. One file per day, rolled again at
  100 MB, and **files older than 30 days are deleted automatically** (`retainedFileTimeLimit`).

`Program.cs` sets the working directory to the exe folder on startup, so the relative `logs/` path lands beside
the binary regardless of the working directory Task Scheduler starts the process with. To put logs somewhere
else, change `path` to an absolute value such as `D:/Logs/SftpDownload/sftp-download-.log`.

Startup failures are written through a bootstrap logger before the host is built, so they reach the console
even when the main configuration cannot load.

## Behaviour notes

- Files are downloaded to a `.staging` subfolder and moved into place atomically, so a crash mid-transfer never
  leaves a partial file in the download folder.
- A failure on one file is logged and counted; the other files are still downloaded and processed, and the
  failed file is retried on the next run. Its `.part` is removed, so `SkipIfLocalExists` never hides it.
- `SkipIfLocalExists` is the only de-duplication. If you need to re-process, delete the local copy or set it
  to `false`.
- Without `HostKeyFingerprint` the first server key is trusted and a warning is logged on every run.
- Not Windows? The same exe (published with `-r linux-x64`) runs from cron: `0 2 * * * /opt/sftp/SftpBackgroundService`.
