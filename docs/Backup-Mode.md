# Backup Mode

Backup mode is the scheduled local protection workflow.

## Setup

1. Select `Backup` as the profile mode.
2. Set `Source` to the local folder to protect.
3. Set `Destination` to the local backup path.
4. Keep `Dry run` enabled until the generated script and preview look correct.
5. Use `Install Task` to register the profile in Windows Task Scheduler.
6. Use `Run Now` for direct execution from the app.

## Execution

Generated scripts use `robocopy` and write logs under:

```text
%LOCALAPPDATA%\Replicator\logs
```

Robocopy exit codes `0` through `7` are treated as successful.

The app preflights source and destination availability before manual `Run Now` and `Preview Dry Run` operations. If the source drive, source path, or destination drive is unavailable, the run is stopped before launching PowerShell and the status banner names the unavailable path.

Scheduled task scripts run through hidden, non-interactive PowerShell actions. `Refresh Status` inspects the selected profile's Task Scheduler action and shows `Repair Task` if an older action is visible, missing `-NonInteractive`, points at the wrong script, or points at a missing script. Repair rewrites the generated script and Task Scheduler action for that profile.

Scheduled scripts also check the source and target roots before copying. If an external drive is unplugged or a source path is missing, the task fails cleanly, writes the error to the run log/status file, and retries naturally on the next scheduled cadence. If a dry run sees that the target path does not exist, it writes a clear latest status saying the real run would create the path and exits before robocopy.

Each run writes a timestamped robocopy log plus a per-profile `*-latest.json` status file under `%LOCALAPPDATA%\Replicator\logs`. The app shows the latest status first, then falls back to robocopy log parsing when no status JSON is available.

Supported task cadences are manual, minutes, hourly, daily, and weekly. Minute schedules use Windows Task Scheduler's minute cadence with the configured minute interval.

## Deletes

By default, backup mode uses additive copy behavior. `Mirror deletes` switches to robocopy `/MIR`, which can remove destination files. Use it only when the destination should exactly mirror the source.

## Known Gaps

- Unavailable shuttle-drive states need more detail, especially locked/encrypted drive handling.
- rclone is planned as a richer backend for local, S3-compatible, and broader targets.
