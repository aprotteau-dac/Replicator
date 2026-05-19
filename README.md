# Replicator

Replicator is a Windows backup and shuttle-control app for people who maintain sensitive local workstreams that should not be pushed to cloud sync or ordinary Git remotes.

It is a .NET 8 WPF desktop app that manages local backup profiles, generates auditable PowerShell scripts, installs scheduled tasks, and supports an early external-drive shuttle workflow for moving paired repos/folders between trusted machines.

> Status: early prototype. The backup path is usable for local testing; shuttle mode is a first vertical slice and should be treated carefully until it has more review, restore tooling, and conflict UX.

## Why

Replicator targets a niche that cloud sync tools do not handle cleanly: sensitive active workstreams, scratch repos, local evidence folders, LLM/dev environments, and private repos that need continuity across machines without using OneDrive, Dropbox, GitHub, or another network remote.

The app separates several concepts that are often conflated:

- `Backup`: protect local state on a cadence.
- `Shuttle`: controlled handoff through an external drive.
- `Restore/converge`: future explicit recovery workflow, not silent sync.
- `Git shuttle`: future support for external-drive bare Git remotes.

## Current Capabilities

- Windows-only WPF UI with a Solarized dark, Fluent-inspired theme.
- Local source-to-local destination backup profiles.
- Controlled shuttle profiles for external-drive handoff between trusted machines.
- Generated PowerShell scripts under `%LOCALAPPDATA%\Replicator\scripts`.
- Robocopy logs under `%LOCALAPPDATA%\Replicator\logs`.
- Profile metadata in `%LOCALAPPDATA%\Replicator\profiles.json`.
- Machine identity in `%LOCALAPPDATA%\Replicator\machine-id.txt`.
- Windows Task Scheduler install, run, enable, disable, query, and removal under `\Replicator\`.
- Dry-run defaults for new profiles.

## Requirements

- Windows
- .NET 8 SDK
- PowerShell
- `robocopy` for the current native backup engine

## Run

```powershell
dotnet run --project src/Replicator.App/Replicator.App.csproj
```

## Test

```powershell
dotnet run --project tests/Replicator.Tests/Replicator.Tests.csproj
```

## Package And Install

Create a self-contained Windows x64 package:

```powershell
.\tools\publish-release.ps1 -Version 0.1.0
```

The script writes:

```text
artifacts\publish\Replicator-0.1.0-win-x64\
artifacts\package\Replicator-0.1.0-win-x64\
artifacts\package\Replicator-0.1.0-win-x64.zip
```

Install from the unpacked package:

```powershell
.\install-replicator.ps1
```

By default this installs per-user to:

```text
%LOCALAPPDATA%\Programs\Replicator
```

It also creates Start Menu and Desktop shortcuts. To skip the desktop shortcut:

```powershell
.\install-replicator.ps1 -NoDesktopShortcut
```

Uninstall:

```powershell
.\uninstall-replicator.ps1
```

To also remove Replicator profile/script/log data:

```powershell
.\uninstall-replicator.ps1 -RemoveAppData
```

This is a lightweight installer path, not an MSI/MSIX yet. A future release can add WiX, MSIX, or Inno Setup once the app shape settles.

## Backup Mode

Backup mode is the straightforward scheduled backup path.

- Select `Backup` as the profile mode.
- Set `Source` to the local folder to protect.
- Set `Destination` to the local backup path.
- Keep `Dry run` enabled until the generated script and preview look right.
- Use `Install Task` to register the profile in Windows Task Scheduler.
- Use `Run Now` for direct execution from the app.

Generated scripts use `robocopy`. Exit codes `0` through `7` are treated as successful. `Mirror deletes` switches from additive copy behavior to robocopy `/MIR`; use it carefully.

## Shuttle Mode

Shuttle mode is for bidirectional, human-controlled handoff through an external drive. It is not silent cloud sync.

Example:

```text
Machine A local source
  F:\work\client-a-investigation

External shuttle pair root
  X:\Replicator\shuttle\client-a-investigation

Machine B local source
  F:\work\client-a-investigation
```

Set the profile mode to `Shuttle`, use `Source` as the local paired repo/folder, and use `Shuttle path` as the external drive pair root.

Replicator manages this structure under the shuttle pair root:

```text
payload\
manifests\
state\
conflicts\
```

Workflow:

- `Prepare Shuttle`: stages local changes into the shuttle payload and writes a prepare manifest. If `Dry run` is checked, it previews only.
- `Depart`: marks the staged payload ready for another machine to dock.
- `Dock Shuttle`: scans an inbound departed payload and summarizes new, changed, and conflicting files.
- `Receive Changes`: applies inbound files and preserves overwritten local files under `conflicts\`.

The intended rhythm is:

```text
Work machine:
  Protect on cadence
  Prepare Shuttle
  Depart

Home/travel machine:
  Dock Shuttle
  Receive Changes
  Protect on cadence
  Prepare Shuttle
  Depart
```

Current shuttle actions are manual. Scheduled `Protect` for shuttle pairs should be implemented as a dedicated shuttle task runner rather than reusing the backup robocopy script, because shuttle protect needs manifests and pending-inbound guards.

## Known Limitations

- S3-compatible targets are not implemented yet.
- rclone, Kopia, restic, and Git shuttle engines are future adapters.
- Automatic external-drive detection is not implemented yet.
- Shuttle conflict handling currently preserves overwritten local files, but does not yet provide a full merge UI.
- Restore/converge workflows are not implemented yet.
- Hyper-V/VM backup scenarios need special handling and should not be treated as ordinary live folder copies.

## Planned Direction

- Add graceful unavailable-source/unavailable-target states.
- Add minute-based schedules, not only hourly/daily/weekly.
- Add a tray app or Windows Service that watches volume-arrival events and prompts when a known shuttle drive appears.
- Add drive identity support so profiles match a volume, not just a drive letter.
- Add rclone as the default transfer backend for local/S3-compatible targets.
- Add Git shuttle support using external-drive bare Git remotes.
- Add explicit restore and converge workflows.

## WinUI 3 Path

The current UI is WPF to keep the initial app simple and compatible with the local Windows scripting/task-scheduler workflow.

A future WinUI 3 refactor should:

- Keep backup profiles, script generation, scheduling, shuttle manifests, and log parsing in `Replicator.Core`.
- Introduce view models that expose commands and observable state without referencing WPF types.
- Create a new WinUI 3 presentation project that binds to those view models.
- Replace the WPF theme dictionary with native WinUI 3 resources, AppWindow behavior, InfoBars, ProgressRing/ProgressBar, TeachingTips, and Fluent command surfaces.
- Retire the WPF project after parity for profile editing, run status, logs, scheduled task management, and shuttle workflows.

## License

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE).
