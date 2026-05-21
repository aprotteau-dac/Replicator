# Scheduled Task Action Center Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a launch-time scheduled task Action Center that discovers `\Replicator\` Task Scheduler entries, summarizes issues, and lets matched unhealthy tasks be reviewed and repaired.

**Architecture:** Add a Core inventory service that queries Task Scheduler, parses `schtasks /FO LIST /V` output, matches rows to known profiles, reuses `ScheduledTaskActionInspector`, and returns summary counts. Add WPF presentation state for a compact Action Center plus a review dialog, using the existing scheduled-task update path to repair matched unhealthy tasks.

**Tech Stack:** .NET 8, WPF, `schtasks.exe`, existing `IProcessRunner`, existing Replicator test harness.

---

## File Structure

- Create `src/Replicator.Core/Scheduling/ScheduledTaskInventoryState.cs`
  - Enum for `Ready`, `NeedsRepair`, `Orphaned`, `Running`, and `Unknown`.
- Create `src/Replicator.Core/Scheduling/ScheduledTaskInventoryItem.cs`
  - Immutable row model for the inventory dialog and tests.
- Create `src/Replicator.Core/Scheduling/ScheduledTaskInventorySummary.cs`
  - Count model plus `HasIssues` and display helpers.
- Create `src/Replicator.Core/Scheduling/ScheduledTaskInventoryResult.cs`
  - Inventory result containing summary, rows, and raw query output.
- Create `src/Replicator.Core/Scheduling/IScheduledTaskInventoryService.cs`
  - Service interface for scan operations.
- Create `src/Replicator.Core/Scheduling/WindowsScheduledTaskInventoryService.cs`
  - Windows implementation backed by `schtasks.exe`.
- Modify `tests/Replicator.Tests/Program.cs`
  - Add failing tests for classification, running-task repair gating, and query failures.
- Modify `src/Replicator.App/MainWindow.xaml`
  - Add compact Action Center panel.
- Modify `src/Replicator.App/MainWindow.xaml.cs`
  - Refresh inventory on launch/status/repair and add review/repair interactions.
- Modify `docs/Smoke-Test-Plan.md`
  - Add Action Center launch scan and Review Tasks smoke coverage.

## Task 1: Core Inventory Classification

**Files:**
- Test: `tests/Replicator.Tests/Program.cs`
- Create: `src/Replicator.Core/Scheduling/ScheduledTaskInventoryState.cs`
- Create: `src/Replicator.Core/Scheduling/ScheduledTaskInventoryItem.cs`
- Create: `src/Replicator.Core/Scheduling/ScheduledTaskInventorySummary.cs`
- Create: `src/Replicator.Core/Scheduling/ScheduledTaskInventoryResult.cs`
- Create: `src/Replicator.Core/Scheduling/IScheduledTaskInventoryService.cs`
- Create: `src/Replicator.Core/Scheduling/WindowsScheduledTaskInventoryService.cs`

- [ ] **Step 1: Write failing inventory classification tests**

Add registrations in `tests/Replicator.Tests/Program.cs` near the scheduled-task tests:

```csharp
("scheduled task inventory classifies matched repair and orphaned tasks", ScheduledTaskInventoryClassifiesMatchedRepairAndOrphanedTasks),
("scheduled task inventory disables repair for running tasks", ScheduledTaskInventoryDisablesRepairForRunningTasks),
("scheduled task inventory reports query failure as unknown", ScheduledTaskInventoryReportsQueryFailureAsUnknown),
```

Add these test methods:

