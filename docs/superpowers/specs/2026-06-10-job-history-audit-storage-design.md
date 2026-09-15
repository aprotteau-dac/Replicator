# Job History Audit Storage Design

## Context

Replicator currently writes timestamped robocopy logs and per-profile `*-latest.json` status files under `%LOCALAPPDATA%\Replicator\logs`. The WinUI shell still exposes this evidence through a prototype output text box and latest-run summary fields. The roadmap calls for durable job history, structured logging, and audit drill-down backed by a lightweight local database.

This slice pulls the storage and logging foundation forward before the full job-history UI. The future front end and design-system modernization will decide how job history is presented.

## Goals

- Add a Core-owned SQLite database at `%LOCALAPPDATA%\Replicator\replicator.db`.
- Build a durable job ledger for app-launched runs without making the database a hard runtime dependency.
- Backfill existing historical evidence from log and status files into the database.
- Add app-wide system event logging separate from per-job events.
- Preserve orphaned history for deleted or renamed profiles.
- Keep verbose logs as file artifacts linked from database records.
- Keep the current output text box and latest-log behavior available during this infrastructure slice.

## Non-Goals

- Build the full job-history table, filters, export, or audit drill-down UI.
- Move profile storage from `profiles.json` into SQLite.
- Make generated scheduled-task PowerShell scripts write directly to SQLite.
- Add tray, service, or background watcher logging.
- Store full verbose robocopy logs inside SQLite.
- Replay every fallback diagnostic event into SQLite after recovery.

## Storage Model

V1 adds a Core-owned SQLite store at `%LOCALAPPDATA%\Replicator\replicator.db`. The database contains schema metadata, `jobs`, `job_events`, `job_artifacts`, `system_events`, and `import_sources`.

The database is not the profile registry in v1. `profiles.json` remains authoritative. Job rows store a nullable current profile ID plus a profile snapshot containing the profile name, mode, source path, target or shuttle path, and operation-relevant settings. This keeps history understandable when profiles are deleted, renamed, or edited after a run.

Full verbose logs remain in log files. The database stores structured fields, counters, bounded summaries, error text, artifact paths, artifact last-seen metadata, and provenance. Job records should remain useful even when a linked log file is later removed.

All persisted timestamps are UTC. Presentation code can convert to local time at the UI boundary.

## Import And Backfill

On database creation, startup, and explicit status refresh, Replicator runs a best-effort importer over `%LOCALAPPDATA%\Replicator\logs`. Timestamped `*.log` files are the primary historical evidence. Matching `*-latest.json` files enrich the latest imported job for a profile slug, but they are not treated as complete history because they are overwritten per profile.

The importer runs asynchronously after the shell opens and writes in batches. It must not block app startup or profile operations. A large log directory should produce import progress and batch summary system events, not a frozen UI.

Each readable artifact selected for import is tracked in `import_sources` using canonical path, file size, last-write UTC, and a streaming SHA-256 content hash. This makes repeated startup and refresh imports idempotent. Duplicate skips are summarized at the batch level instead of emitting one system event per duplicate artifact.

Malformed, unreadable, locked, or unparseable artifacts are skipped independently. The importer records per-artifact system events for parse failures and unusual conditions, then continues. If a database write fails during a batch, that batch stops and unimported artifacts remain eligible for the next startup or refresh.

Historical imports preserve uncertainty. If an old log has clear robocopy summary evidence, the importer captures counters and infers status only where safe. If success or failure cannot be proven, the job status is `unknown` or `completed_unverified`, with parser confidence and provenance recorded. Evidence from deleted profiles is imported as orphaned job history with the best available profile snapshot.

## Job Capture

For app-launched operations, Replicator attempts to create a job row before work starts. This applies to `Run Now`, `Preview Dry Run`, and shuttle operations. If job registration succeeds, the app updates and completes that job through the run. If job registration fails, the operation still proceeds through the current file and log path.

Backup and dry-run jobs capture operation type, status, profile snapshot, source path, destination path, start and completion UTC timestamps, elapsed time, process exit code, generated script path, log path, parsed robocopy counters when available, and a bounded error summary.

Shuttle jobs capture operation type, status, profile snapshot, source path, shuttle path, start and completion UTC timestamps, manifest path when available, manifest counters, conflicts, warnings, and bounded details. User-canceled operations complete as `canceled`, not `failed`.

Scheduled and background Task Scheduler runs remain indirect in v1. Generated PowerShell scripts continue writing status JSON and log files. The importer registers those runs retroactively from artifacts. This avoids SQLite dependency and locking concerns inside standalone scheduled-task PowerShell.

## System Event Logging

System events describe Replicator itself rather than a specific job. V1 records startup, database initialization, migration, import batch start and completion, import failures, storage failures, profile save and delete, script generation, scheduled task install/update/repair/remove/start attempts, BitLocker check failures, and fallback logging activation.

Each system event includes UTC timestamp, severity, category, stable code, short message, optional details, and optional correlation fields: job ID, profile ID, artifact ID, and import batch ID where relevant.

System event APIs must be safe to call from normal app paths. A failure to record a system event must not throw into backup, shuttle, scheduling, profile, or status-refresh flows.

## Failure Handling And Fallback

The database is the preferred job ledger for app-launched operations, but it is not a hard runtime dependency. Backup, dry-run, shuttle, scheduling, status refresh, and current log/status display continue using today's file-based behavior when storage is unavailable.

Storage failures are reported through a bounded file-backed diagnostic sink under `%LOCALAPPDATA%\Replicator\logs\system-events-fallback.log`. When the file reaches 1 MB, Replicator rotates the current file to `system-events-fallback.1.log` and starts a new one, keeping one previous fallback file. Once database storage becomes available again, the app may record a recovery event. V1 does not need to replay every fallback event into SQLite.

Importer failures are scoped to the current artifact or batch. A broken historical file should never make Replicator unusable. Locked active logs can be skipped and retried on the next startup or refresh.

## Testing Scope

Tests should follow the existing custom C# runner in `tests/Replicator.Tests/Program.cs`.

Coverage should include:

- schema initialization and idempotent migration behavior
- idempotent import dedupe through `import_sources`
- historical timestamped log import
- `*-latest.json` enrichment of the matching latest imported job
- orphaned profile history import
- `unknown` or `completed_unverified` status for uncertain logs
- linked artifact metadata without full log body storage
- file-backed fallback diagnostic behavior
- app-launched job creation and completion
- canceled job status for canceled shuttle work
- system event correlation fields
- view-model proof that job capture failures do not block an app-launched run

## Documentation Scope

Update the job history, roadmap, smoke-test, and uninstall/data-removal docs. The docs should explain that `replicator.db` stores local audit metadata, including profile names and filesystem paths, but not full verbose log contents. Full logs remain linked file artifacts under the existing logs directory.

## Follow-Up Work

Later slices can add the full job-history UI, filters, export, profile storage migration, PowerShell direct DB writes if still desirable, service/tray logging, fallback replay, and richer audit drill-down.
