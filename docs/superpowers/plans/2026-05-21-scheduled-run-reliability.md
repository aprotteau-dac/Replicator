# Scheduled Run Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make selected-profile scheduled runs headless, repairable, and diagnosable when old Task Scheduler actions, unavailable targets, or dry-run status files are involved.

**Architecture:** Add a small scheduled-task action inspector in Core, extend the selected-profile task snapshot with repair health, and label the existing WPF update path as "Repair Task" when a task is stale. Extend generated PowerShell scripts to write clear preflight status before robocopy and add a status reader that reads the existing `*-latest.json` files beside robocopy logs.

**Tech Stack:** .NET 8 WPF, `schtasks.exe`, generated PowerShell scripts, JSON status files, repository smoke-test harness in `tests/Replicator.Tests`.

---

## File Structure

- Create `src/Replicator.Core/Scheduling/ScheduledTaskActionHealth.cs`
  - Immutable result for action health: repair flag, repair reasons, parsed script path, and script-existence state.
- Create `src/Replicator.Core/Scheduling/ScheduledTaskActionInspector.cs`
  - Parses the `Task To Run` text returned by `schtasks /Query /FO LIST /V` and decides whether a task action needs repair.
- Modify `src/Replicator.Core/Scheduling/ScheduledTaskSnapshot.cs`
  - Add optional action-health fields while preserving the existing positional record constructor.
- Modify `src/Replicator.Core/Scheduling/IScheduledTaskService.cs`
  - Allow callers to pass the expected profile script path into `QueryAsync`.
- Modify `src/Replicator.Core/Scheduling/WindowsScheduledTaskService.cs`
  - Parse `Task To Run`, inspect it, and populate the new snapshot fields.
- Modify `src/Replicator.Core/Scripting/PowerShellScriptGenerator.cs`
  - Expose `ScriptPathFor(profile)` and add target-path preflight behavior before robocopy.
- Create `src/Replicator.Core/Scripting/BackupRunStatus.cs`
  - Immutable display model for `*-latest.json` status.
- Create `src/Replicator.Core/Scripting/BackupRunStatusReader.cs`
  - Reads the selected profile's latest status file and tolerates missing or malformed JSON.
- Modify `src/Replicator.App/MainWindow.xaml.cs`
  - Query expected script path, surface repair state, disable starting unhealthy tasks, and show latest JSON status when available.
- Modify `tests/Replicator.Tests/Program.cs`
  - Add red-first tests for action inspection, task query health, target preflight script content, and status reading.
- Modify `docs/Smoke-Test-Plan.md`
  - Update expected test count and add scheduled-task repair/status smoke coverage.

## Task 1: Scheduled Task Action Inspector

**Files:**
- Test: `tests/Replicator.Tests/Program.cs`
- Create: `src/Replicator.Core/Scheduling/ScheduledTaskActionHealth.cs`
- Create: `src/Replicator.Core/Scheduling/ScheduledTaskActionInspector.cs`

- [ ] **Step 1: Add failing action-inspector tests**

In `tests/Replicator.Tests/Program.cs`, add these registrations near the existing scheduled-task tests:

```csharp
("scheduled task action inspector flags visible powershell actions", ScheduledTaskActionInspectorFlagsVisiblePowerShellActions),
("scheduled task action inspector flags mismatched script paths", ScheduledTaskActionInspectorFlagsMismatchedScriptPaths),
```

Add these test methods:

