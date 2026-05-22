# Replicator

Replicator is a Windows backup and shuttle-control app for people who maintain sensitive local workstreams that should not be pushed to cloud sync or ordinary Git remotes.

It is a .NET 8 WPF desktop app that manages local backup profiles, generates auditable PowerShell scripts, installs scheduled tasks, and supports an early external-drive shuttle workflow for moving paired repos/folders between trusted machines.

> Status: early prototype. Backup mode is usable for local testing with availability preflight, generated scripts, Task Scheduler integration, and minute/hourly/daily/weekly cadences. Shuttle mode is a first vertical slice with progress, cancellation, and conflict-preserving receive behavior, but still needs review, restore tooling, resumability, and conflict UX.

## Why

Replicator targets a niche that cloud sync tools do not handle cleanly: sensitive active workstreams, scratch repos, local evidence folders, LLM/dev environments, and private repos that need continuity across machines without using OneDrive, Dropbox, GitHub, or another network remote.

The app separates several concepts that are often conflated:

- `Backup`: protect local state on a cadence.
- `Shuttle`: controlled handoff through an external drive.
- `Restore/converge`: future explicit recovery workflow, not silent sync.
- `Git shuttle`: future support for external-drive bare Git remotes.

## Current Capabilities

- Windows-only WPF UI with the Replicator Industrial Red brand theme.
- Local source-to-local destination backup profiles.
- Controlled shuttle profiles for external-drive handoff between trusted machines.
- Generated PowerShell scripts under `%LOCALAPPDATA%\Replicator\scripts`.
- Robocopy logs under `%LOCALAPPDATA%\Replicator\logs`.
- Profile metadata in `%LOCALAPPDATA%\Replicator\profiles.json`.
- Machine identity in `%LOCALAPPDATA%\Replicator\machine-id.txt`.
- Windows Task Scheduler install, run, enable, disable, query, and removal under `\Replicator\`.
- Manual, minute-based, hourly, daily, and weekly schedule cadences.
- Source/target/shuttle availability preflight for manual runs, shuttle operations, and generated scheduled scripts.
- Shuttle operations run off the UI thread with file-count progress and cancellation.
- BitLocker posture visibility for local Windows drive roots, cached by drive root across profile switching.
- Repeatable smoke-test gate and manual smoke plan.
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

## Documentation

Project docs live in [docs/Home.md](docs/Home.md). They mirror the intended GitHub wiki structure and cover concepts, backup mode, shuttle mode, drive security, packaging, and the roadmap.

For a repeatable pre-release check, use [docs/Smoke-Test-Plan.md](docs/Smoke-Test-Plan.md) or run:

```powershell
.\tools\run-smoke-tests.ps1
```

## Package And Install

Install directly from a repo checkout:

```powershell
.\tools\install-dev.ps1
```

That command rebuilds the local package, then installs Replicator per-user.

To intentionally reuse an existing package:

```powershell
.\tools\install-dev.ps1 -SkipPublish
```

For a file-only install without shortcuts:

```powershell
.\tools\install-dev.ps1 -NoShortcuts
```

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

To skip all shortcuts:

```powershell
.\install-replicator.ps1 -NoShortcuts
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

## Drive Security

Replicator is intended for sensitive local workstreams, so external-drive security should become a first-class feature. The likely implementation path is a BitLocker wrapper rather than custom encryption.

Implemented BitLocker visibility:

- Detect whether a target or shuttle drive is BitLocker-protected.
- Show drive lock/unlock/protection state in the profile UI.
- Cache drive posture by local drive root for the current app session.
- Offer a narrow `Check All as Admin` verification action for permission-required BitLocker status checks.

Planned BitLocker enforcement:

- Replace the current elevated PowerShell verification helper with a branded Replicator-owned elevated helper executable.
- Block `Prepare Shuttle`, `Receive Changes`, and scheduled backup writes when a required protected drive is locked or unprotected.
- Offer guided setup for external drives using BitLocker To Go.
- Store recovery-key reminders and policy metadata, but never store recovery keys in Replicator profile JSON.
- Prefer Windows-native unlock flows and Credential Manager/DPAPI for any local secrets that are truly required.

For now, use BitLocker manually on external shuttle/backup drives before trusting them with sensitive content.

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

Long term, shuttle should be an additive capability on a protected profile, not an either/or replacement for backup mode. The current prototype separates `Backup` and `Shuttle` modes to prove the workflow, but the intended model is a profile that can protect on cadence and also perform shuttle handoff.

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

