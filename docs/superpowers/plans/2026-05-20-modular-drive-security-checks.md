# Modular Drive Security Checks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep Replicator usable without administrator launch while making BitLocker permission limits explicit, testable, and modular.

**Architecture:** Replicator remains a standard-user app by default. BitLocker query failures are classified by a small security classifier so access-denied results become `PermissionRequired` warnings, while unavailable drives remain errors and unknown parser/tool failures remain unknown warnings. Process launching is abstracted behind `IProcessRunner` so security providers can be tested without reflection or real PowerShell.

**Tech Stack:** .NET 8, C#, WPF, Windows PowerShell BitLocker cmdlets, existing single-file console test harness, existing PowerShell smoke script.

---

## Scope Decision

Do not require administrator privileges for the whole app. The security check should run in the standard app session, classify permission-limited BitLocker checks as a distinct warning, and continue to let backup/shuttle workflows run because drive-security enforcement is still visibility-only. An elevated verification path can be added as a separate explicit user action after this state model exists.

This plan does not add UAC prompts, self-elevation, or blocking policy. It makes the current warning accurate and gives the next elevated-check feature a clean surface to build on.

## File Structure

- `src/Replicator.Core/Security/DriveSecurityState.cs`: add `PermissionRequired` as a first-class posture state.
- `src/Replicator.Core/Security/BitLockerQueryFailureClassifier.cs`: create a focused classifier for access-denied, unavailable, and unknown BitLocker query failures.
- `src/Replicator.Core/Security/PowerShellBitLockerStatusProvider.cs`: delegate command-failure classification to the new classifier.
- `src/Replicator.Core/Execution/IProcessRunner.cs`: create a narrow process-runner interface for tests and modular providers.
- `src/Replicator.Core/Execution/ProcessRunner.cs`: implement `IProcessRunner`.
- `src/Replicator.Core/Scheduling/WindowsScheduledTaskService.cs`: accept `IProcessRunner` without changing behavior.
- `tests/Replicator.Tests/Program.cs`: add focused regression tests and a fake process runner.
- `docs/Drive-Security.md`: document standard-user-first behavior and permission-required warnings.
- `docs/Smoke-Test-Plan.md`: update automated count and manual BitLocker posture expectations.

## Task 1: Classify Permission-Limited BitLocker Results

**Files:**
- Modify: `tests/Replicator.Tests/Program.cs`
- Modify: `src/Replicator.Core/Security/DriveSecurityState.cs`
- Create: `src/Replicator.Core/Security/BitLockerQueryFailureClassifier.cs`

- [ ] **Step 1: Write the failing classifier test**

In `tests/Replicator.Tests/Program.cs`, replace this test registration:

```csharp
("bitlocker access denied reason is actionable", BitLockerAccessDeniedReasonIsActionable),
```

with this registration:

```csharp
("bitlocker access denied is classified as permission required", BitLockerAccessDeniedIsClassifiedAsPermissionRequired),
```

Replace the existing `BitLockerAccessDeniedReasonIsActionable` method with this method:

```csharp
static Task BitLockerAccessDeniedIsClassifiedAsPermissionRequired()
{
    var item = BitLockerQueryFailureClassifier.ToSecurityItem(
        "Source drive",
        @"D:\repos\personal",
        @"D:\",
        "Get-CimInstance : Access denied");

    Assert(item.State == DriveSecurityState.PermissionRequired, $"Expected permission-required state, got {item.State}.");
    Assert(item.Severity == DriveSecuritySeverity.Warning, "Permission-required posture should warn without blocking.");
    Assert(item.Message.Contains(@"D:\", StringComparison.OrdinalIgnoreCase), $"Expected drive root in message: {item.Message}");
    Assert(item.Message.Contains("elevated permissions", StringComparison.OrdinalIgnoreCase), $"Expected elevation guidance: {item.Message}");
    Assert(!item.Message.Contains("Get-CimInstance", StringComparison.OrdinalIgnoreCase), $"Expected raw PowerShell command text to be hidden: {item.Message}");
    Assert(!item.Message.Contains("Access denied", StringComparison.OrdinalIgnoreCase), $"Expected raw access-denied text to be hidden: {item.Message}");
    return Task.CompletedTask;
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet run --project tests\Replicator.Tests\Replicator.Tests.csproj
```

Expected: build failure because `BitLockerQueryFailureClassifier` and `DriveSecurityState.PermissionRequired` do not exist.

- [ ] **Step 3: Add the permission-required state**

Change `src/Replicator.Core/Security/DriveSecurityState.cs` to:

```csharp
namespace Replicator.Core.Security;

public enum DriveSecurityState
{
    Unknown = 0,
    Protected = 1,
    Unprotected = 2,
    Locked = 3,
    Unavailable = 4,
    NotApplicable = 5,
    PermissionRequired = 6
}
```

