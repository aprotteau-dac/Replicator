# Shuttle Mode

Shuttle mode is for bidirectional, human-controlled handoff through an external drive. It is not silent cloud sync.

## Example

```text
Machine A local source
  F:\work\client-a-investigation

External shuttle pair root
  X:\Replicator\shuttle\client-a-investigation

Machine B local source
  F:\work\client-a-investigation
```

Set the profile mode to `Shuttle`, use `Source` as the local paired repo/folder, and use `Shuttle path` as the external drive pair root.

Long term, shuttle should not replace backup behavior. A shuttle profile should be a layered profile: it still performs scheduled `Protect` backups/checkpoints while also exposing manual `Prepare Shuttle`, `Depart`, `Dock Shuttle`, and `Receive Changes` actions. The current prototype treats `Backup` and `Shuttle` as separate modes, which is useful for proving the workflow but not the final model.

## Managed Structure

Replicator manages this structure under the shuttle pair root:

```text
payload\
manifests\
state\
conflicts\
```

## Workflow

- `Prepare Shuttle`: stages local changes into the shuttle payload and writes a prepare manifest. If `Dry run` is checked, it previews only.
- `Depart`: marks the staged payload ready for another machine to dock.
- `Dock Shuttle`: scans an inbound departed payload and summarizes new, changed, and conflicting files.
- `Receive Changes`: applies inbound files and preserves overwritten local files under `conflicts\`.

## Intended Rhythm

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

## Design Rules

- Shuttle mode is interactive by default.
- Shuttle is layered on top of protection, not a replacement for backup/checkpoint cadence.
- Backups remain authoritative; shuttle payloads are handoff state.
- Conflict handling must preserve data rather than silently delete or overwrite without a preserved copy.
- Future matching should support path drift, where the same repo exists at a different local path on another machine.

## Field Notes

- Shuttling about 6,500 files worked, but caused major UI lockup in the first prototype. Shuttle operations now run off the WinUI thread, report throttled file-count progress, can be canceled from the UI, preserve payload timestamps, prune excluded directories before recursion, and write per-file manifest entries with SHA-256 hashes. First-time staging hashes while copying so new source files are not read twice. Later prepares can reuse unchanged manifest hashes, and dock/receive can classify drifted-timestamp files from the manifest hash without re-reading the shuttle payload file.
- Current long-smoke timing on a generated 6,500-file tree: first prepare about 8-10s, skipped prepare under 1s, dock analysis about 2s. Real timings will vary with file sizes, excluded folder volume, and external-drive speed.
- The current UI presents `Backup` and `Shuttle` as either/or profile modes. The desired product model is combined: a profile can protect on cadence and also have shuttle handoff actions.
