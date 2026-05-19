param(
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\Replicator"),
    [switch] $RemoveAppData
)

$ErrorActionPreference = "Stop"

$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Replicator"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "Replicator.lnk"
$installDirResolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($InstallDir)

if (Test-Path $startMenuDir) {
    Remove-Item -LiteralPath $startMenuDir -Recurse -Force
}

if (Test-Path $desktopShortcut) {
    Remove-Item -LiteralPath $desktopShortcut -Force
}

if (Test-Path $installDirResolved) {
    Remove-Item -LiteralPath $installDirResolved -Recurse -Force
}

if ($RemoveAppData) {
    $appData = Join-Path $env:LOCALAPPDATA "Replicator"
    if (Test-Path $appData) {
        Remove-Item -LiteralPath $appData -Recurse -Force
    }
}

Write-Host "Replicator uninstalled."