- [ ] **Step 4: Add the failure classifier**

Create `src/Replicator.Core/Security/BitLockerQueryFailureClassifier.cs`:

```csharp
namespace Replicator.Core.Security;

public static class BitLockerQueryFailureClassifier
{
    public static DriveSecurityItem ToSecurityItem(string label, string path, string root, string reason)
    {
        if (IsUnavailableMessage(reason))
        {
            return new DriveSecurityItem(
                label,
                path,
                root,
                DriveSecurityState.Unavailable,
                DriveSecuritySeverity.Error,
                $"Drive security: {label} is unavailable ({root}). {reason}");
        }

        if (IsPermissionDeniedMessage(reason))
        {
            return new DriveSecurityItem(
                label,
                path,
                root,
                DriveSecurityState.PermissionRequired,
                DriveSecuritySeverity.Warning,
                $"Drive security: {label} BitLocker status requires elevated permissions ({root}). Replicator can continue, but encryption state was not confirmed.");
        }

        return new DriveSecurityItem(
            label,
            path,
            root,
            DriveSecurityState.Unknown,
            DriveSecuritySeverity.Warning,
            $"Drive security: {label} BitLocker status unknown ({root}). {reason}");
    }

    private static bool IsUnavailableMessage(string message)
    {
        return message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPermissionDeniedMessage(string message)
    {
        return message.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run:

```powershell
dotnet run --project tests\Replicator.Tests\Replicator.Tests.csproj
```

Expected: all existing tests pass, now with `24 test(s) passed.` because this task replaces one test rather than adding a new one.

- [ ] **Step 6: Commit the classifier slice**

Run:

```powershell
git add tests\Replicator.Tests\Program.cs src\Replicator.Core\Security\DriveSecurityState.cs src\Replicator.Core\Security\BitLockerQueryFailureClassifier.cs
git commit -m "feat: classify permission-limited drive security checks"
```

## Task 2: Wire Provider Behavior Through a Testable Process Runner

**Files:**
- Modify: `tests/Replicator.Tests/Program.cs`
- Create: `src/Replicator.Core/Execution/IProcessRunner.cs`
- Modify: `src/Replicator.Core/Execution/ProcessRunner.cs`
- Modify: `src/Replicator.Core/Security/PowerShellBitLockerStatusProvider.cs`
- Modify: `src/Replicator.Core/Scheduling/WindowsScheduledTaskService.cs`

- [ ] **Step 1: Write the failing provider behavior test**

In `tests/Replicator.Tests/Program.cs`, add this using near the existing `Replicator.Core` imports:

```csharp
using Replicator.Core.Execution;
```

Add this test registration after the classifier test:

```csharp
("powershell bitlocker provider maps access denied to permission required", PowerShellBitLockerProviderMapsAccessDeniedToPermissionRequired),
```

Add this test method after `BitLockerAccessDeniedIsClassifiedAsPermissionRequired`:

```csharp
static async Task PowerShellBitLockerProviderMapsAccessDeniedToPermissionRequired()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var runner = new FakeProcessRunner(new ProcessResult(1, "", "Get-CimInstance : Access denied"));
    var item = await new PowerShellBitLockerStatusProvider(runner).CheckAsync(
        "Source drive",
        @"D:\repos\personal",
        @"D:\");

    Assert(item.State == DriveSecurityState.PermissionRequired, $"Expected permission-required state, got {item.State}.");
    Assert(item.Severity == DriveSecuritySeverity.Warning, "Permission-required provider result should warn without blocking.");
    Assert(item.Message.Contains("elevated permissions", StringComparison.OrdinalIgnoreCase), $"Expected elevation guidance: {item.Message}");
    Assert(!item.Message.Contains("Get-CimInstance", StringComparison.OrdinalIgnoreCase), $"Expected raw command details to be hidden: {item.Message}");
}
```

Add this fake runner near the existing fake test classes at the bottom of the file:

```csharp
sealed class FakeProcessRunner(ProcessResult result) : IProcessRunner
{
    public IReadOnlyList<string> LastArguments { get; private set; } = [];