```csharp
static Task ScheduledTaskActionInspectorFlagsVisiblePowerShellActions()
{
    var scriptPath = Path.Combine(Path.GetTempPath(), $"replicator-{Guid.NewGuid():N}.ps1");
    File.WriteAllText(scriptPath, "# test");

    try
    {
        var action = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"";

        var health = ScheduledTaskActionInspector.Inspect(action, scriptPath);

        Assert(health.NeedsRepair, "Expected visible/noninteractive-missing action to need repair.");
        Assert(health.ScriptPath == scriptPath, $"Unexpected script path: {health.ScriptPath}");
        Assert(health.ScriptExists, "Expected script to exist.");
        Assert(health.RepairReasons.Any(reason => reason.Contains("-WindowStyle Hidden", StringComparison.OrdinalIgnoreCase)), "Expected hidden-window repair reason.");
        Assert(health.RepairReasons.Any(reason => reason.Contains("-NonInteractive", StringComparison.OrdinalIgnoreCase)), "Expected noninteractive repair reason.");
    }
    finally
    {
        if (File.Exists(scriptPath))
        {
            File.Delete(scriptPath);
        }
    }

    return Task.CompletedTask;
}

static Task ScheduledTaskActionInspectorFlagsMismatchedScriptPaths()
{
    var expected = Path.Combine(Path.GetTempPath(), $"expected-{Guid.NewGuid():N}.ps1");
    var actual = Path.Combine(Path.GetTempPath(), $"actual-{Guid.NewGuid():N}.ps1");
    File.WriteAllText(actual, "# test");

    try
    {
        var action = $"powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{actual}\"";

        var health = ScheduledTaskActionInspector.Inspect(action, expected);

        Assert(health.NeedsRepair, "Expected mismatched script path to need repair.");
        Assert(health.ScriptPath == actual, $"Unexpected parsed script path: {health.ScriptPath}");
        Assert(health.ScriptExists, "Expected actual script to exist.");
        Assert(health.RepairReasons.Any(reason => reason.Contains("does not match", StringComparison.OrdinalIgnoreCase)), "Expected path mismatch repair reason.");
    }
    finally
    {
        if (File.Exists(actual))
        {
            File.Delete(actual);
        }
    }

    return Task.CompletedTask;
}
```

- [ ] **Step 2: Run tests to verify red**

Run:

```powershell
dotnet run --project .\tests\Replicator.Tests\Replicator.Tests.csproj -- --filter "scheduled task action inspector"
```

Expected: build fails because `ScheduledTaskActionInspector` is not defined.

- [ ] **Step 3: Add action health record**

Create `src/Replicator.Core/Scheduling/ScheduledTaskActionHealth.cs`:

```csharp
namespace Replicator.Core.Scheduling;

public sealed record ScheduledTaskActionHealth(
    bool NeedsRepair,
    IReadOnlyList<string> RepairReasons,
    string ScriptPath,
    bool ScriptExists);
```

- [ ] **Step 4: Add action inspector implementation**

