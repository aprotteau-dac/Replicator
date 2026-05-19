param(
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\Replicator"),
    [switch] $NoDesktopShortcut,
    [switch] $NoShortcuts
)

$ErrorActionPreference = "Stop"

function New-Shortcut {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ShortcutPath,
        [Parameter(Mandatory = $true)]
        [string] $TargetPath,
        [Parameter(Mandatory = $true)]
        [string] $WorkingDirectory,
        [string] $Description = ""
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = $Description
    $shortcut.IconLocation = $TargetPath
    $shortcut.Save()
}

$sourceDir = $PSScriptRoot
$exeName = "Replicator.App.exe"
$sourceExe = Join-Path $sourceDir $exeName

if (-not (Test-Path $sourceExe)) {
    $repoInstall = Resolve-Path (Join-Path $PSScriptRoot "install-dev.ps1") -ErrorAction SilentlyContinue
    if ($repoInstall) {
        throw "This installer runs from a published package. From the repo root, use .\tools\install-dev.ps1 to publish and install Replicator."
    }

    throw "Installer must be run from a published Replicator package containing $exeName."
}

$installDirResolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($InstallDir)
New-Item -ItemType Directory -Force $installDirResolved | Out-Null

Get-ChildItem -LiteralPath $sourceDir -Force |
    Where-Object { $_.Name -notin @("install-replicator.ps1") } |
    ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $installDirResolved -Recurse -Force
    }

$installedExe = Join-Path $installDirResolved $exeName
$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Replicator"
$desktopDir = [Environment]::GetFolderPath("DesktopDirectory")

if (-not $NoShortcuts) {
    New-Item -ItemType Directory -Force $startMenuDir | Out-Null

    New-Shortcut `
        -ShortcutPath (Join-Path $startMenuDir "Replicator.lnk") `
        -TargetPath $installedExe `
        -WorkingDirectory $installDirResolved `
        -Description "Replicator backup and shuttle control"

    if (-not $NoDesktopShortcut) {
        New-Shortcut `
            -ShortcutPath (Join-Path $desktopDir "Replicator.lnk") `
            -TargetPath $installedExe `
            -WorkingDirectory $installDirResolved `
            -Description "Replicator backup and shuttle control"
    }
}

Write-Host "Replicator installed to $installDirResolved"
if (-not $NoShortcuts) {
    Write-Host "Start Menu shortcut created under $startMenuDir"
}
