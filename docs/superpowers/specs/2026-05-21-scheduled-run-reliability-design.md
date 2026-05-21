# Scheduled Run Reliability Design

## Problem

Replicator scheduled backup runs can still feel unsafe and opaque when an existing Windows Task Scheduler entry launches a visible PowerShell/robocopy window, points at stale script paths, or runs while the target drive is unavailable. The user experience looks disruptive and suspicious, and the app does not yet surface enough run-status evidence to diagnose what happened after a scheduled run.

This design targets the insertion point where Replicator owns a scheduled task definition and generated PowerShell backup script. It does not introduce a resident tray process or Windows Service.

## Goals

- Detect stale or unhealthy `\Replicator\` scheduled task actions created by older Replicator versions.
- Repair stale task actions by regenerating the script and updating the task action.
- Keep scheduled runs headless: no visible PowerShell or robocopy windows.
- Make generated scripts fail clearly before robocopy when source or target roots/paths are unavailable.
- Read the existing per-profile latest status JSON and show the most recent run result in the app.

## Non-Goals

- Do not replace Windows Task Scheduler as the runner.
- Do not add a tray application, Windows Service, or always-on background process.
- Do not build a full job-history database in this slice.
- Do not auto-delete orphaned tasks without an explicit later reconciliation UI.

## Current Evidence

New task creation already writes an action containing `-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File`, but pre-existing scheduled tasks can retain older visible actions. The app currently queries only the selected profile task and displays Task Scheduler state, not task action health. Generated scripts write a timestamped log plus a `*-latest.json` status file, but the UI only reads the robocopy log summary.

## Proposed Approach

### 1. Task Action Health

Extend `ScheduledTaskSnapshot` with task action details parsed from `schtasks /Query /FO LIST /V`:

- `TaskToRun`
- `NeedsRepair`
- `RepairReasons`
- `ScriptPath`
- `ScriptExists`

The health check should classify the selected profile task as needing repair when:

- the action does not include `-WindowStyle Hidden`
- the action does not include `-NonInteractive`
- the action does not include `-File`
- the action points to a missing script path
- the action points to a script path that does not match the expected generated script path for the selected profile

The UI should surface this as `Needs repair` and keep `Update Task` available. The repair path is the existing install/update flow: save profile, regenerate script, and call `InstallOrUpdateAsync`.

### 2. Script Preflight And Status Logging

Keep generated scripts as the scheduled-run execution unit, but improve their preflight and status output:

- create the log directory and initial log before checking source/target
- write `Started` status immediately
- check source root, source path, target root, and target path before robocopy
- when the target root is missing, write a clear failure message to both log and latest status JSON
- when the target directory is missing and the run is a dry run, report that it would be created instead of silently letting robocopy behavior confuse the result
- when the target directory is missing and the run is a real copy, create it before robocopy
- keep `Dry run - no files will be copied` in both log header and latest status

This keeps unavailable-target failures diagnosable even when the task runs unattended.

### 3. Latest Run Status Reader

Add a small reader for the generated `*-latest.json` status file:

- locate by `PowerShellScriptGenerator.ProfileSlug(profile)` under `%LOCALAPPDATA%\Replicator\logs`
- parse profile name, mode, source, destination, log path, timestamps, exit code, success flag, and message
- return `null` if the status file is missing or malformed

The selected-profile UI should show latest status evidence alongside the existing latest-log summary:

- successful run: message and exit code
- failed run: failure message and exit code
- dry run: mode clearly says no files were copied
- stale/missing status: keep current log summary fallback

## UX Behavior

When a selected profile has a scheduled task with a stale visible action, the header should not merely show `Ready`. It should show that the task needs repair, and the output/status area should explain why. The primary action remains `Update Task`, because that is already the safe way to regenerate the script and task definition.

When a scheduled task runs and the target drive is missing, no PowerShell or robocopy window should appear. The app should later show a failed latest run with a message such as `Target drive is unavailable: H:\` and a path to the log/status evidence.

## Testing Strategy

- Unit-test task query parsing for `Task To Run` and repair detection.
- Unit-test stale visible action detection: missing `-WindowStyle Hidden` and `-NonInteractive`.
- Unit-test missing or mismatched script path detection.
- Unit-test generated script content for target preflight and latest status messages.
- Unit-test latest status JSON parsing.
- Keep the existing smoke gate as the final verification command.

## Rollout Notes

After this is implemented, existing users with stale scheduled tasks should click `Refresh Status`, see a repair warning, and click `Update Task`. A later intake/reconciliation flow can scan all `\Replicator\` tasks and list orphaned tasks, but this slice fixes the selected-profile path first because that is where the visible scheduled-run window is being encountered.
