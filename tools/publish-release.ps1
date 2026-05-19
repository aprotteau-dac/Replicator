param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Version = "0.1.0",
    [switch] $FrameworkDependent
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$artifactsRoot = Join-Path $repoRoot "artifacts"
$publishRoot = Join-Path $artifactsRoot "publish"
$packageRoot = Join-Path $artifactsRoot "package"
$appProject = Join-Path $repoRoot "src\Replicator.App\Replicator.App.csproj"
$publishDir = Join-Path $publishRoot "Replicator-$Version-$Runtime"
$packageDir = Join-Path $packageRoot "Replicator-$Version-$Runtime"
$zipPath = Join-Path $packageRoot "Replicator-$Version-$Runtime.zip"

New-Item -ItemType Directory -Force $publishRoot, $packageRoot | Out-Null

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

if (Test-Path $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$publishArgs = @(
    "publish",
    $appProject,
    "--configuration",
    $Configuration,
    "--runtime",
    $Runtime,
    "--output",
    $publishDir,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:Version=$Version"
)

if ($FrameworkDependent) {
    $publishArgs += "--no-self-contained"
} else {
    $publishArgs += "--self-contained"
    $publishArgs += "true"
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

New-Item -ItemType Directory -Force $packageDir | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageDir -Recurse -Force
Copy-Item -Path (Join-Path $PSScriptRoot "install-replicator.ps1") -Destination $packageDir -Force
Copy-Item -Path (Join-Path $PSScriptRoot "uninstall-replicator.ps1") -Destination $packageDir -Force
Copy-Item -Path (Join-Path $repoRoot "README.md") -Destination $packageDir -Force
Copy-Item -Path (Join-Path $repoRoot "LICENSE") -Destination $packageDir -Force

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -Force

Write-Host "Published: $publishDir"
Write-Host "Package:   $packageDir"
Write-Host "Zip:       $zipPath"
