# Milestone 1 Issue Templates (Solo)

Use these as copy-paste issue bodies for your tracker.

## M1-01 Session Join and Ownership Hardening

### Title
M1-01: Session join and ownership hardening

### Description
Harden host and player session flow for one GM + one player, ensuring ownership and assignment rules are enforced and persisted.

### Scope
- Validate open session path for starter_forest_edge.
- Validate one player join and combatant assignment.
- Validate ownership checks for action authorization boundaries.
- Ensure assignment survives snapshot save/load.

### Acceptance Criteria
- Session smoke passes with explicit assertions for player ownership.
- Combatant owner lookup returns expected player id after restore.
- Unauthorized ownership actions are rejected with actionable error.

### Definition of Done
- Code merged with passing CI smoke markers.
- No compile errors.
- Notes added to architecture/API docs only if behavior changed.

### Estimate
1.0 day

## M1-02 MoonSharp Contract Enforcement and Diagnostics

### Title
M1-02: MoonSharp contract enforcement and diagnostics

### Description
Keep MoonSharp mandatory and make Lua runtime failures immediately actionable when reflection/API variants drift.

### Scope
- Keep fallback path disabled.
- Ensure runtime reports clear root causes for MoonSharp invocation errors.
- Ensure Lua smoke requires MoonSharp active marker.

### Acceptance Criteria
- Lua bindings smoke reports MoonSharp path active on pass.
- MoonSharp API mismatch failures include actionable error text.
- No fallback execution path is reachable in production flow.

### Definition of Done
- Batch smoke suite passes locally and in CI.
- Negative-path diagnostics are readable without debugger attachment.

### Estimate
1.0 day

## M1-03 Encounter Turn Loop Reliability

### Title
M1-03: Encounter turn loop reliability

### Description
Ensure one complete submit/resolve/advance cycle is deterministic and reflected correctly in network snapshots.

### Scope
- Validate action submit and pending state.
- Validate GM resolution transitions pending to resolved.
- Validate turn advancement and next combatant state.
- Validate snapshot publication ordering for one cycle.

### Acceptance Criteria
- Combat smoke asserts pending count and next turn state.
- No inconsistent state between combat service and network snapshot.
- One full turn loop completes without manual intervention.

### Definition of Done
- Combat smoke updated and green in CI.
- No regressions in session smoke.

### Estimate
1.0 day

## M1-04 Persistence Round-Trip Equivalence

### Title
M1-04: Persistence round-trip equivalence checks

### Description
Guarantee save/load restores the same observable session and encounter state for the vertical slice.

### Scope
- Save active session + combat state.
- Reload and compare critical invariants.
- Verify player ownership, current combatant, and pending action list.
- Validate migration-owned schema behavior remains non-destructive.

### Acceptance Criteria
- Persistence smoke checks invariant equivalence post-load.
- Normalized table counts are expected and stable.
- No destructive schema mutations in runtime path.

### Definition of Done
- Persistence smoke green in local + CI runs.
- Any schema assumptions documented.

### Estimate
1.0 day

## M1-05 Package Portability Verification

### Title
M1-05: Package portability verification

### Description
Confirm exported package can be imported cleanly and replay the same core runtime path.

### Scope
- Export world to .ewm.
- Import into clean target.
- Validate manifest, dependencies, and integrity checks.
- Run session->combat->save path on imported package.

### Acceptance Criteria
- Import succeeds without manual file edits.
- Same smoke flow is reproducible from imported content.
- Validation errors are clear when dependency mismatch is introduced.

### Definition of Done
- Round-trip flow documented in one command/procedure.
- Packaging path stable across reruns.

### Estimate
0.5 day

## M1-06 CI and Release Gate Finalization

### Title
M1-06: CI and release gate finalization

### Description
Finalize smoke-based merge policy and artifact retention so regressions are blocked automatically.

### Scope
- Enforce required core smoke workflow status on pull requests.
- Ensure smoke log artifact is uploaded on success/failure.
- Verify local and CI command parity.

### Acceptance Criteria
- Merge is blocked when required smoke markers are missing.
- CI run always includes core-smoke.log artifact.
- Required markers include MoonSharp path active.

### Definition of Done
- Branch protection policy updated.
- CI workflow documented in repo docs.

### Estimate
0.5 day