```csharp
static async Task ScheduledTaskInventoryClassifiesMatchedRepairAndOrphanedTasks()
{
    var profile = ValidProfile();
    var expectedScript = Path.Combine(Path.GetTempPath(), $"expected-{Guid.NewGuid():N}.ps1");
    var orphanScript = Path.Combine(Path.GetTempPath(), $"orphan-{Guid.NewGuid():N}.ps1");
    File.WriteAllText(orphanScript, "# orphan");

    try
    {
        var output = $"""
TaskName:                             {ScheduledTaskName.ForProfile(profile)}
Next Run Time:                        5/21/2026 2:00:00 PM
Status:                               Ready
Last Run Time:                        5/21/2026 1:00:00 PM
Last Result:                          0
Task To Run:                          powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{orphanScript}"

TaskName:                             \Replicator\Legacy-Replicator-Task
Next Run Time:                        5/21/2026 3:00:00 PM
Status:                               Ready
Last Run Time:                        5/21/2026 12:00:00 PM
Last Result:                          0
Task To Run:                          powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "{orphanScript}"
""";
        var service = new WindowsScheduledTaskInventoryService(new FakeProcessRunner(new ProcessResult(0, output, string.Empty)));

        var result = await service.ScanAsync([profile], new Dictionary<Guid, string> { [profile.Id] = expectedScript });

        Assert(result.Summary.Total == 2, $"Expected two inventory rows, got {result.Summary.Total}.");
        Assert(result.Summary.NeedsRepair == 1, $"Expected one repair row, got {result.Summary.NeedsRepair}.");
        Assert(result.Summary.Orphaned == 1, $"Expected one orphan row, got {result.Summary.Orphaned}.");
        Assert(result.Summary.HasIssues, "Expected summary to show issues.");

        var matched = result.Items.Single(item => item.ProfileId == profile.Id);
        Assert(matched.InventoryState == ScheduledTaskInventoryState.NeedsRepair, $"Unexpected matched state: {matched.InventoryState}.");
        Assert(matched.CanRepair, "Expected matched unhealthy task to be repairable.");
        Assert(matched.Reason.Contains("-WindowStyle Hidden", StringComparison.OrdinalIgnoreCase), $"Unexpected repair reason: {matched.Reason}");

        var orphan = result.Items.Single(item => item.InventoryState == ScheduledTaskInventoryState.Orphaned);
        Assert(orphan.ProfileId is null, "Expected orphan row to have no profile id.");
        Assert(!orphan.CanRepair, "Orphaned tasks should not be repairable in this slice.");
    }
    finally
    {
        if (File.Exists(orphanScript))
        {
            File.Delete(orphanScript);
        }
    }
}

static async Task ScheduledTaskInventoryDisablesRepairForRunningTasks()
{
    var profile = ValidProfile();
    var script = Path.Combine(Path.GetTempPath(), $"running-{Guid.NewGuid():N}.ps1");
    File.WriteAllText(script, "# running");

    try
    {
        var output = $"""
TaskName:                             {ScheduledTaskName.ForProfile(profile)}
Next Run Time:                        N/A
Status:                               Running
Last Run Time:                        5/21/2026 1:00:00 PM
Last Result:                          267009
Task To Run:                          powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "{script}"
""";
        var service = new WindowsScheduledTaskInventoryService(new FakeProcessRunner(new ProcessResult(0, output, string.Empty)));

        var result = await service.ScanAsync([profile], new Dictionary<Guid, string> { [profile.Id] = script });

        var item = result.Items.Single();
        Assert(item.InventoryState == ScheduledTaskInventoryState.Running, $"Unexpected state: {item.InventoryState}.");
        Assert(!item.CanRepair, "Running tasks should not be repairable.");
        Assert(result.Summary.Running == 1, $"Expected one running task, got {result.Summary.Running}.");
    }
    finally
    {
        if (File.Exists(script))
        {
            File.Delete(script);
        }
    }
}

static async Task ScheduledTaskInventoryReportsQueryFailureAsUnknown()
{
    var service = new WindowsScheduledTaskInventoryService(new FakeProcessRunner(new ProcessResult(1, string.Empty, "Access is denied.")));

    var result = await service.ScanAsync([ValidProfile()], new Dictionary<Guid, string>());

    Assert(result.Summary.Total == 1, $"Expected one unknown inventory row, got {result.Summary.Total}.");
    Assert(result.Summary.Unknown == 1, $"Expected one unknown row, got {result.Summary.Unknown}.");
    Assert(result.Summary.HasIssues, "Expected query failure to show an Action Center issue.");
    Assert(result.Items[0].InventoryState == ScheduledTaskInventoryState.Unknown, $"Unexpected state: {result.Items[0].InventoryState}.");
    Assert(result.Items[0].Reason.Contains("Access is denied", StringComparison.OrdinalIgnoreCase), $"Unexpected reason: {result.Items[0].Reason}");
}
```

- [ ] **Step 2: Run tests to verify red**

Run:

```powershell
dotnet run --project .\tests\Replicator.Tests\Replicator.Tests.csproj -- --filter "scheduled task inventory"
```

Expected: build fails because the inventory service/types do not exist.

- [ ] **Step 3: Add inventory models**

Create `src/Replicator.Core/Scheduling/ScheduledTaskInventoryState.cs`:

```csharp
namespace Replicator.Core.Scheduling;

public enum ScheduledTaskInventoryState
{
    Ready,
    NeedsRepair,
    Orphaned,
    Running,
    Unknown
}
```

Create `src/Replicator.Core/Scheduling/ScheduledTaskInventoryItem.cs`:

```csharp
namespace Replicator.Core.Scheduling;

public sealed record ScheduledTaskInventoryItem(
    string TaskName,
    Guid? ProfileId,
    string ProfileName,
    ScheduledTaskInventoryState InventoryState,
    ScheduledTaskState TaskState,
    string NextRunTime,
    string LastRunTime,
    int LastResult,
    string TaskToRun,
    string ScriptPath,
    bool ScriptExists,
    IReadOnlyList<string> RepairReasons,
    string Reason,
    string RawOutput)
{
    public bool CanRepair => ProfileId.HasValue
        && InventoryState == ScheduledTaskInventoryState.NeedsRepair
        && TaskState != ScheduledTaskState.Running;
}
```