Create `src/Replicator.Core/Scheduling/ScheduledTaskActionInspector.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Replicator.Core.Scheduling;

public static partial class ScheduledTaskActionInspector
{
    private static readonly Regex FileArgumentPattern = new(
        """(?i)(?:^|\s)-File\s+(?:"(?<quoted>[^"]+)"|'(?<single>[^']+)'|(?<bare>\S+))""",
        RegexOptions.Compiled);

    public static ScheduledTaskActionHealth Inspect(string taskToRun, string? expectedScriptPath)
    {
        taskToRun ??= string.Empty;
        var reasons = new List<string>();

        if (!taskToRun.Contains("-WindowStyle Hidden", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Task action is missing -WindowStyle Hidden.");
        }

        if (!taskToRun.Contains("-NonInteractive", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Task action is missing -NonInteractive.");
        }

        var scriptPath = ParseScriptPath(taskToRun);
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            reasons.Add("Task action is missing -File script path.");
        }

        var scriptExists = !string.IsNullOrWhiteSpace(scriptPath) && File.Exists(scriptPath);
        if (!string.IsNullOrWhiteSpace(scriptPath) && !scriptExists)
        {
            reasons.Add($"Task script is missing: {scriptPath}");
        }

        if (!string.IsNullOrWhiteSpace(expectedScriptPath)
            && !string.IsNullOrWhiteSpace(scriptPath)
            && !PathsEqual(scriptPath, expectedScriptPath))
        {
            reasons.Add($"Task script path does not match expected profile script: {scriptPath}");
        }

        return new ScheduledTaskActionHealth(reasons.Count > 0, reasons, scriptPath, scriptExists);
    }

    private static string ParseScriptPath(string taskToRun)
    {
        var match = FileArgumentPattern.Match(taskToRun);
        if (!match.Success)
        {
            return string.Empty;
        }

        if (match.Groups["quoted"].Success)
        {
            return match.Groups["quoted"].Value;
        }

        if (match.Groups["single"].Success)
        {
            return match.Groups["single"].Value;
        }

        return match.Groups["bare"].Success ? match.Groups["bare"].Value : string.Empty;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
        catch (NotSupportedException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
        catch (PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify green**

Run:

```powershell
dotnet run --project .\tests\Replicator.Tests\Replicator.Tests.csproj -- --filter "scheduled task action inspector"
```

Expected: both action-inspector tests pass.

- [ ] **Step 6: Commit task 1**

```powershell
git add .\tests\Replicator.Tests\Program.cs .\src\Replicator.Core\Scheduling\ScheduledTaskActionHealth.cs .\src\Replicator.Core\Scheduling\ScheduledTaskActionInspector.cs
git commit -m "feat: inspect scheduled task actions"
```

## Task 2: Scheduled Task Query Repair State

**Files:**
- Test: `tests/Replicator.Tests/Program.cs`
- Modify: `src/Replicator.Core/Scheduling/ScheduledTaskSnapshot.cs`
- Modify: `src/Replicator.Core/Scheduling/IScheduledTaskService.cs`
- Modify: `src/Replicator.Core/Scheduling/WindowsScheduledTaskService.cs`
- Modify: `src/Replicator.Core/Scripting/PowerShellScriptGenerator.cs`
- Modify: `src/Replicator.App/MainWindow.xaml.cs`

- [ ] **Step 1: Add failing scheduled-task query test**

In `tests/Replicator.Tests/Program.cs`, add this registration:

```csharp
("scheduled task query reports repair reasons", ScheduledTaskQueryReportsRepairReasons),
```

Add this test method:

```csharp
static async Task ScheduledTaskQueryReportsRepairReasons()
{
    var expectedScript = Path.Combine(Path.GetTempPath(), $"expected-{Guid.NewGuid():N}.ps1");
    var actualScript = Path.Combine(Path.GetTempPath(), $"actual-{Guid.NewGuid():N}.ps1");
    File.WriteAllText(actualScript, "# test");

    try
    {
        var output = $"""

Folder: \Replicator
HostName:                             DEVBOX
TaskName:                             \Replicator\Back-up-Personal-Dev-58baefdb
Next Run Time:                        5/21/2026 2:00:00 PM
Status:                               Ready
Logon Mode:                           Interactive/Background
Last Run Time:                        5/21/2026 1:00:00 PM
Last Result:                          0
Task To Run:                          powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{actualScript}"

""";
        var service = new WindowsScheduledTaskService(new FakeProcessRunner(new ProcessResult(0, output, string.Empty)));

        var snapshot = await service.QueryAsync(ValidProfile(), expectedScript);

        Assert(snapshot.NeedsRepair, "Expected visible or mismatched action to need repair.");
        Assert(snapshot.TaskToRun.Contains(actualScript, StringComparison.OrdinalIgnoreCase), "Expected raw task action to be captured.");
        Assert(snapshot.ScriptPath == actualScript, $"Unexpected script path: {snapshot.ScriptPath}");
        Assert(snapshot.ScriptExists, "Expected parsed task script to exist.");
        Assert(snapshot.RepairReasons.Any(reason => reason.Contains("-WindowStyle Hidden", StringComparison.OrdinalIgnoreCase)), "Expected missing hidden-window repair reason.");
        Assert(snapshot.RepairReasons.Any(reason => reason.Contains("-NonInteractive", StringComparison.OrdinalIgnoreCase)), "Expected missing noninteractive repair reason.");
        Assert(snapshot.RepairReasons.Any(reason => reason.Contains("does not match", StringComparison.OrdinalIgnoreCase)), "Expected script path mismatch reason.");
    }
    finally
    {
        if (File.Exists(actualScript))
        {
            File.Delete(actualScript);
        }
    }
}
```

- [ ] **Step 2: Run test to verify red**

Run:

```powershell
dotnet run --project .\tests\Replicator.Tests\Replicator.Tests.csproj -- --filter "scheduled task query reports repair reasons"
```

Expected: build fails because `QueryAsync` does not accept `expectedScript` or `ScheduledTaskSnapshot` lacks action-health properties.

- [ ] **Step 3: Extend scheduled task snapshot**

Modify `src/Replicator.Core/Scheduling/ScheduledTaskSnapshot.cs` to:

```csharp
namespace Replicator.Core.Scheduling;

