using System.Text;
using Replicator.Core.Models;
using Replicator.Core.Scheduling;

namespace Replicator.Core.Scripting;

public sealed class PowerShellScriptGenerator(string scriptsDirectory, string logsDirectory)
{
    public GeneratedScript Generate(BackupProfile profile)
    {
        var issues = BackupProfileValidator.Validate(profile);
        if (issues.Count > 0)
        {
            var message = string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Field}: {issue.Message}"));
            throw new InvalidOperationException(message);
        }

        var slug = ProfileSlug(profile);
        var scriptPath = ScriptPathFor(profile);
        var content = BuildScript(profile, slug);

        return new GeneratedScript(scriptPath, content);
    }

    public string ScriptPathFor(BackupProfile profile)
    {
        var slug = ProfileSlug(profile);
        return Path.Combine(scriptsDirectory, $"{slug}.ps1");
    }

    public async Task<GeneratedScript> WriteAsync(BackupProfile profile, CancellationToken cancellationToken = default)
    {
        var script = Generate(profile);

        Directory.CreateDirectory(scriptsDirectory);
        Directory.CreateDirectory(logsDirectory);

        await File.WriteAllTextAsync(script.Path, script.Content, Encoding.UTF8, cancellationToken);
        await PowerShellScheduledTaskLauncher.WriteAsync(script.Path, cancellationToken);
        return script;
    }

    public static string ProfileSlug(BackupProfile profile)
    {
        var name = string.IsNullOrWhiteSpace(profile.Name) ? "profile" : profile.Name.Trim();
        var invalidChars = Path.GetInvalidFileNameChars().Concat([' ', '\t']).ToHashSet();
        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            builder.Append(invalidChars.Contains(character) ? '-' : char.ToLowerInvariant(character));
        }

        var compact = string.Join('-', builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(compact))
        {
            compact = "profile";
        }

        if (compact.Length > 48)
        {
            compact = compact[..48].TrimEnd('-');
        }

        return $"{compact}-{profile.Id.ToString("N")[..12]}";
    }

    private string BuildScript(BackupProfile profile, string slug)
    {
        var excludes = profile.ExcludePatterns
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ToPowerShellLiteral)
            .ToList();

        var excludeLiteral = excludes.Count == 0
            ? "@()"
            : $"@({string.Join(", ", excludes)})";

        var dryRunLiteral = profile.DryRun ? "$true" : "$false";
        var mirrorDeletesLiteral = profile.MirrorDeletes ? "$true" : "$false";

        return $$"""
            [CmdletBinding()]
            param(
                [switch] $DryRun
            )

            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'

            $ProfileName = {{ToPowerShellLiteral(profile.Name)}}
            $Source = {{ToPowerShellLiteral(profile.SourcePath)}}
            $Destination = {{ToPowerShellLiteral(profile.Target.Path)}}
            $LogDirectory = {{ToPowerShellLiteral(logsDirectory)}}
            $ProfileSlug = {{ToPowerShellLiteral(slug)}}
            $ExcludePatterns = {{excludeLiteral}}
            $DryRunFromProfile = {{dryRunLiteral}}
            $MirrorDeletes = {{mirrorDeletesLiteral}}

            function Write-RunLog {
                param([string] $Message)
                $line = '{0:u} {1}' -f (Get-Date), $Message
                Write-Host $line
                if ($null -ne $LogPath -and (Test-Path -LiteralPath $LogPath)) {
                    Add-Content -LiteralPath $LogPath -Value $line
                }
            }

            function Write-Status {
                param(
                    [string] $Message,
                    [object] $ExitCode,
                    [bool] $Succeeded
                )

                $status = [ordered]@{
                    ProfileName = $ProfileName
                    Mode = $RunMode
                    Source = $Source
                    Destination = $Destination
                    LogPath = $LogPath
                    StartedAt = $StartedAt.ToString('o')
                    UpdatedAt = (Get-Date).ToString('o')
                    ExitCode = $ExitCode
                    Succeeded = $Succeeded
                    Message = $Message
                }

                $status | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $StatusPath -Encoding UTF8
            }

            function Test-RootAvailable {
                param(
                    [string] $Label,
                    [string] $Path
                )

                $root = [System.IO.Path]::GetPathRoot($Path)
                if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path -LiteralPath $root -PathType Container)) {
                    throw "$Label drive is unavailable: $root"
                }
            }

            New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null

            $StartedAt = Get-Date
            $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
            $LogPath = Join-Path $LogDirectory ("{0}-{1}.log" -f $ProfileSlug, $timestamp)
            $StatusPath = Join-Path $LogDirectory ("{0}-latest.json" -f $ProfileSlug)
            $effectiveDryRun = [bool] $DryRun -or [bool] $DryRunFromProfile
            $RunMode = if ($effectiveDryRun) { 'Dry run - no files will be copied' } else { 'Copy - files may be copied' }

            $header = @(
                'Replicator backup run',
                "Profile: $ProfileName",
                "Mode: $RunMode",
                "Source: $Source",
                "Destination: $Destination",
                "Started: $($StartedAt.ToString('u'))",
                "Mirror deletes: $MirrorDeletes",
                "Excludes: $($ExcludePatterns -join ', ')",
                ''
            )
            Set-Content -LiteralPath $LogPath -Value $header -Encoding UTF8
            Write-Status -Message 'Started.' -ExitCode $null -Succeeded $false

            try {
                Test-RootAvailable -Label 'Source' -Path $Source
                Test-RootAvailable -Label 'Target' -Path $Destination

                if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
                    throw "Source path is unavailable: $Source"
                }

                if (-not (Test-Path -LiteralPath $Destination -PathType Container)) {
                    if ($effectiveDryRun) {
                        $message = "Target path does not exist; dry run would create it during a real run: $Destination"
                        Write-RunLog $message
                        Write-Status -Message $message -ExitCode 0 -Succeeded $true
                        exit 0
                    }

                    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
                }

                $robocopyArgs = @($Source, $Destination)
                if ($MirrorDeletes) {
                    $robocopyArgs += '/MIR'
                } else {
                    $robocopyArgs += '/E'
                }

                $robocopyArgs += @('/COPY:DAT', '/DCOPY:DAT', '/R:2', '/W:5', '/FFT', '/Z', '/NP', '/TEE', "/LOG+:$LogPath")

                if ($effectiveDryRun) {
                    $robocopyArgs += '/L'
                    Write-RunLog "Starting dry-run replication for profile '$ProfileName'. No files will be copied."
                } else {
                    Write-RunLog "Starting replication for profile '$ProfileName'."
                }

                if ($ExcludePatterns.Count -gt 0) {
                    $robocopyArgs += '/XD'
                    $robocopyArgs += $ExcludePatterns
                    $robocopyArgs += '/XF'
                    $robocopyArgs += $ExcludePatterns
                }

                & robocopy @robocopyArgs
                $exitCode = $LASTEXITCODE

                if ($exitCode -gt 7) {
                    $message = "Robocopy failed with exit code $exitCode. See log: $LogPath"
                    Write-RunLog $message
                    Write-Status -Message $message -ExitCode $exitCode -Succeeded $false
                    exit 1
                }

                $message = "Replication completed with robocopy exit code $exitCode. Log: $LogPath"
                Write-RunLog $message
                Write-Status -Message $message -ExitCode $exitCode -Succeeded $true
                exit 0
            } catch {
                $message = $_.Exception.Message
                Write-RunLog "ERROR: $message"
                Write-Status -Message $message -ExitCode 1 -Succeeded $false
                Write-Error $message
                exit 1
            }
            """;
    }

    private static string ToPowerShellLiteral(string value)
    {
        return $"'{value.Replace("'", "''")}'";
    }
}