Field note: shuttling about 6,500 files worked, but caused major UI lockup in the first prototype. Shuttle operations now run off the WPF UI thread, report throttled file-count progress, can be canceled from the UI, preserve payload timestamps, prune excluded directories before recursion, and write per-file manifest entries with SHA-256 hashes. First-time staging hashes while copying so new source files are not read twice. Later prepares can reuse unchanged manifest hashes, and dock/receive can classify drifted-timestamp files from the manifest hash without re-reading the shuttle payload file. Current long-smoke timing on a generated 6,500-file tree: first prepare about 8-10s, skipped prepare under 1s, dock analysis about 2s.

## Known Limitations

- S3-compatible targets are not implemented yet.
- rclone, Kopia, restic, and Git shuttle engines are future adapters.
- Automatic external-drive detection is not implemented yet.
- BitLocker posture visibility is implemented for local Windows drive roots, including session cache and batch elevated verification. Secure-drive policy enforcement and a branded elevated helper are not implemented yet.
- Shuttle conflict handling currently preserves overwritten local files, but does not yet provide a full merge UI.
- Restore/converge workflows are not implemented yet.
- Hyper-V/VM backup scenarios need special handling and should not be treated as ordinary live folder copies.
- Shuttle currently behaves like a separate mode instead of an additive capability layered on top of scheduled protect/backup behavior.

## Planned Direction

- Expand graceful unavailable-source/unavailable-target states into BitLocker and drive-identity aware states.
- Expand scheduling beyond manual/minute/hourly/daily/weekly with richer calendar rules if needed.
- Add a tray app or Windows Service that watches volume-arrival events and prompts when a known shuttle drive appears.
- Add drive identity support so profiles match a volume, not just a drive letter.
- Add BitLocker/secure-drive policy enforcement for backup and shuttle targets.
- Add rclone as the default transfer backend for local/S3-compatible targets.
- Add Git shuttle support using external-drive bare Git remotes.
- Add explicit restore and converge workflows.
- Collapse shuttle into the profile model as an additive capability, not an either/or replacement for backup mode.
- Replace the prototype results textbox with durable job history, structured logging, and audit drill-down backed by a lightweight database.

## Backlog

Near-term backlog items:

- **Graceful unavailable states**: backup-profile preflight, scheduled-script failures, and shuttle operation availability blocks are implemented. Next expansion is secure-drive policy detail and BitLocker enforcement.
- **BitLocker posture checks**: visibility and verification slices implemented. Replicator now checks local Windows profile drive roots, caches results by drive root, runs one elevated `Check All as Admin` pass across all profile-attached local roots when needed, and reports protected, unprotected, locked, unavailable, permission-required, or unknown posture. Remaining work: branded elevated helper, policy enforcement, unlock guidance, and BitLocker To Go setup flow.
- **Known shuttle drive detection**: add a tray app or Windows Service that watches for volume arrival, recognizes member drives, and prompts `Dock Shuttle` when relevant.
- **Drive identity over drive letters**: bind profiles to volume identity/label/serial metadata so `E:` becoming `F:` does not break profiles.
- **Shuttle protect cadence**: add a dedicated scheduled shuttle-protect runner that writes manifests and respects pending inbound state, rather than reusing the backup robocopy runner.
- **Shuttle as profile capability**: redesign profiles so a profile can run scheduled protect/backup behavior and also expose shuttle actions. The current `Backup` versus `Shuttle` split is a prototype simplification.
- **Large shuttle performance**: first slices implemented. Shuttle file work now runs off the WPF UI thread with throttled file-count progress, stream hashing, hash-while-copy staging, timestamp-preserving payload copies, manifest file indexes, metadata fast-skip checks, manifest-hash fallback checks, and user-initiated cancellation. Remaining work: resumability and avoiding long operation output in a single text box.
- **Job history and audit UI**: replace the results textbox with a tabular jobs view backed by SQLite or another lightweight local database. Every backup/shuttle/restore run should create an auditable job record with stats, logs, artifacts, and drill-down detail.
- **Path drift compensation**: match shuttle pairs even when the local repo/folder moved or has a different intermediate path, using explicit pair ids, Git history/ref fingerprints, and content similarity.
- **Git shuttle engine**: support external-drive bare Git remotes for sensitive repos so committed work can move through Git’s conflict model without network remotes.
- **rclone engine**: add rclone-backed `copy`, `sync`, and later `bisync` adapters for local, S3-compatible, and broader storage targets.
- **Restore/converge workflows**: keep backup namespaces authoritative and build explicit preview-first restore/converge flows from those backups.
- **Conflict review UI**: replace the current conflict-preserve behavior with a reviewer surface for incoming, local, and preserved copies.

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
