# Roadmap And Backlog

## Planned Direction

- Expand graceful unavailable-source/unavailable-target states into BitLocker and drive-identity aware states.
- Expand scheduling beyond manual/daily/weekly/hourly/minute intervals with richer calendar rules if needed.
- Add a tray app or Windows Service that watches volume-arrival events and prompts when a known shuttle drive appears.
- Add drive identity support so profiles match a volume, not just a drive letter.
- Add BitLocker/secure-drive policy enforcement for backup and shuttle targets.
- Add rclone as the default transfer backend for local/S3-compatible targets.
- Add Git shuttle support using external-drive bare Git remotes.
- Add explicit restore and converge workflows.
- Collapse shuttle into the profile model as an additive capability, not an either/or replacement for backup mode.
- Replace the prototype results textbox with durable job history, structured logging, and audit drill-down backed by a lightweight database.

## Near-Term Backlog

- **Graceful unavailable states**: initial backup-profile preflight and scheduled-script failures are implemented. Next expansion is `shuttle drive locked/missing` and secure-drive policy detail.
- **BitLocker posture checks**: first visibility slice implemented. Replicator now checks local Windows profile drive roots and reports protected, unprotected, locked, unavailable, or unknown posture. Remaining work: policy enforcement, unlock guidance, and BitLocker To Go setup flow.
- **Known shuttle drive detection**: add a tray app or Windows Service that watches for volume arrival, recognizes member drives, and prompts `Dock Shuttle` when relevant.
- **Drive identity over drive letters**: bind profiles to volume identity/label/serial metadata so `E:` becoming `F:` does not break profiles.
- **Shuttle protect cadence**: add a dedicated scheduled shuttle-protect runner that writes manifests and respects pending inbound state, rather than reusing the backup robocopy runner.
- **Shuttle as profile capability**: redesign profiles so a profile can run scheduled protect/backup behavior and also expose shuttle actions. The current `Backup` versus `Shuttle` split is a prototype simplification.
- **Large shuttle performance**: first slices implemented. Shuttle file work now runs off the WPF UI thread with throttled file-count progress, stream hashing, timestamp-preserving payload copies, manifest file indexes, metadata fast-skip checks, manifest-hash fallback checks, and user-initiated cancellation. Remaining work: resumability and avoiding long operation output in a single text box.
- **Job history and audit UI**: replace the results textbox with a tabular jobs view backed by SQLite or another lightweight local database. Every backup/shuttle/restore run should create an auditable job record with stats, logs, artifacts, and drill-down detail.
- **Path drift compensation**: match shuttle pairs even when the local repo/folder moved or has a different intermediate path, using explicit pair ids, Git history/ref fingerprints, and content similarity.
- **Git shuttle engine**: support external-drive bare Git remotes for sensitive repos so committed work can move through Git's conflict model without network remotes.
- **rclone engine**: add rclone-backed `copy`, `sync`, and later `bisync` adapters for local, S3-compatible, and broader storage targets.
- **Restore/converge workflows**: keep backup namespaces authoritative and build explicit preview-first restore/converge flows from those backups.
- **Conflict review UI**: replace the current conflict-preserve behavior with a reviewer surface for incoming, local, and preserved copies.

## Backlog Scoring

Scored on May 19, 2026 for the next single-feature implementation pass.

Weights:

- User value: 35%
- Risk reduction: 25%
- Dependency unlock: 20%
- Effort fit: 20%

| Candidate | User value | Risk reduction | Dependency unlock | Effort fit | Weighted score |
| --- | ---: | ---: | ---: | ---: | ---: |
| Graceful unavailable states | 5 | 5 | 4 | 5 | 4.8 |
| Large shuttle performance | 5 | 4 | 4 | 2 | 3.95 |
| Job history and audit UI | 5 | 4 | 4 | 2 | 3.95 |
| Shuttle as profile capability | 5 | 4 | 5 | 1 | 3.9 |
| BitLocker posture checks | 4 | 5 | 4 | 3 | 4.0 |
| Known shuttle drive detection | 4 | 3 | 4 | 2 | 3.3 |
| Drive identity over drive letters | 4 | 3 | 4 | 3 | 3.5 |
| Path drift compensation | 3 | 3 | 3 | 2 | 2.8 |
| Git shuttle engine | 4 | 3 | 4 | 1 | 3.2 |
| rclone engine | 4 | 3 | 4 | 1 | 3.2 |

Winner for first pass: graceful unavailable states.

Second-pass winner: large shuttle performance first slice.

Third-pass winner: BitLocker posture visibility.

Fourth-pass winner: shuttle operation cancellation.

Fifth-pass winner: minute-based scheduled tasks.

## WinUI 3 Path

The current UI is WPF to keep the initial app simple and compatible with the local Windows scripting/task-scheduler workflow.

A future WinUI 3 refactor should:

- Keep backup profiles, script generation, scheduling, shuttle manifests, and log parsing in `Replicator.Core`.
- Introduce view models that expose commands and observable state without referencing WPF types.
- Create a new WinUI 3 presentation project that binds to those view models.
- Replace the WPF theme dictionary with native WinUI 3 resources, AppWindow behavior, InfoBars, ProgressRing/ProgressBar, TeachingTips, and Fluent command surfaces.
- Retire the WPF project after parity for profile editing, run status, logs, scheduled task management, and shuttle workflows.
