# Smoke Test Plan

This plan is the repeatable pre-release smoke pass for Replicator. It focuses on the current prototype risk areas: backup execution, scheduled tasks, unavailable paths, shuttle handoff, shuttle progress/cancellation, and BitLocker posture visibility.

## Automated Gate

Run from the repository root:

```powershell
.\tools\run-smoke-tests.ps1
```

Expected result:

```text
Build succeeded.
17 test(s) passed.
Replicator smoke gates passed.
```

This gate covers:

- profile validation
- script generation
- robocopy log parsing
- JSON profile storage
- shuttle prepare, depart, dock, receive, and conflict preservation
- shuttle manifest file entries and hash-backed skip analysis
- shuttle progress reporting
- shuttle cancellation token handling
- scheduled task naming
- minute schedule command generation and validation
- default development excludes
- availability checks for missing source, creatable target, and unavailable drive
- BitLocker parser classification
- profile drive-security summary behavior

Optional long shuttle smoke:

```powershell
$env:REPLICATOR_LONG_SHUTTLE_SMOKE = '1'
dotnet run --project tests\Replicator.Tests\Replicator.Tests.csproj
Remove-Item Env:\REPLICATOR_LONG_SHUTTLE_SMOKE
```

Expected result: the extra long smoke creates 6,500 matching files with drifted local timestamps, docks them as skips through manifest-hash comparison, and prints the elapsed dock-analysis time.

## Manual Windows Smoke

Use disposable folders. Do not point this smoke run at a real workstream.

Suggested setup:

```powershell
$root = "$env:TEMP\replicator-smoke"
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$root\source", "$root\target", "$root\external\Replicator\shuttle\repo" | Out-Null
Set-Content -LiteralPath "$root\source\note.md" -Value "first"
```

Start the app:

```powershell
dotnet run --project src\Replicator.App\Replicator.App.csproj
```

## Backup Profile Smoke

1. Create or select a profile.
2. Set mode to `Backup`.
3. Set source to `%TEMP%\replicator-smoke\source`.
4. Set destination to `%TEMP%\replicator-smoke\target`.
5. Keep `Dry run` checked.
6. Click `Preview Dry Run`.
7. Confirm the output mentions dry run and no files are copied.
8. Uncheck `Dry run`.
9. Click `Run Now`.
10. Confirm `%TEMP%\replicator-smoke\target\note.md` exists.
11. Confirm latest log and run summary update in the UI.

Pass criteria:

- no unhandled UI error
- status changes from busy to completion
- latest log path is populated
- file appears in the target after non-dry-run execution

## Unavailable Path Smoke

1. Change source to an unused drive root such as `Z:\missing-source` if `Z:` is not mounted.
2. Click `Run Now`.
3. Confirm the run is blocked before PowerShell starts.
4. Confirm the availability line names the unavailable drive or path.
5. Restore the valid source path.

Pass criteria:

- no scheduled/manual backup starts for the unavailable source
- output contains the availability report
- UI remains usable after the failure

## Scheduled Task Smoke

1. Use a valid backup profile.
2. Set cadence to `Minutes` and interval to `15`.
3. Click `Install Task`.
4. Confirm the task name starts with `\Replicator\`.
5. Click `Start Scheduled Task`.
6. Wait for polling to complete or click `Refresh Status`.
7. Confirm latest log and run summary update.
8. Click `Disable Task`; confirm `Enable Task` appears.
9. Click `Enable Task`; confirm `Disable Task` appears.
10. Click `Remove Task`; confirm install/update controls return.

Pass criteria:

- task action buttons are conditional to the installed/enabled/disabled state
- minute cadence installs successfully through Task Scheduler
- on-demand scheduled task execution writes a log
- removal leaves no active `\Replicator\` task for the profile

## Shuttle Smoke

Prepare two local source folders to simulate two machines:

```powershell
New-Item -ItemType Directory -Force -Path "$root\home", "$root\work" | Out-Null
Set-Content -LiteralPath "$root\home\note.md" -Value "from home"
Set-Content -LiteralPath "$root\work\note.md" -Value "local work edit"
```

Use one shuttle profile at a time:

1. Set mode to `Shuttle`.
2. Set source to `%TEMP%\replicator-smoke\home`.
3. Set shuttle path to `%TEMP%\replicator-smoke\external\Replicator\shuttle\repo`.
4. Uncheck `Dry run`.
5. Click `Prepare Shuttle`.
6. Confirm the progress bar becomes determinate when file count is known.
7. Click `Depart`.
8. Change source to `%TEMP%\replicator-smoke\work`.
9. Click `Dock Shuttle`.
10. Confirm output shows inbound summary and potential conflict count.
11. Click `Receive Changes`.
12. Confirm `%TEMP%\replicator-smoke\work\note.md` changed to `from home`.
13. Confirm a preserved conflict copy exists under the shuttle `conflicts` folder.

Pass criteria:

- shuttle buttons remain responsive during operations
- progress updates during file work
- receive preserves local conflicting content before overwrite
- manifests and state files are written under the shuttle path

## Shuttle Cancellation Smoke

Use a larger disposable source tree:

```powershell
$large = "$root\large-source"
New-Item -ItemType Directory -Force -Path $large | Out-Null
1..6500 | ForEach-Object {
    Set-Content -LiteralPath (Join-Path $large ("file-{0:D5}.txt" -f $_)) -Value $_
}
```

1. Set mode to `Shuttle`.
2. Set source to `%TEMP%\replicator-smoke\large-source`.
3. Set shuttle path to `%TEMP%\replicator-smoke\external\Replicator\shuttle\large`.
4. Uncheck `Dry run`.
5. Click `Prepare Shuttle`.
6. Confirm `Cancel` appears while the operation runs.
7. Click `Cancel`.
8. Confirm status changes to canceled and the UI becomes usable again.
9. Re-run `Prepare Shuttle` without canceling and confirm it can complete.

Pass criteria:

- cancel button is only visible during cancellable shuttle operations
- cancel request disables the button immediately
- operation ends with a canceled status instead of an unhandled exception
- subsequent shuttle operation can still run

## BitLocker Posture Smoke

Use the same profile with a local drive target or an external BitLocker To Go drive if available.

1. Select a profile.
2. Click `Refresh Status` or save the profile.
3. Confirm the header shows a drive-security line.
4. On an unprotected disposable drive, confirm the line warns that the drive is not BitLocker protected.
5. On a protected unlocked drive, confirm the line reports BitLocker protected.
6. If a locked external drive is available, confirm the line reports locked or unavailable.

Pass criteria:

- drive-security check never blocks the UI permanently
- protected, unprotected, locked, unavailable, or unknown states are visible
- Replicator warns only; it does not enforce blocking policy yet

## Stop Criteria

Stop the smoke pass and open a bug if any of these occur:

- app hangs for more than 30 seconds after canceling a shuttle operation
- backup or shuttle writes to an unexpected path
- scheduled task action buttons contradict the task state
- unavailable source or target starts a copy anyway
- BitLocker command failure crashes the app instead of reporting unknown posture
- conflict receive overwrites local content without preserving a conflict copy

## Evidence To Capture

For a release candidate, capture:

- command output from `.\tools\run-smoke-tests.ps1`
- screenshot of a successful backup run summary
- screenshot of a shuttle progress run
- screenshot of a canceled shuttle operation
- screenshot of BitLocker posture line
- latest log path from `%LOCALAPPDATA%\Replicator\logs`
