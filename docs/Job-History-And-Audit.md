# Job History And Audit

The current results window is a prototype text output surface. It should become a tabular job history and audit view.

## Target Experience

Every run should create a durable job record that can be reviewed later.

The UI should show a table with:

- job id
- profile name
- profile mode/capability
- operation type: backup, dry run, prepare shuttle, depart, dock, receive, restore
- status: queued, running, succeeded, failed, canceled, skipped
- source path
- target/shuttle path
- started time
- completed time
- elapsed time
- files scanned
- files copied
- files skipped
- files changed
- conflicts
- bytes copied
- error summary

Clicking a row should open a detail view with:

- full log tail
- manifest path
- generated script path, when applicable
- task scheduler result, when applicable
- source/target availability checks
- conflict files and preserved-copy locations
- raw engine output

## Storage

Use a lightweight local database rather than only JSON/log files.

Candidate: SQLite.

Reasons:

- single local file
- easy backup
- enough query power for history, filtering, and audit
- works well with WPF/.NET
- avoids inventing an ad hoc log index format

Proposed location:

```text
%LOCALAPPDATA%\Replicator\replicator.db
```

## Initial Schema Sketch

```text
profiles
  id
  name
  source_path
  target_path
  created_at
  updated_at

jobs
  id
  profile_id
  operation
  status
  source_path
  target_path
  started_at
  completed_at
  elapsed_ms
  files_scanned
  files_copied
  files_skipped
  files_changed
  conflicts
  bytes_copied
  log_path
  manifest_path
  script_path
  error_summary

job_events
  id
  job_id
  timestamp
  level
  message

job_artifacts
  id
  job_id
  kind
  path
  description
```

## Implementation Notes

- Every operation should create a job row before doing work.
- Long-running operations should update progress counters during execution.
- The UI should bind to job summaries, not append all output to one text box.
- The detail view can still show raw logs, but logs should be linked artifacts, not the only source of truth.
- Scheduled task scripts should write structured job status back to the database or to an ingestible JSON status file that the app imports.

## Backlog

- Add `Replicator.Data` or equivalent storage layer.
- Add SQLite dependency and migration/versioning strategy.
- Replace output textbox with a jobs table.
- Add job detail pane.
- Add filters by profile, operation, status, and date.
- Add export for selected job logs/manifests.
