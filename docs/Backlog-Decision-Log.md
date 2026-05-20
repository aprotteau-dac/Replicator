# Backlog Decision Log

## 2026-05-20 Shuttle Availability Preflight

Decision question: which single backlog slice should Replicator implement next?

Weights: user safety/value 30%, risk reduction 25%, effort fit 20%, dependency unlock 15%, testability 10%.

| Option | User safety/value | Risk reduction | Effort fit | Dependency unlock | Testability | Weighted score | Notes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Graceful unavailable states: shuttle preflight | 5 | 5 | 5 | 4 | 5 | 4.85 | Small safety slice that stops shuttle file work before missing source or shuttle paths can create confusing state. |
| BitLocker policy enforcement | 4 | 5 | 3 | 4 | 3 | 4.05 | Important, but needs policy choices and unlock guidance before blocking behavior is clear. |
| Conflict review UI | 5 | 5 | 1 | 4 | 2 | 3.85 | High value, but too large for a single focused pass. |
| Job history and audit UI | 5 | 4 | 2 | 4 | 3 | 3.75 | Strong product value, but needs persistence and UI design. |
| Drive identity over drive letters | 4 | 4 | 3 | 4 | 3 | 3.75 | Useful foundation, but less immediate than blocking unsafe operations. |
| Shuttle protect cadence | 4 | 4 | 2 | 4 | 3 | 3.55 | Valuable scheduler work, but depends on clearer shuttle state rules. |
| Known shuttle drive detection | 4 | 3 | 2 | 4 | 3 | 3.25 | Good ergonomics, but needs watcher/service shape. |
| Path drift compensation | 3 | 3 | 2 | 3 | 2 | 2.75 | Useful later, but harder to validate in a small pass. |

Recommendation: implement graceful unavailable states for shuttle operations.

Sensitivity: stable. Even if effort fit is reduced, the safety and testability scores keep this ahead for the current pass.

Next action: block `Prepare Shuttle`, `Depart`, `Dock Shuttle`, and `Receive Changes` when profile availability has errors, keep expanded source paths consistent during shuttle file work, and cover the behavior with smoke-gate regression tests.
