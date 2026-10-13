<#
  Installs (or removes) the RMS Scheduler as a Windows service. Run from an elevated PowerShell prompt
  in the folder that holds RMS.Scheduler.exe (the publish output).

    .\install-service.ps1 -Account "CORP\svc_rms_scheduler"   # recommended: a dedicated service account
    .\install-service.ps1                                     # runs as Local System
    .\install-service.ps1 -Uninstall

  The account needs: read access to this folder, Modify rights on the report share folders, and network
  access to the databases and the mail server. It does not need to be an RMS user.
#>
param(
    [string] $ServiceName = "RMS.Scheduler",
    [string] $DisplayName = "RMS Report Scheduler",
    [string] $Account,
    [switch] $Uninstall
)

$ErrorActionPreference = "Stop"
$exe = Join-Path $PSScriptRoot "RMS.Scheduler.exe"

if ($Uninstall) {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
        sc.exe delete $ServiceName | Out-Null
        Write-Host "Removed $ServiceName."
    }
    return
}

if (-not (Test-Path $exe)) { throw "RMS.Scheduler.exe not found in $PSScriptRoot. Run this script from the publish folder." }
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) { throw "$ServiceName is already installed. Use -Uninstall first." }

$parameters = @{
    Name           = $ServiceName
    BinaryPathName = "`"$exe`""
    DisplayName    = $DisplayName
    Description    = "Runs scheduled reports from Reports Management and delivers them by email and to shared folders."
    StartupType    = "Automatic"
}
if ($Account) { $parameters.Credential = Get-Credential -UserName $Account -Message "Password for $Account" }
New-Service @parameters | Out-Null

# Restart automatically after a crash: after 1 minute, 1 minute, then 5 minutes.
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null

# Event log source the service writes to (Windows Logs > Application, source RMS.Scheduler).
if (-not [System.Diagnostics.EventLog]::SourceExists("RMS.Scheduler")) {
    New-EventLog -LogName Application -Source "RMS.Scheduler"
}

Start-Service -Name $ServiceName
Get-Service -Name $ServiceName
