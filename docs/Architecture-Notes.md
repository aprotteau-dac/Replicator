# Architecture Notes

## Shuttle Should Be Additive

The current prototype models `Backup` and `Shuttle` as separate profile modes. Field testing showed that this is the wrong long-term shape.

Target model:

```text
Profile
  Source
  Protection target
  Schedule
  Shuttle capability optional
    Shuttle pair root
    Prepare/Depart/Dock/Receive state
```

In other words, a profile should keep protecting local state on a cadence while also exposing shuttle handoff actions when a shuttle pair is configured.

## Large Shuttle Performance

Shuttling about 6,500 files worked but caused major UI lockup in the first prototype.

Initial mitigations now implemented:

- shuttle file work runs off the WPF UI thread
- file-count progress is reported with throttled UI updates
- user-initiated cancellation is wired through shuttle operations
- hashing uses streams instead of whole-file reads

Remaining target implementation:

- move from direct task execution to a background job service
- report richer progress with bytes, current relative path, phase, and elapsed time
- write operation logs incrementally to disk
- keep UI output summarized and tail-based
- use incremental manifests so unchanged files can avoid rehashing where possible
- consider rclone or Git engines for large trees instead of custom file-copy logic
