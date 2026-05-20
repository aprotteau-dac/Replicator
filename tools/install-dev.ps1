param(
    [string] $Version = "0.1.0",
    [string] $Runtime = "win-x64",
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\Replicator"),
    [switch] $NoDesktopShortcut,
    [switch] $NoShortcuts,
    [switch] $FrameworkDependent,
    [switch] $SkipPublish
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$packageDir = Join-Path $repoRoot "artifacts\package\Replicator-$Version-$Runtime"
$packageInstaller = Join-Path $packageDir "install-replicator.ps1"
$packageExe = Join-Path $packageDir "Replicator.App.exe"

if (-not $SkipPublish) {
    $publishScript = Join-Path $PSScriptRoot "publish-release.ps1"
    $publishArgs = @{
        Version = $Version
        Runtime = $Runtime
    }

    if ($FrameworkDependent) {
        $publishArgs.FrameworkDependent = $true
    }

    & $publishScript @publishArgs
} elseif (-not (Test-Path $packageExe)) {
    throw "Packaged executable not found: $packageExe. Run without -SkipPublish to build it."
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