Create `src/Replicator.Core/Scheduling/ScheduledTaskInventorySummary.cs`:

```csharp
namespace Replicator.Core.Scheduling;

public sealed record ScheduledTaskInventorySummary(
    int Total,
    int Ready,
    int NeedsRepair,
    int Orphaned,
    int Running,
    int Unknown)
{
    public bool HasIssues => NeedsRepair > 0 || Orphaned > 0 || Unknown > 0;

    public string ToDisplayString()
    {
        if (Total == 0)
        {
            return "No Replicator scheduled tasks found.";
        }

        if (!HasIssues)
        {
            return $"Scheduled tasks checked: {Ready} ready, {Running} running.";
        }

        var parts = new List<string>();
        if (NeedsRepair > 0) parts.Add($"{NeedsRepair} need repair");
        if (Orphaned > 0) parts.Add($"{Orphaned} orphaned");
        if (Unknown > 0) parts.Add($"{Unknown} unknown");
        if (Running > 0) parts.Add($"{Running} running");
        if (Ready > 0) parts.Add($"{Ready} ready");

        return $"{Total} scheduled task(s) found: {string.Join(", ", parts)}.";
    }
}
```

Create `src/Replicator.Core/Scheduling/ScheduledTaskInventoryResult.cs`:

```csharp
namespace Replicator.Core.Scheduling;

public sealed record ScheduledTaskInventoryResult(
    IReadOnlyList<ScheduledTaskInventoryItem> Items,
    ScheduledTaskInventorySummary Summary,
    string RawOutput);
```

Create `src/Replicator.Core/Scheduling/IScheduledTaskInventoryService.cs`:

```csharp
using Replicator.Core.Models;

namespace Replicator.Core.Scheduling;

public interface IScheduledTaskInventoryService
{
    Task<ScheduledTaskInventoryResult> ScanAsync(
        IReadOnlyList<BackupProfile> profiles,
        IReadOnlyDictionary<Guid, string> expectedScriptPaths,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Add Windows inventory service**

Create `src/Replicator.Core/Scheduling/WindowsScheduledTaskInventoryService.cs` with a `ScanAsync` implementation that:

- runs `schtasks.exe /Query /FO LIST /V`
- returns one `Unknown` item when the query fails
- parses LIST output into task records split by blank lines or repeated `TaskName`
- filters records to task names starting with `\Replicator\`
- matches profiles by `ScheduledTaskName.ForProfile(profile)`
- inspects matched actions with `ScheduledTaskActionInspector.Inspect(taskToRun, expectedScriptPath)`
- classifies unmatched tasks as `Orphaned`
- classifies matched running tasks as `Running`
- classifies matched tasks with repair reasons as `NeedsRepair`
- classifies matched healthy tasks as `Ready`

- [ ] **Step 5: Run inventory tests to verify green**

Run:

```powershell
dotnet run --project .\tests\Replicator.Tests\Replicator.Tests.csproj -- --filter "scheduled task inventory"
```

Expected: inventory tests pass and the harness reports `36 test(s) passed`.

- [ ] **Step 6: Commit task 1**

```powershell
git add .\tests\Replicator.Tests\Program.cs .\src\Replicator.Core\Scheduling\ScheduledTaskInventoryState.cs .\src\Replicator.Core\Scheduling\ScheduledTaskInventoryItem.cs .\src\Replicator.Core\Scheduling\ScheduledTaskInventorySummary.cs .\src\Replicator.Core\Scheduling\ScheduledTaskInventoryResult.cs .\src\Replicator.Core\Scheduling\IScheduledTaskInventoryService.cs .\src\Replicator.Core\Scheduling\WindowsScheduledTaskInventoryService.cs
git commit -m "feat: inventory scheduled tasks"
```

## Task 2: Action Center And Review UI

**Files:**
- Modify: `src/Replicator.App/MainWindow.xaml`
- Modify: `src/Replicator.App/MainWindow.xaml.cs`
- Create: `src/Replicator.App/ScheduledTaskInventoryWindow.xaml`
- Create: `src/Replicator.App/ScheduledTaskInventoryWindow.xaml.cs`

- [ ] **Step 1: Add Action Center panel to XAML**

Add an `Auto` row between the header and the form, then move the form and command bar down one row. Add a collapsed `Border` named `TaskActionCenterPanel` with:

- title text block named `TaskActionCenterTitleTextBlock`
- summary text block named `TaskActionCenterSummaryTextBlock`
- `ReviewTaskInventoryButton`
- `RepairSelectedInventoryTaskButton`

- [ ] **Step 2: Add inventory service fields and state**

In `MainWindow.xaml.cs`, add:

```csharp
private readonly IScheduledTaskInventoryService _taskInventoryService;
private ScheduledTaskInventoryResult? _taskInventory;
```

Initialize:

```csharp
_taskInventoryService = new WindowsScheduledTaskInventoryService(_processRunner);
```

- [ ] **Step 3: Refresh inventory on launch and after task mutations**

Add:

```csharp
private async Task RefreshTaskInventoryAsync()
{
    var expectedPaths = _profiles.ToDictionary(profile => profile.Id, profile => _scriptGenerator.ScriptPathFor(profile));
    _taskInventory = await _taskInventoryService.ScanAsync(_profiles.ToList(), expectedPaths);
    ShowTaskActionCenter(_taskInventory);
}
```

Call `RefreshTaskInventoryAsync` after profile load, after install/update, after enable/disable/delete, and after manual refresh.

- [ ] **Step 4: Show Action Center summary**

Add `ShowTaskActionCenter(ScheduledTaskInventoryResult? inventory)`:

- collapse when inventory is null or `!inventory.Summary.HasIssues`
- show title `Scheduled task review needed`
- show `inventory.Summary.ToDisplayString()`
- enable `ReviewTaskInventoryButton` when rows exist
- enable `RepairSelectedInventoryTaskButton` only when the selected profile has a repairable inventory row

- [ ] **Step 5: Add review inventory window**

Create `src/Replicator.App/ScheduledTaskInventoryWindow.xaml` as a compact review window with:

- heading `Scheduled Tasks`
- summary text block named `SummaryTextBlock`
- `DataGrid` named `TasksGrid`
- columns for state, profile name, task name, reason, last run, and last result
- `Close` button

Create `src/Replicator.App/ScheduledTaskInventoryWindow.xaml.cs`:

```csharp
using System.Windows;
using Replicator.Core.Scheduling;

