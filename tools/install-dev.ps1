param(
    [string] $Version = "0.1.0",
    [string] $Runtime = "win-x64",
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\Replicator"),
    [switch] $NoDesktopShortcut,
    [switch] $NoShortcuts,
    [switch] $FrameworkDependent
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$packageDir = Join-Path $repoRoot "artifacts\package\Replicator-$Version-$Runtime"
$packageInstaller = Join-Path $packageDir "install-replicator.ps1"
$packageExe = Join-Path $packageDir "Replicator.App.exe"

if (-not (Test-Path $packageExe)) {
    $publishScript = Join-Path $PSScriptRoot "publish-release.ps1"
    $publishArgs = @{
        Version = $Version
        Runtime = $Runtime
    }

    if ($FrameworkDependent) {
        $publishArgs.FrameworkDependent = $true
    }

    & $publishScript @publishArgs
}

if (-not (Test-Path $packageInstaller)) {
    throw "Expected packaged installer not found: $packageInstaller"
}

$installArgs = @{
    InstallDir = $InstallDir
}

if ($NoDesktopShortcut) {
    $installArgs.NoDesktopShortcut = $true
}

if ($NoShortcuts) {
    $installArgs.NoShortcuts = $true
}

& $packageInstaller @installArgs
