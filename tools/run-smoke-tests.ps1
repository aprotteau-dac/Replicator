[CmdletBinding()]
param(
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

if (-not $SkipBuild) {
    dotnet build Replicator.sln
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

dotnet run --project tests/Replicator.Tests/Replicator.Tests.csproj
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host 'Replicator smoke gates passed.'
