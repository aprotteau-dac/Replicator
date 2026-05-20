# Drive Security

Replicator is intended for sensitive local workstreams, so external-drive security should become a first-class feature.

The likely implementation path is a BitLocker wrapper rather than custom encryption.

## Current Implementation

Replicator now performs a visibility-only BitLocker posture check for local Windows drive roots used by a profile.

The app checks:

- source drive
- backup target drive
- shuttle drive

The header can show:

- BitLocker protected
- not BitLocker protected
- BitLocker locked
- unavailable or unknown

If Windows denies the BitLocker status query, Replicator reports that the status cannot be checked without elevated permissions instead of showing raw PowerShell/CIM error text.

This is not yet an enforcement control. Replicator warns about unprotected or unknown drives, but it does not block backup or shuttle writes yet.

## Planned BitLocker Enforcement

- Block `Prepare Shuttle`, `Receive Changes`, and scheduled backup writes when policy requires encryption and the target drive is locked, unprotected, unavailable, or unknown.
- Offer guided setup for external drives using BitLocker To Go.
- Store recovery-key reminders and policy metadata, but never store recovery keys in Replicator profile JSON.
- Prefer Windows-native unlock flows and Credential Manager/DPAPI for local secrets that are truly required.

## Current Guidance

Use BitLocker manually on external shuttle and backup drives before trusting them with sensitive content.

## Future Policy Model

Profiles should eventually support policy like:

```text
Require encrypted target: true
Allow unprotected local backup: false
Allow unprotected shuttle depart: false
Require drive identity match: true
```
