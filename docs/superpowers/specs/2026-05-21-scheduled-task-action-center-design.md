# Scheduled Task Action Center Design

## Problem

Replicator now detects when the selected profile's scheduled task needs repair, but the user experience still feels too passive. If Task Scheduler contains stale, orphaned, or corrupted Replicator tasks, the app should notice that on launch and guide the user toward a visible review path. A single selected-profile banner is useful evidence, but it does not answer the larger question: "What scheduled work did Replicator find on this machine, and what should I do about it?"

The desired direction is a hybrid of a launch intake flow and a full task inventory. Replicator should not interrupt startup with a wizard by default, but it should expose a clear Action Center when task review is needed and provide an inventory view for details.

## Goals

- Scan existing Windows Task Scheduler entries under `\Replicator\` when the app launches.
- Show a persistent Action Center when the scan finds task issues.
- Summarize counts for tasks that need repair, orphaned tasks, healthy matched tasks, and tasks whose status cannot be determined.
- Provide a `Review Tasks` action that opens an in-app scheduled-task inventory.
- Reuse the current selected-profile repair path for matched tasks that need repair: save profile, regenerate script, and call `InstallOrUpdateAsync`.
- Keep normal profile editing usable while the Action Center is visible.
- Keep scheduled task discovery read-only until the user clicks an explicit repair action.

## Non-Goals

- Do not block application startup with a modal wizard in the first slice.
- Do not adopt orphaned tasks into profiles in the first slice.
- Do not delete orphaned tasks in the first slice.
- Do not replace Windows Task Scheduler as the execution engine.
- Do not auto-repair tasks without explicit user action.
- Do not scan arbitrary non-Replicator scheduled tasks.

## Current Context

Task creation now uses a hidden, non-interactive PowerShell action. The selected-profile query can detect missing `-WindowStyle Hidden`, missing `-NonInteractive`, mismatched script paths, and missing scripts. The app can display `Needs repair` and changes the install/update button label to `Repair Task`.

That behavior is profile-local. It does not inventory all `\Replicator\` tasks, does not surface orphaned tasks, and does not provide a launch-time "we found scheduled work" status. Existing users can still launch the app and wonder whether the disruptive visible PowerShell window came from an old task, an orphaned task, or the selected profile.

## Proposed UX

### Action Center

On launch, Replicator starts a background task inventory scan after profiles load. If no issues are found, no large banner appears; the app can quietly show a brief success status such as `Scheduled tasks checked.` If issues are found, a compact Action Center appears near the top of the main screen, below the profile header and above the form.

The Action Center copy should be direct and operational:

```text
Scheduled task review needed
2 issues found: 1 needs repair, 1 orphaned task.
```

Primary action:

```text
Review Tasks
```

Contextual quick action for the selected profile:

```text
Repair Task
```

The Action Center should not hide the existing profile form or bottom command bar. The user can continue editing profiles while the panel remains visible.

### Review Tasks Inventory

Clicking `Review Tasks` opens an in-app inventory view. In the first slice this can be a modal WPF dialog or an in-window panel, whichever fits the current WPF layout with less risk. The important product behavior is the inventory content, not the container.

Each row should show:

- task name
- matched profile name, or `No matching profile`
- health state: `Ready`, `Needs repair`, `Orphaned`, `Unknown`, or `Running`
- concise reason text such as `Visible PowerShell action`, `Script path does not match this profile`, or `No profile matches this task name`
- last run and last result when available
- allowed action for the row

First-slice row actions:

- `Repair` for matched unhealthy tasks that are not running.
- `Select Profile` for matched tasks, so the user can inspect the profile in the main screen.
- `Refresh` for the inventory.

Deferred row actions:

- `Adopt` for orphaned tasks.
- `Remove` for orphaned or corrupted tasks.
- `Repair all safe`.

Deferred actions may appear disabled only if the text makes it clear that they are not yet available. It is also acceptable to omit them from the first slice to keep the interface honest.

## Task Classification

The inventory scan should classify each discovered task into one of these states:

- `Ready`: the task matches a known profile and the action inspector reports no repair reasons.
- `NeedsRepair`: the task matches a known profile and the action inspector reports one or more repair reasons.
- `Running`: the task matches a known profile and Task Scheduler reports it is currently running. If it also needs repair, repair actions should be disabled until it stops.
- `Orphaned`: the task is under `\Replicator\` but does not match any known profile task name.
- `Unknown`: the task or folder could not be queried, parsed, or inspected.

Profile matching should be exact by scheduled task name for the first slice. Later adoption work can use script paths or embedded profile metadata to propose matches for orphaned tasks.

## Architecture

Add a new scheduled-task inventory layer in Core:

- `ScheduledTaskInventoryItem`: immutable model for a discovered task row.
- `ScheduledTaskInventorySummary`: counts and helper properties for Action Center visibility.
- `IScheduledTaskInventoryService`: scans Replicator tasks and returns a summary plus items.
- `WindowsScheduledTaskInventoryService`: runs `schtasks.exe`, parses the output, filters `\Replicator\` task names, matches profiles, and reuses `ScheduledTaskActionInspector`.

The WPF app owns presentation state:

- after profiles load, start `RefreshTaskInventoryAsync`
- keep the result in `_taskInventory`
- update Action Center visibility and summary copy
- open the review inventory from `Review Tasks`
- call existing repair flow for matched unhealthy tasks

The inventory service should not mutate profiles or tasks. Repair stays in the existing app flow that already writes the script and updates the scheduled task.

## Data Flow

1. App loads profiles from `JsonProfileStore`.
2. App refreshes the selected profile task status as it does today.
3. App starts the inventory scan.
4. Inventory service queries Windows Task Scheduler for tasks under `\Replicator\`.
5. Inventory service matches task names against loaded profiles using `ScheduledTaskName.ForProfile(profile)`.
6. Inventory service inspects each task action with `ScheduledTaskActionInspector`.
7. App displays the Action Center if the summary has `NeedsRepair`, `Orphaned`, or `Unknown` counts.
8. User clicks `Review Tasks`.
9. App shows the inventory rows.
10. User clicks `Repair` for a matched unhealthy task.
11. App selects or uses the matched profile, writes the generated script, calls `InstallOrUpdateAsync`, then refreshes both the selected-profile status and inventory summary.

## Error Handling

- If Task Scheduler query fails, show an Action Center warning with an `Unknown` count and preserve the raw output in the inventory details.
- If parsing returns no task rows, treat the inventory as clean rather than as a failure.
- If a matched task is running, disable repair and show `Task is running. Refresh after it completes.`
- If repair fails, keep the row visible and show the existing `TaskOperationResult` message and output.
- If the scan takes longer than expected, the main UI remains usable and the Action Center can show `Checking scheduled tasks...`.

## UI Constraints

- Do not add a new landing page or startup modal for this slice.
- Keep the Action Center compact and operational; it is not a marketing hero.
- Avoid duplicating the selected-profile status. The Action Center is about inventory-level discovery, while the header remains about the selected profile.
- The inventory view should support scanning and comparing rows. Use compact row layout, restrained color, and explicit action buttons.

## Testing Strategy

- Unit-test inventory parsing for multiple `schtasks` records under `\Replicator\`.
- Unit-test exact profile matching by scheduled task name.
- Unit-test classification for ready, needs-repair, orphaned, running, and unknown tasks.
- Unit-test inventory summary counts and Action Center visibility rules.
- Unit-test repair action gating for running and orphaned rows where practical.
- Keep existing scheduled task action inspector tests unchanged.
- Add smoke-test documentation for launch inventory scan and Review Tasks behavior.

## Rollout

This should be implemented as the next feature slice after the selected-profile repair banner. Existing users will launch Replicator, see a visible Action Center if old or orphaned tasks exist, and use `Review Tasks` to understand what Replicator found. Matched unhealthy tasks can be repaired immediately; orphan adoption and deletion remain follow-up work.

## Self-Review

- Placeholder scan: no placeholder sections or deferred requirements without explicit scope boundaries.
- Internal consistency: the design consistently uses Action Center as the entry point and inventory as the details view.
- Scope check: first slice is limited to scan, summarize, review, and repair matched unhealthy tasks.
- Ambiguity check: orphan adoption and deletion are explicitly deferred and should not be implemented in this slice.