    public Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
    {
        LastArguments = arguments.ToList();
        return Task.FromResult(result);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet run --project tests\Replicator.Tests\Replicator.Tests.csproj
```

Expected: build failure because `IProcessRunner` does not exist and `PowerShellBitLockerStatusProvider` still requires a concrete `ProcessRunner`.

- [ ] **Step 3: Add `IProcessRunner`**

Create `src/Replicator.Core/Execution/IProcessRunner.cs`:

```csharp
namespace Replicator.Core.Execution;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Update the concrete process runner**

Change the class declaration in `src/Replicator.Core/Execution/ProcessRunner.cs` from:

```csharp
public sealed class ProcessRunner
```

to:

```csharp
public sealed class ProcessRunner : IProcessRunner
```

- [ ] **Step 5: Update constructor dependencies**

Change the class declaration in `src/Replicator.Core/Security/PowerShellBitLockerStatusProvider.cs` from:

```csharp
public sealed class PowerShellBitLockerStatusProvider(ProcessRunner processRunner) : IBitLockerStatusProvider
```

to:

```csharp
public sealed class PowerShellBitLockerStatusProvider(IProcessRunner processRunner) : IBitLockerStatusProvider
```

Change the class declaration in `src/Replicator.Core/Scheduling/WindowsScheduledTaskService.cs` from:

```csharp
public sealed class WindowsScheduledTaskService(ProcessRunner processRunner) : IScheduledTaskService
```

to:

```csharp
public sealed class WindowsScheduledTaskService(IProcessRunner processRunner) : IScheduledTaskService
```

- [ ] **Step 6: Delegate BitLocker failure states to the classifier**

Replace the full contents of `src/Replicator.Core/Security/PowerShellBitLockerStatusProvider.cs` with:

```csharp
using Replicator.Core.Execution;

namespace Replicator.Core.Security;

public sealed class PowerShellBitLockerStatusProvider(IProcessRunner processRunner) : IBitLockerStatusProvider
{
    public async Task<DriveSecurityItem> CheckAsync(
        string label,
        string path,
        string root,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return BitLockerQueryFailureClassifier.ToSecurityItem(label, path, root, "BitLocker status is only available on Windows.");
        }

        var mountPoint = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(mountPoint))
        {
            return BitLockerQueryFailureClassifier.ToSecurityItem(label, path, root, "Drive root is unavailable.");
        }

        var escapedMountPoint = mountPoint.Replace("'", "''");
        var command = string.Join(
            Environment.NewLine,
            [
                $"$volume = Get-BitLockerVolume -MountPoint '{escapedMountPoint}' -ErrorAction Stop",
                "[ordered]@{",
                "    MountPoint = $volume.MountPoint",
                "    VolumeStatus = $volume.VolumeStatus.ToString()",
                "    ProtectionStatus = $volume.ProtectionStatus.ToString()",
                "    LockStatus = $volume.LockStatus.ToString()",
                "    EncryptionPercentage = $volume.EncryptionPercentage",
                "} | ConvertTo-Json -Compress"
            ]);

        try
        {
            var result = await processRunner.RunAsync(
                "powershell.exe",
                [
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    command
                ],
                cancellationToken);

            if (!result.Succeeded)
            {
                var message = FirstNonEmptyLine(result.StandardError) ?? FirstNonEmptyLine(result.StandardOutput) ?? "BitLocker status command failed.";
                return BitLockerQueryFailureClassifier.ToSecurityItem(label, path, root, message);
            }

            return BitLockerStatusParser.TryParseJson(result.StandardOutput, out var status)
                ? BitLockerStatusParser.ToSecurityItem(label, path, root, status)
                : BitLockerQueryFailureClassifier.ToSecurityItem(label, path, root, "BitLocker status output could not be parsed.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return BitLockerQueryFailureClassifier.ToSecurityItem(label, path, root, exception.Message);
        }
    }

    private static string? FirstNonEmptyLine(string text)
    {
        return text
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }
}
```

- [ ] **Step 7: Run the test to verify it passes**

Run:

```powershell
dotnet run --project tests\Replicator.Tests\Replicator.Tests.csproj
```

Expected: `25 test(s) passed.`

- [ ] **Step 8: Commit the provider wiring**

Run:

```powershell
git add tests\Replicator.Tests\Program.cs src\Replicator.Core\Execution\IProcessRunner.cs src\Replicator.Core\Execution\ProcessRunner.cs src\Replicator.Core\Security\PowerShellBitLockerStatusProvider.cs src\Replicator.Core\Scheduling\WindowsScheduledTaskService.cs
git commit -m "refactor: modularize drive security process checks"
```

## Task 3: Preserve Visibility-Only UX Semantics

**Files:**
- Modify: `tests/Replicator.Tests/Program.cs`

- [ ] **Step 1: Write the report regression test**

In `tests/Replicator.Tests/Program.cs`, add this test registration after the provider behavior test:

```csharp
("drive security report treats permission required as a warning", DriveSecurityReportTreatsPermissionRequiredAsWarning),
```

Add this test method after `PowerShellBitLockerProviderMapsAccessDeniedToPermissionRequired`:

```csharp
static Task DriveSecurityReportTreatsPermissionRequiredAsWarning()
{
    var report = new ProfileDriveSecurityReport(
    [
        new DriveSecurityItem(
            "Source drive",
            @"D:\repos\personal",
            @"D:\",
            DriveSecurityState.PermissionRequired,
            DriveSecuritySeverity.Warning,
            @"Drive security: Source drive BitLocker status requires elevated permissions (D:\). Replicator can continue, but encryption state was not confirmed."),
        new DriveSecurityItem(
            "Target drive",
            @"H:\dev\personal",
            @"H:\",
            DriveSecurityState.Protected,
            DriveSecuritySeverity.Info,
            @"Drive security: Target drive is BitLocker protected (H:\).")
    ]);

    Assert(report.HasWarnings, "Permission-required posture should set warning status.");
    Assert(!report.HasErrors, "Permission-required posture should not be an error while drive-security is visibility-only.");
    Assert(report.Summary.Contains("requires elevated permissions", StringComparison.OrdinalIgnoreCase), $"Unexpected summary: {report.Summary}");
    return Task.CompletedTask;
}
```

- [ ] **Step 2: Run the test**

Run:

```powershell
dotnet run --project tests\Replicator.Tests\Replicator.Tests.csproj
```

Expected: `26 test(s) passed.` If this fails, keep `PermissionRequired` severity as `DriveSecuritySeverity.Warning`; do not promote it to `Error`.

- [ ] **Step 3: Commit the UX regression test**

Run:

```powershell
git add tests\Replicator.Tests\Program.cs
git commit -m "test: lock drive security permission warning semantics"
```

## Task 4: Update Documentation

**Files:**
- Modify: `docs/Drive-Security.md`
- Modify: `docs/Smoke-Test-Plan.md`

- [ ] **Step 1: Update drive-security docs**

In `docs/Drive-Security.md`, replace the "Current Implementation" section with:

```markdown
## Current Implementation

Replicator performs a visibility-only BitLocker posture check for local Windows drive roots used by a profile. The app runs this check from the normal user session; it does not require Replicator itself to be launched as administrator.

The app checks:

- source drive
- backup target drive
- shuttle drive

The header can show:

- BitLocker protected
- not BitLocker protected
- BitLocker locked
- unavailable
- permission required for BitLocker verification
- unknown

If Windows denies the BitLocker status query, Replicator reports a permission-required warning instead of showing raw PowerShell/CIM error text. Backups and shuttle actions are not blocked by this warning because drive security is still visibility-only.

Administrator elevation should remain a narrow verification action, not an app-wide requirement. A later elevated verification command can reuse the same drive-security state model without changing normal profile editing, backup, or shuttle flows.

This is not yet an enforcement control. Replicator warns about unprotected, permission-limited, or unknown drives, but it does not block backup or shuttle writes yet.
```

- [ ] **Step 2: Update smoke-test docs**

In `docs/Smoke-Test-Plan.md`, change the automated expected result from:

```text
24 test(s) passed.
```

to:

```text
26 test(s) passed.
```

In the automated gate coverage list, replace:

```markdown
- BitLocker access-denied message formatting
```

with:

```markdown
- BitLocker permission-required classification and provider mapping
```

In the BitLocker posture smoke pass criteria, replace:

```markdown
- protected, unprotected, locked, unavailable, or unknown states are visible
```

with:

```markdown
- protected, unprotected, locked, unavailable, permission-required, or unknown states are visible
```

In the stop criteria, replace:

```markdown
- BitLocker command failure crashes the app instead of reporting unknown posture
```

with:

```markdown
- BitLocker command failure crashes the app instead of reporting unavailable, permission-required, or unknown posture
```

- [ ] **Step 3: Commit the docs**

Run:

```powershell
git add docs\Drive-Security.md docs\Smoke-Test-Plan.md
git commit -m "docs: document modular drive security checks"
```

## Task 5: Final Verification and Push

**Files:**
- Verify: full repository

- [ ] **Step 1: Run the full smoke gate**

Run:

```powershell
.\tools\run-smoke-tests.ps1
```

Expected:

```text
Build succeeded.
26 test(s) passed.
Replicator smoke gates passed.
```

- [ ] **Step 2: Inspect the final diff**

Run:

```powershell
git status --short
git log --oneline -3
```

Expected: clean worktree and three new local commits:

```text
test: lock drive security permission warning semantics
refactor: modularize drive security process checks
feat: classify permission-limited drive security checks
```

- [ ] **Step 3: Push**

Run:

```powershell
git push
```

Expected: `main` pushes successfully to the GitHub remote.

## Self-Review

- Spec coverage: the plan keeps Replicator standard-user by default, makes access-denied BitLocker checks modular, preserves warning-only behavior, and documents why app-wide admin is not the direction.
- Placeholder scan: no task relies on unspecified code or unnamed tests.
- Type consistency: `IProcessRunner`, `BitLockerQueryFailureClassifier`, `DriveSecurityState.PermissionRequired`, and the new tests use consistent names and signatures throughout.
