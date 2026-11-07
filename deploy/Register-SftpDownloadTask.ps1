<#
.SYNOPSIS
    Registers (or removes) the Windows Task Scheduler task that runs the SFTP download once a day.

.DESCRIPTION
    Run from an elevated PowerShell on the target server after copying the published output there.
    Re-running the script replaces the existing task, so it is safe to use for schedule changes.

.PARAMETER ExePath
    Full path to SftpBackgroundService.exe in the publish folder.

.PARAMETER At
    Time of day to run, in the server's local time. Default 02:00.

.PARAMETER User
    Account the task runs as. Default is the SYSTEM account, which needs no stored password but has broad
    privileges. For a dedicated service account pass -User "DOMAIN\svc_sftp" and you will be prompted for its
    password; the task then runs "whether user is logged on or not".

.PARAMETER Unregister
    Remove the task instead of creating it.

.EXAMPLE
    .\Register-SftpDownloadTask.ps1 -ExePath "C:\Services\SftpBackgroundService\SftpBackgroundService.exe"

.EXAMPLE
    .\Register-SftpDownloadTask.ps1 -ExePath "C:\Services\SftpBackgroundService\SftpBackgroundService.exe" -At "03:30" -User "CORP\svc_sftp"

.EXAMPLE
    .\Register-SftpDownloadTask.ps1 -Unregister
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(ParameterSetName = 'Register', Mandatory)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string] $ExePath,

    [Parameter(ParameterSetName = 'Register')]
    [string] $At = '02:00',

    [Parameter(ParameterSetName = 'Register')]
    [string] $User = 'NT AUTHORITY\SYSTEM',

    [Parameter(ParameterSetName = 'Unregister', Mandatory)]
    [switch] $Unregister
)

$ErrorActionPreference = 'Stop'
$taskName = 'SFTP Daily Download'

if ($Unregister) {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        if ($PSCmdlet.ShouldProcess($taskName, 'Unregister scheduled task')) {
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
            Write-Host "Removed task '$taskName'."
        }
    }
    else {
        Write-Host "Task '$taskName' does not exist. Nothing to do."
    }
    return
}

$exe = (Resolve-Path $ExePath).Path
$workDir = Split-Path $exe -Parent

$action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $workDir

$trigger = New-ScheduledTaskTrigger -Daily -At $At

$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Hours 2) `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 15) `
    -MultipleInstances IgnoreNew `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries

# -StartWhenAvailable: if the server was off at the scheduled time, run as soon as it is back.
# -RestartCount/-RestartInterval: retry a failed run (non-zero exit code) up to 3 times, 15 minutes apart.
# -MultipleInstances IgnoreNew: never run two downloads at once.

$registerArgs = @{
    TaskName    = $taskName
    Description = 'Downloads new files from the SFTP server and processes them. Exit codes: 0 ok, 1 download failed, 2 some files failed processing, 3 startup/config error, 4 cancelled.'
    Action      = $action
    Trigger     = $trigger
    Settings    = $settings
    Force       = $true
}

if ($User -eq 'NT AUTHORITY\SYSTEM') {
    $registerArgs.Principal = New-ScheduledTaskPrincipal -UserId $User -LogonType ServiceAccount -RunLevel Highest
}
else {
    $credential = Get-Credential -UserName $User -Message "Password for the account that will run '$taskName'"
    $registerArgs.User     = $credential.UserName
    $registerArgs.Password = $credential.GetNetworkCredential().Password
    $registerArgs.RunLevel = 'Highest'
}

if ($PSCmdlet.ShouldProcess($taskName, "Register scheduled task running $exe daily at $At as $User")) {
    $task = Register-ScheduledTask @registerArgs
    Write-Host "Registered task '$($task.TaskName)'."
    Write-Host "  Runs:    daily at $At (local time) as $User"
    Write-Host "  Command: $exe"
    Write-Host ""
    Write-Host "Run it now to verify:   Start-ScheduledTask -TaskName '$taskName'"
    Write-Host "Check the last result:  (Get-ScheduledTaskInfo -TaskName '$taskName').LastTaskResult"
}
