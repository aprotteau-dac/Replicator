# Drive Security

Replicator is intended for sensitive local workstreams, so external-drive security should become a first-class feature.

The likely implementation path is a BitLocker wrapper rather than custom encryption.

## Current Implementation

Replicator performs a visibility-only BitLocker posture check for local Windows drive roots used by all loaded profiles. The app runs the first pass from the normal user session; it does not require Replicator itself to be launched as administrator.

The app checks:

- source drive
- backup target drive
- shuttle drive

The header can show:

- BitLocker protected
- not BitLocker protected
- BitLocker locked
- unavailable
- permission required for BitLocker verification
- unknown

Drive posture is cached by drive root for the current app session. Switching between profiles reuses the same checked source, backup target, and shuttle-drive results instead of prompting or re-querying the same drive repeatedly.

If Windows denies the standard BitLocker status query, Replicator reports a permission-required warning instead of showing raw PowerShell/CIM error text. Backups and shuttle actions are not blocked by this warning because drive security is still visibility-only.

Administrator elevation remains a narrow verification action, not an app-wide requirement. When the header shows permission-required drive security, `Check All as Admin` performs one elevated verification pass for all profile-attached local drive roots and updates the shared cache. This avoids re-verifying each profile independently.

Current limitation: the elevated check still uses an elevated PowerShell helper, so the UAC prompt is branded as PowerShell. A branded Replicator-owned elevated helper executable is planned so the prompt can show Replicator identity instead of generic PowerShell.

This is not yet an enforcement control. Replicator warns about unprotected, permission-limited, or unknown drives, but it does not block backup or shuttle writes yet.

## Planned BitLocker Enforcement

- Block `Prepare Shuttle`, `Receive Changes`, and scheduled backup writes when policy requires encryption and the target drive is locked, unprotected, unavailable, permission-required, or unknown.
- Offer guided setup for external drives using BitLocker To Go.
- Store recovery-key reminders and policy metadata, but never store recovery keys in Replicator profile JSON.
- Prefer Windows-native unlock flows and Credential Manager/DPAPI for local secrets that are truly required.

Permission-required must never be treated as equivalent to BitLocker protected. It only means Replicator could not verify the encryption state from the current permission context.

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