public sealed record ScheduledTaskSnapshot(
    string TaskName,
    ScheduledTaskState State,
    string NextRunTime,
    string LastRunTime,
    int LastResult,
    string RawOutput)
{
    public string TaskToRun { get; init; } = string.Empty;

    public string ScriptPath { get; init; } = string.Empty;

    public bool ScriptExists { get; init; }

    public bool NeedsRepair { get; init; }

    public IReadOnlyList<string> RepairReasons { get; init; } = [];
}
```

- [ ] **Step 4: Extend scheduled task service contract**

Modify `src/Replicator.Core/Scheduling/IScheduledTaskService.cs`:

```csharp
namespace Replicator.Core.Scheduling;

public interface IScheduledTaskService
{
    Task<ScheduledTaskSnapshot> QueryAsync(BackupProfile profile, string? expectedScriptPath = null, CancellationToken cancellationToken = default);

    Task InstallOrUpdateAsync(BackupProfile profile, string scriptPath, CancellationToken cancellationToken = default);

    Task StartAsync(BackupProfile profile, CancellationToken cancellationToken = default);

    Task DisableAsync(BackupProfile profile, CancellationToken cancellationToken = default);

    Task RemoveAsync(BackupProfile profile, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Populate action health from schtasks output**

Modify `src/Replicator.Core/Scheduling/WindowsScheduledTaskService.cs`:

```csharp
public async Task<ScheduledTaskSnapshot> QueryAsync(
    BackupProfile profile,
    string? expectedScriptPath = null,
    CancellationToken cancellationToken = default)
{
    var taskName = TaskNameFor(profile);
    var result = await runner.RunAsync("schtasks.exe", ["/Query", "/TN", taskName, "/FO", "LIST", "/V"], cancellationToken);
    if (result.ExitCode != 0)
    {
        return new ScheduledTaskSnapshot(taskName, ScheduledTaskState.Missing, string.Empty, string.Empty, 0, CombineOutput(result));
    }

    var values = ParseListOutput(result.StandardOutput);
    var state = ParseState(values.GetValueOrDefault("Status"));
    var nextRun = values.GetValueOrDefault("Next Run Time") ?? string.Empty;
    var lastRun = values.GetValueOrDefault("Last Run Time") ?? string.Empty;
    var lastResult = ParseLastResult(values.GetValueOrDefault("Last Result"));
    var taskToRun = values.GetValueOrDefault("Task To Run") ?? string.Empty;
    var actionHealth = ScheduledTaskActionInspector.Inspect(taskToRun, expectedScriptPath);

    return new ScheduledTaskSnapshot(taskName, state, nextRun, lastRun, lastResult, result.StandardOutput)
    {
        TaskToRun = taskToRun,
        ScriptPath = actionHealth.ScriptPath,
        ScriptExists = actionHealth.ScriptExists,
        NeedsRepair = actionHealth.NeedsRepair,
        RepairReasons = actionHealth.RepairReasons
    };
}
```

Keep all existing methods in `WindowsScheduledTaskService` unchanged except any call signatures affected by the interface change.

- [ ] **Step 6: Expose expected script path**

Modify `src/Replicator.Core/Scripting/PowerShellScriptGenerator.cs` so `Generate` computes paths through a new public method:

```csharp
public string ScriptPathFor(BackupProfile profile)
{
    var slug = ProfileSlug(profile);
    return Path.Combine(scriptsDirectory, $"{slug}.ps1");
}
```

Use it inside `Generate`:

```csharp
var scriptPath = ScriptPathFor(profile);
var slug = ProfileSlug(profile);
```

- [ ] **Step 7: Wire repair state into the WPF selected-profile UI**

In `src/Replicator.App/MainWindow.xaml.cs`, update each selected-profile task query to pass the expected script path:

```csharp
var expectedScriptPath = _scriptGenerator.ScriptPathFor(profile);
var snapshot = await _scheduledTasks.QueryAsync(profile, expectedScriptPath);
```

In `RefreshTaskStatusAsync`, set the summary text with repair reasons when repair is needed:

```csharp
if (snapshot.NeedsRepair)
{
    TaskSummaryTextBlock.Text = $"Needs repair | {string.Join(" ", snapshot.RepairReasons)}";
}
else
{
    TaskSummaryTextBlock.Text = $"{snapshot.State} | {profile.Engine} | {profile.Target.Kind}";
}
```

In `ApplyTaskStatusBrushes`, treat repair state as warning/alarm:

```csharp
if (snapshot.State == ScheduledTaskState.Missing || snapshot.NeedsRepair)
{
    TaskSummaryTextBlock.Foreground = (Brush)FindResource("WarningBrush");
    return;
}
```

In `UpdateActionSurface`, use repair-specific command text and block starting unhealthy scheduled tasks:

```csharp
var taskNeedsRepair = snapshot?.NeedsRepair == true;
var taskExists = supportsScheduledTask && taskState != ScheduledTaskState.Missing;
var taskRunning = taskState == ScheduledTaskState.Running;
var taskCanStart = taskExists && !taskNeedsRepair && (taskState == ScheduledTaskState.Ready || taskState == ScheduledTaskState.Unknown);

InstallTaskButton.Content = taskNeedsRepair ? "Repair Task" : taskExists ? "Update Task" : "Install Task";
StartScheduledTaskButton.IsEnabled = taskCanStart && IsProfilePersisted(profile);
```

Preserve the existing enable/disable behavior for Save, Run Now, Preview Dry Run, Disable Task, Remove Task, and Refresh Status.

- [ ] **Step 8: Run query/UI build verification**

Run:

```powershell
dotnet run --project .\tests\Replicator.Tests\Replicator.Tests.csproj -- --filter "scheduled task query reports repair reasons"
dotnet build .\Replicator.sln
```

Expected: the query test passes and solution build succeeds.

- [ ] **Step 9: Commit task 2**

```powershell
git add .\tests\Replicator.Tests\Program.cs .\src\Replicator.Core\Scheduling\ScheduledTaskSnapshot.cs .\src\Replicator.Core\Scheduling\IScheduledTaskService.cs .\src\Replicator.Core\Scheduling\WindowsScheduledTaskService.cs .\src\Replicator.Core\Scripting\PowerShellScriptGenerator.cs .\src\Replicator.App\MainWindow.xaml.cs
git commit -m "feat: surface scheduled task repair state"
```

## Task 3: Generated Script Target Preflight

**Files:**
- Test: `tests/Replicator.Tests/Program.cs`
- Modify: `src/Replicator.Core/Scripting/PowerShellScriptGenerator.cs`

- [ ] **Step 1: Add failing script preflight test**

In `tests/Replicator.Tests/Program.cs`, add this registration near the existing script generator tests:

```csharp
("script generator emits target preflight status", ScriptGeneratorEmitsTargetPreflightStatus),
```

Add this test method:

```csharp
static Task ScriptGeneratorEmitsTargetPreflightStatus()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var generator = new PowerShellScriptGenerator(Path.Combine(root, "scripts"), Path.Combine(root, "logs"));

    var script = generator.Generate(ValidProfile());

    Assert(script.Content.Contains("Target path does not exist; dry run would create it during a real run:", StringComparison.Ordinal), "Expected dry-run target-path message.");
    Assert(script.Content.Contains("Write-Status -Message $message -ExitCode 0 -Succeeded $true", StringComparison.Ordinal), "Expected dry-run target status write.");
    Assert(
        script.Content.IndexOf("Target path does not exist; dry run would create it during a real run:", StringComparison.Ordinal)
            < script.Content.IndexOf("& robocopy", StringComparison.OrdinalIgnoreCase),
        "Expected target preflight before robocopy.");

    return Task.CompletedTask;
}
```

- [ ] **Step 2: Run test to verify red**

Run:

```powershell
dotnet run --project .\tests\Replicator.Tests\Replicator.Tests.csproj -- --filter "script generator emits target preflight status"
```

Expected: test fails because the generated script does not contain the new dry-run target preflight message.

- [ ] **Step 3: Add target preflight before robocopy**

In `src/Replicator.Core/Scripting/PowerShellScriptGenerator.cs`, within the generated script's main `try` block, replace the current destination creation block with:

```powershell
if (-not (Test-Path -LiteralPath $Destination -PathType Container)) {
    if ($effectiveDryRun) {
        $message = "Target path does not exist; dry run would create it during a real run: $Destination"
        Write-RunLog $message
        Write-Status -Message $message -ExitCode 0 -Succeeded $true
        exit 0
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
}
```

Keep this block after:

```powershell
Test-RootAvailable -Label 'Target' -Path $Destination
```

and before:

```powershell
$robocopyArgs = @(
```

- [ ] **Step 4: Run script preflight tests to verify green**

Run:

```powershell
dotnet run --project .\tests\Replicator.Tests\Replicator.Tests.csproj -- --filter "script generator"
```

Expected: all script generator tests pass.

- [ ] **Step 5: Commit task 3**

```powershell
git add .\tests\Replicator.Tests\Program.cs .\src\Replicator.Core\Scripting\PowerShellScriptGenerator.cs
git commit -m "fix: report dry-run target preflight"
```

## Task 4: Latest Run Status Reader

**Files:**
- Test: `tests/Replicator.Tests/Program.cs`
- Create: `src/Replicator.Core/Scripting/BackupRunStatus.cs`
- Create: `src/Replicator.Core/Scripting/BackupRunStatusReader.cs`
- Modify: `src/Replicator.App/MainWindow.xaml.cs`

- [ ] **Step 1: Add failing latest-status reader tests**

In `tests/Replicator.Tests/Program.cs`, add these registrations near the log reader tests:

```csharp
("status reader parses latest backup status", StatusReaderParsesLatestBackupStatus),
("status reader ignores malformed latest backup status", StatusReaderIgnoresMalformedLatestBackupStatus),
```

Add these test methods:

```csharp
static Task StatusReaderParsesLatestBackupStatus()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var logsDirectory = Path.Combine(root, "logs");
    Directory.CreateDirectory(logsDirectory);

    try
    {
        var profile = ValidProfile();
        var slug = PowerShellScriptGenerator.ProfileSlug(profile);
        var statusPath = Path.Combine(logsDirectory, $"{slug}-latest.json");
        File.WriteAllText(statusPath, """
{
  "ProfileName": "Scratch",
  "Mode": "DryRun",
  "Source": "D:\\repos\\work",
  "Destination": "H:\\dev\\work",
  "LogPath": "C:\\Users\\aprotteau\\AppData\\Local\\Replicator\\logs\\scratch.log",
  "StartedAt": "2026-05-21T20:00:00.0000000Z",
  "UpdatedAt": "2026-05-21T20:00:01.0000000Z",
  "ExitCode": 0,
  "Succeeded": true,
  "Message": "Target path does not exist; dry run would create it during a real run: H:\\dev\\work"
}
""");
        var reader = new BackupRunStatusReader(logsDirectory);

        var status = reader.ReadLatest(profile);

        if (status is null)
        {
            throw new InvalidOperationException("Expected latest status to be parsed.");
        }

        Assert(status.ProfileName == "Scratch", $"Unexpected profile name: {status.ProfileName}");
        Assert(status.Mode == "DryRun", $"Unexpected mode: {status.Mode}");
        Assert(status.ExitCode == 0, $"Unexpected exit code: {status.ExitCode}");
        Assert(status.Succeeded, "Expected status success.");
        Assert(status.Message.Contains("dry run would create", StringComparison.OrdinalIgnoreCase), $"Unexpected message: {status.Message}");
        Assert(status.ToDisplayString().Contains("DryRun", StringComparison.Ordinal), "Expected display string to include mode.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    return Task.CompletedTask;
}

static Task StatusReaderIgnoresMalformedLatestBackupStatus()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var logsDirectory = Path.Combine(root, "logs");
    Directory.CreateDirectory(logsDirectory);

    try
    {
        var profile = ValidProfile();
        var slug = PowerShellScriptGenerator.ProfileSlug(profile);
        File.WriteAllText(Path.Combine(logsDirectory, $"{slug}-latest.json"), "{ not json");
        var reader = new BackupRunStatusReader(logsDirectory);

        var status = reader.ReadLatest(profile);

        Assert(status is null, "Expected malformed status to be ignored.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    return Task.CompletedTask;
}
```

- [ ] **Step 2: Run tests to verify red**

Run:

```powershell
dotnet run --project .\tests\Replicator.Tests\Replicator.Tests.csproj -- --filter "status reader"
```

Expected: build fails because `BackupRunStatusReader` is not defined.

- [ ] **Step 3: Add backup run status model**

Create `src/Replicator.Core/Scripting/BackupRunStatus.cs`:

```csharp
namespace Replicator.Core.Scripting;

public sealed record BackupRunStatus(
    string ProfileName,
    string Mode,
    string Source,
    string Destination,
    string LogPath,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt,
    int? ExitCode,
    bool Succeeded,
    string Message)
{
    public string ToDisplayString()
    {
        var exit = ExitCode.HasValue ? $"exit {ExitCode.Value}" : "exit unavailable";
        var result = Succeeded ? "succeeded" : "failed";
        var updated = UpdatedAt.HasValue ? $" Updated {UpdatedAt.Value.LocalDateTime:g}." : string.Empty;

        return $"{Mode} {result} ({exit}). {Message}{updated}".Trim();
    }
}
```

- [ ] **Step 4: Add backup run status reader**

Create `src/Replicator.Core/Scripting/BackupRunStatusReader.cs`:

```csharp
using System.Text.Json;
using Replicator.Core.Scheduling;

namespace Replicator.Core.Scripting;

public sealed class BackupRunStatusReader(string logsDirectory)
{
    public BackupRunStatus? ReadLatest(BackupProfile profile)
    {
        var statusPath = Path.Combine(logsDirectory, $"{ProfileSlug(profile)}-latest.json");
        if (!File.Exists(statusPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(statusPath);
            var status = JsonSerializer.Deserialize<BackupRunStatusDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (status is null)
            {
                return null;
            }

            return new BackupRunStatus(
                status.ProfileName ?? string.Empty,
                status.Mode ?? string.Empty,
                status.Source ?? string.Empty,
                status.Destination ?? string.Empty,
                status.LogPath ?? string.Empty,
                status.StartedAt,
                status.UpdatedAt,
                status.ExitCode,
                status.Succeeded,
                status.Message ?? string.Empty);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ProfileSlug(BackupProfile profile)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var slug = new string(profile.Name
            .Trim()
            .ToLowerInvariant()
            .Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : ch)
            .ToArray());

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug) ? "profile" : slug.Trim('-');
    }

    private sealed class BackupRunStatusDto
    {
        public string? ProfileName { get; init; }

        public string? Mode { get; init; }

        public string? Source { get; init; }

        public string? Destination { get; init; }

        public string? LogPath { get; init; }

        public DateTimeOffset? StartedAt { get; init; }

        public DateTimeOffset? UpdatedAt { get; init; }

        public int? ExitCode { get; init; }

        public bool Succeeded { get; init; }

        public string? Message { get; init; }
    }
}
```

- [ ] **Step 5: Show latest JSON status in the app**

In `src/Replicator.App/MainWindow.xaml.cs`, add a field:

```csharp
private readonly BackupRunStatusReader _runStatusReader;
```

Initialize it with the existing paths:

```csharp
_runStatusReader = new BackupRunStatusReader(_paths.LogsDirectory);
```

Update `ShowLatestRun(BackupProfile profile)`:

```csharp
private void ShowLatestRun(BackupProfile profile)
{
    var status = _runStatusReader.ReadLatest(profile);
    var summary = _logReader.ReadLatest(profile);

    if (status is null && summary is null)
    {
        LatestLogTextBlock.Text = "No log has been written for this profile yet.";
        RunSummaryTextBlock.Text = "Run summary unavailable.";
        OutputTextBox.Text = string.Empty;
        return;
    }

    if (status is not null)
    {
        LatestLogTextBlock.Text = string.IsNullOrWhiteSpace(status.LogPath) ? "No log path was written for the latest run." : status.LogPath;
        RunSummaryTextBlock.Text = status.ToDisplayString();
        OutputTextBox.Text = summary?.Tail ?? status.Message;
        return;
    }

    LatestLogTextBlock.Text = summary!.LogPath;
    RunSummaryTextBlock.Text = summary.ToDisplayString();
    OutputTextBox.Text = summary.Tail;
}
```

- [ ] **Step 6: Run status-reader tests to verify green**

Run:

```powershell
dotnet run --project .\tests\Replicator.Tests\Replicator.Tests.csproj -- --filter "status reader"
dotnet build .\Replicator.sln
```

Expected: status reader tests pass and solution build succeeds.

- [ ] **Step 7: Commit task 4**

```powershell
git add .\tests\Replicator.Tests\Program.cs .\src\Replicator.Core\Scripting\BackupRunStatus.cs .\src\Replicator.Core\Scripting\BackupRunStatusReader.cs .\src\Replicator.App\MainWindow.xaml.cs
git commit -m "feat: read latest scheduled run status"
```

## Task 5: Docs, Smoke, Merge, and Push

**Files:**
- Modify: `docs/Smoke-Test-Plan.md`
- Review: `docs/superpowers/specs/2026-05-21-scheduled-run-reliability-design.md`
- Review: `docs/superpowers/plans/2026-05-21-scheduled-run-reliability.md`

- [ ] **Step 1: Update smoke-test documentation**

In `docs/Smoke-Test-Plan.md`, update the automated smoke test count from the current count to:

```text
33 test(s) passed
```

Add coverage language under the scheduled task or backup-mode smoke area:

```markdown
- Scheduled task actions are inspected for hidden/noninteractive PowerShell launch arguments and mismatched script paths.
- Generated backup scripts write a clear latest status when dry-run target preflight stops before robocopy.
- The app reads the selected profile's latest JSON status file before falling back to robocopy log summaries.
```

- [ ] **Step 2: Run focused test suite**

Run:

```powershell
dotnet run --project .\tests\Replicator.Tests\Replicator.Tests.csproj
```

Expected: `33 test(s) passed`.

- [ ] **Step 3: Run repository smoke tests**

Run:

```powershell
.\tools\run-smoke-tests.ps1
```

Expected: solution build succeeds, test harness reports `33 test(s) passed`, and smoke gates pass.

- [ ] **Step 4: Commit docs**

```powershell
git add .\docs\Smoke-Test-Plan.md .\docs\superpowers\plans\2026-05-21-scheduled-run-reliability.md
git commit -m "docs: document scheduled run reliability plan"
```

- [ ] **Step 5: Finish the branch**

Run:

```powershell
git status --short --branch
git log --oneline --decorate -5
git checkout main
git pull --ff-only
git merge --ff-only codex/scheduled-run-reliability
git push origin main
```

Expected: feature branch fast-forwards into `main` and pushes successfully to `origin/main`.

## Self-Review

- Spec coverage: Task action health covers visible PowerShell/robocopy windows and stale actions. Script preflight covers unavailable target and confusing dry-run runs. Status reader covers run logging visibility in the app. Docs/smoke covers repeatable verification.
- Placeholder scan: The plan contains concrete file paths, exact test names, exact command lines, and concrete code blocks for new types and UI integration points.
- Type consistency: `ScheduledTaskActionHealth`, `ScheduledTaskActionInspector.Inspect`, `ScheduledTaskSnapshot.NeedsRepair`, `PowerShellScriptGenerator.ScriptPathFor`, `BackupRunStatus`, and `BackupRunStatusReader.ReadLatest` are named consistently across tests, implementation, and UI steps.
