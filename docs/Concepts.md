# Concepts

Replicator separates workflows that are often mixed together by ordinary backup and sync tools.

## Backup

Backup mode protects local state on a cadence. It is one-way and should preserve an authoritative copy of a machine's work without being mutated by other machines.

## Shuttle

Shuttle mode is controlled handoff through an external drive. It is bidirectional across time, but not simultaneous automatic two-way sync.

Typical flow:

```text
Machine A works -> Prepare Shuttle -> Depart
Machine B docks -> Receive Changes -> works -> Prepare Shuttle -> Depart
Machine A docks -> Receive Changes
```

## Restore And Converge

Restore and converge are future explicit workflows. Backups should remain authoritative evidence. Converged copies should be derived state that can be rebuilt.

## Git Shuttle

Git shuttle is a planned engine where an external drive hosts private bare Git remotes for sensitive repositories. That lets committed work move through Git's conflict model without network remotes.
