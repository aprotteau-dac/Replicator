# Bug Tracking

GitHub Issues is the canonical bug log for Replicator:

```text
https://github.com/aprotteau-dac/Replicator/issues
```

Open an issue for reproducible defects, regressions, smoke-test failures, and cases where Replicator might write to an unexpected path or hide an unsafe state.

Keep planned feature work in [Roadmap And Backlog](Roadmap-And-Backlog.md) until it becomes a confirmed defect or regression.

## When To Open A Bug

- A smoke-test stop criterion occurs.
- Backup or shuttle writes to an unexpected path.
- Unavailable source or target paths start a copy anyway.
- A shuttle receive overwrites local content without preserving a conflict copy.
- The UI hangs or cannot recover after cancellation or a failed operation.
- Installer or packaging behavior installs stale bits.

## Issue Hygiene

- Use the bug-report template.
- Include exact steps to reproduce with disposable paths.
- Include the latest log path or smoke-test command output when available.
- Link related docs or backlog items when the bug is part of a larger product gap.