namespace Replicator.App;

public partial class ScheduledTaskInventoryWindow : Window
{
    public ScheduledTaskInventoryWindow(ScheduledTaskInventoryResult inventory)
    {
        InitializeComponent();
        SummaryTextBlock.Text = inventory.Summary.ToDisplayString();
        TasksGrid.ItemsSource = inventory.Items;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
```

Add `ReviewTaskInventory_Click` in `MainWindow.xaml.cs` that opens `ScheduledTaskInventoryWindow` with `_taskInventory`.

- [ ] **Step 6: Add selected-profile repair from Action Center**

Add `RepairSelectedInventoryTask_Click` that calls the same save/generate/install/update flow as `InstallTask_Click`, then refreshes selected status and inventory.

- [ ] **Step 7: Build app**

Run:

```powershell
dotnet build .\Replicator.sln
```

Expected: build succeeds with 0 warnings and 0 errors.

- [ ] **Step 8: Commit task 2**

```powershell
git add .\src\Replicator.App\MainWindow.xaml .\src\Replicator.App\MainWindow.xaml.cs .\src\Replicator.App\ScheduledTaskInventoryWindow.xaml .\src\Replicator.App\ScheduledTaskInventoryWindow.xaml.cs
git commit -m "feat: add scheduled task action center"
```

## Task 3: Docs And Smoke

**Files:**
- Modify: `docs/Smoke-Test-Plan.md`
- Modify: `docs/Backup-Mode.md`

- [ ] **Step 1: Update docs**

Update expected automated test count to:

```text
36 test(s) passed.
```

Add smoke coverage for:

- launch-time scheduled task inventory scan
- Action Center visibility when issues exist
- `Review Tasks` inventory output
- selected-profile repair from Action Center

- [ ] **Step 2: Run full tests**

Run:

```powershell
dotnet run --project .\tests\Replicator.Tests\Replicator.Tests.csproj
```

Expected: `36 test(s) passed.`

- [ ] **Step 3: Run smoke tests**

Run:

```powershell
.\tools\run-smoke-tests.ps1
```

Expected: solution builds, test harness reports `36 test(s) passed`, and `Replicator smoke gates passed.`

- [ ] **Step 4: Commit task 3**

```powershell
git add .\docs\Smoke-Test-Plan.md .\docs\Backup-Mode.md
git commit -m "docs: document scheduled task action center"
```

## Self-Review

- Spec coverage: core scan/classification, Action Center summary, Review Tasks, and selected-profile repair are covered.
- Scope check: orphan adopt/remove and repair-all are not included in implementation tasks.
- Placeholder scan: all tasks include exact files, commands, and expected verification outcomes.
- Type consistency: inventory model names and service signatures are consistent across tests, app, and docs.
