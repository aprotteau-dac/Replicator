# Replicator Docs

Replicator is a .NET 10 WinUI 3 Windows backup and shuttle-control app for sensitive local workstreams that should not be pushed to cloud sync or ordinary Git remotes.

Use these docs as the project wiki until the GitHub wiki is available.

## Pages

- [Concepts](Concepts.md)
- [Backup Mode](Backup-Mode.md)
- [Shuttle Mode](Shuttle-Mode.md)
- [Drive Security](Drive-Security.md)
- [Brand](Brand.md)
- [Bug Tracking](Bug-Tracking.md)
- [Backlog Decision Log](Backlog-Decision-Log.md)
- [Job History And Audit](Job-History-And-Audit.md)
- [Smoke Test Plan](Smoke-Test-Plan.md)
- [Packaging And Install](Packaging-And-Install.md)
- [Architecture Notes](Architecture-Notes.md)
- [Roadmap And Backlog](Roadmap-And-Backlog.md)

## Current Status

Replicator is an early .NET 10 WinUI 3 prototype. Backup mode is usable for local testing with availability preflight, minute/hourly/daily/weekly schedules, generated PowerShell scripts, and Task Scheduler integration. Shuttle mode is a first vertical slice for controlled external-drive handoff with progress, cancellation, and conflict-preserving receive behavior. It should still be used carefully until conflict review, drive identity, resumability, and secure-drive enforcement mature.
