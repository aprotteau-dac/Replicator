# Job History And Audit

## Implemented Storage Foundation

Replicator keeps a local SQLite audit ledger at `%LOCALAPPDATA%\Replicator\replicator.db`.

The Core-owned store records app-launched backups, dry-run previews, and prepare/depart/dock/receive shuttle operations. Jobs include a profile snapshot, operation, outcome, UTC times, elapsed time, counters, bounded summaries/errors, and links to log, script, or manifest artifacts. User cancellation is recorded as `canceled`. Each app backup receives a unique log filename.

`profiles.json` remains the profile registry. Deleting a profile clears the current profile reference in its jobs, preserving snapshots and evidence. Renaming a profile does not rewrite historical names or paths.

The schema has `schema_metadata`, `jobs`, `job_events`, `job_artifacts`, `system_events`, and `import_sources`. Schema version 1 initializes transactionally and can be reopened repeatedly. A newer unsupported schema is left untouched; normal operations continue through the fallback path.

System events cover startup, storage initialization/migration, profile save/delete, script generation, scheduled task actions and repairs, BitLocker check failures, and import batches. Events carry optional job, profile, artifact, and batch correlation fields.

## Historical And Scheduled Runs

After profiles load, and on explicit status refresh, a background importer scans the logs directory. It streams file hashes and log parsing, commits batches of up to 32 artifacts, and summarizes duplicate skips per batch. Import does not hold a database transaction while reading files or block the UI thread.

- Timestamped Replicator logs are primary historical evidence. Headers supply historical profile details; the stable ID suffix can match a renamed profile.
- A matching `*-latest.json` enriches only the job for its exact log path. It cannot reconstruct overwritten history or create jobs by itself.
- Canonical path, size, last-write UTC, and SHA-256 fingerprints make repeated scans idempotent. Changed files are processed again, updating their existing job.
- Malformed, locked, and unreadable artifacts are skipped independently and retried on a later scan. A database write failure rolls back its batch, leaving artifacts eligible for retry.
- Explicit outcomes are retained; incomplete logs remain `unknown`, and summary-only completion remains `completed_unverified`. Unknown timestamps and counters stay null/absent.
- App jobs reserve their log path before launch, so importing their logs does not duplicate those jobs or override their direct outcomes.

Generated Task Scheduler scripts continue writing logs and status JSON. They do not load SQLite. Scheduled runs appear in the ledger when the app next imports their artifacts.

`process_exit_code` is the app-launched process result. `robocopy_exit_code` comes from an explicit robocopy completion/failure message. `reported_exit_code` comes from status JSON and can represent a script preflight failure instead of robocopy. These are kept separate.

## Failure Handling And Data

Storage is best effort. Backups, previews, shuttle operations, scheduling, and the current latest-log display continue when SQLite is unavailable. Storage failures and the original system event are written to `%LOCALAPPDATA%\Replicator\logs\system-events-fallback.log`.

The diagnostic file rotates before exceeding 1 MB, keeping one previous `system-events-fallback.1.log`. Failure of this sink also cannot block an operation. Recovery is logged when storage becomes available; fallback events are not replayed into SQLite.

The database contains local metadata, including profile names and filesystem paths. It does not store full verbose log bodies or shuttle manifest file lists. Full logs and manifests remain linked file artifacts. Removing an artifact does not erase its job or last-observed metadata. Script and latest-status paths are mutable artifacts; snapshots and stored outcomes preserve the run's context.

Default uninstall preserves application data. `uninstall-replicator.ps1 -RemoveAppData` removes the entire local Replicator data directory, including the database and fallback diagnostics. Close Replicator before copying or removing its database.

## Follow-Up UI Work

The current output textbox and latest-run summary remain in place. This slice does not add a job-history screen.

Remaining work:

- A jobs table and detail pane, with filters by profile, operation, status, and date.
- Export of selected job evidence and richer audit drill-down.
- Restore workflows and additional operation types as those features arrive.
- Optional direct PowerShell database writes, service/tray logging, and fallback replay.

See the [storage design](superpowers/specs/2026-06-10-job-history-audit-storage-design.md) for the agreed scope.
