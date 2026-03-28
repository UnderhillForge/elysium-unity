# Elysium VTT Architecture Spec

## Goals

Elysium VTT is a Unity-based multiplayer virtual tabletop focused initially on Pathfinder 1e. The first release target is a GM-hosted private server that supports authored worlds, party exploration, turn-based encounters, SQLite persistence, and community world sharing through `.ewm` packages.

## Core Principles

- GM host is authoritative for world state, encounter state, visibility, and dice resolution.
- SQLite is the only persistence backend for private and persistent worlds.
- World authoring is prefab-first, with ProBuilder as a support tool and custom terrain paint/sculpt systems for natural spaces.
- World logic is primarily data-driven, with sandboxed Lua used for attachable behaviors.
- Shared world packages are portable and never depend on absolute host-local paths.

## Runtime Subsystems

### Networking

Unity Netcode for GameObjects runs in host mode first. Clients submit intents, and the GM host validates and commits authoritative results.

### Persistence

Each world or shard uses its own SQLite database. Authored content and runtime state are separated logically so the same storage model supports template exports, campaign snapshots, and persistent world hosting.

### World Authoring

Worlds are authored as editable folder projects. Areas contain terrain data, prefab placements, trigger definitions, NPC definitions, encounter content, and Lua attachments.

### Rules

Pathfinder 1e reference data is imported from the linked SRD source into a local cache owned by the game. Runtime rules processing, derived stat calculation, and validation happen locally on the host.

### Packaging

Worlds have two formats:

- Folder project: editable source of truth for creators and source control.
- `.ewm`: versioned single-file export for sharing and import.

Import pipeline validates manifest version, optional integrity hashes (`ewm-integrity.json`), and dependency requirements against installed content/rules packs before installation.

## Lua Scripting Model

Lua is allowed for attachable authored behaviors on:

- assets
- placeables
- triggers
- NPCs
- world-level controllers

Lua runs in a sandboxed managed interpreter. Scripts declare capabilities in package metadata and only receive access to a curated host API. Direct file system access, unrestricted reflection, native process execution, and raw network access are not part of the normal script surface.

MoonSharp is mandatory for runtime Lua execution. Fallback interpreters are not accepted in production or smoke validation.

The project uses the plugin assembly at `Assets/Plugins/MoonSharp.Interpreter.dll` as the active MoonSharp source. Do not enable a second source-copy package of MoonSharp at the same time, because duplicate symbol definitions can break script compilation.

Lua metadata can be declared in script header annotations (`@id`, `@attachment`, `@capabilities`) and exported into package manifest entries.

## Continuous Verification

Core runtime coverage is gated by a Unity batch smoke suite entrypoint (`Elysium.Editor.SmokeBatchRunner.RunCoreSmokeSuite`) and CI checks for the following markers:

- `[Session] PASS`
- `[Combat] PASS`
- `[LuaContextBindings] PASS`
- `[Persistence] PASS`
- `MoonSharp path: active`

## Initial Folder Ownership

- `Assets/Scripts/Boot`: startup and composition root
- `Assets/Scripts/Networking`: session orchestration and replication boundaries
- `Assets/Scripts/World`: area runtime, authoring, and streaming
- `Assets/Scripts/World/Lua`: Lua references, policy, and host services
- `Assets/Scripts/Packaging`: `.ewm` manifest, import/export, validation
- `Assets/Scripts/Rules`: PF1e import and rules services
- `Assets/Scripts/Characters`: character data and inventory
- `Assets/Scripts/Combat`: initiative and action execution

## Recommended First Vertical Slice

Build one outdoor authored area that can be loaded by a GM host, joined by players, explored in real time, transitioned into a simple turn-based encounter, saved to SQLite, and exported as a template `.ewm` package.

## Milestone 1 Checklist (Production Vertical Slice)

### Scope

Deliver one end-to-end host-authoritative gameplay path that covers session, Lua, combat, persistence, and package portability.

### Work Items

1. Session bootstrap and lobby flow
- GM can open a session for `starter_forest_edge`.
- At least one player can join, is visible to host, and receives assigned combatant ownership.

2. World load and scripted trigger execution
- Host loads the starter area and executes `on_world_loaded` through MoonSharp.
- Trigger script logs and encounter start hooks run through curated host APIs.

3. Turn-based combat loop
- Encounter initializes with deterministic combatants and initiative order.
- One full turn cycle completes: submit action, resolve action, advance turn.
- Combat state snapshots remain synchronized through networking service boundaries.

4. Persistence round-trip
- Active session plus encounter state is saved to `campaign.db`.
- Reload restores players, ownership bindings, current combatant, and pending actions.

5. Package import/export path
- World exports to `.ewm`, imports cleanly, and validates dependency/integrity checks.
- Imported world can be loaded and played through the same session->combat->save path.

6. CI verification gate
- Batch smoke suite is required for pull requests.
- Required pass markers remain:
	- `[Session] PASS`
	- `[Combat] PASS`
	- `[LuaContextBindings] PASS`
	- `[Persistence] PASS`
	- `MoonSharp path: active`

### Acceptance Criteria

1. Functional criteria
- Host and one player can complete one encounter interaction in the starter area without manual editor intervention.
- Lua execution uses MoonSharp (no fallback path) and exposes expected host context methods.
- Save and restore produce equivalent session/combat state for the tested scenario.

2. Quality criteria
- No compile errors in workspace.
- Core smoke suite passes in local batch mode and CI.
- No transient/generated runtime artifacts are required to be committed for the tested flow.

3. Definition of done
- Checklist items above are complete.
- CI gate is green on the merge commit.
- Architecture and API contract docs reflect current implemented behavior.

## Milestone 1 Sprint Plan (1 Week)

### Planning Assumptions

- Team size: solo developer with Copilot support.
- Sprint length: 5 working days.
- All tickets ship behind existing host-authoritative flow (no new runtime mode introduced).

### Ticket Backlog

1. M1-01: Session join and ownership hardening
- Owner: Solo (networking focus)
- Estimate: 1.0 day
- Dependencies: none
- Deliverables:
	- Validate host/lobby join flow for one GM and one player.
	- Ensure combatant assignment is persisted into session snapshots.
	- Add assertions in session smoke path for ownership checks.
- Exit criteria:
	- Session smoke passes locally and in CI.

2. M1-02: MoonSharp contract enforcement + diagnostics
- Owner: Solo (Lua/runtime focus)
- Estimate: 1.0 day
- Dependencies: none
- Deliverables:
	- Keep MoonSharp mandatory runtime behavior.
	- Improve Lua runtime errors so unsupported API variants report actionable details.
	- Keep Lua context bindings smoke asserting `MoonSharp path: active`.
- Exit criteria:
	- Lua bindings smoke is deterministic and fails with clear diagnostics when MoonSharp breaks.

3. M1-03: Encounter turn loop reliability
- Owner: Solo (combat focus)
- Estimate: 1.0 day
- Dependencies: M1-01
- Deliverables:
	- Validate submit/resolve/advance turn loop under host authority.
	- Ensure snapshot publication order remains stable through one full turn cycle.
	- Add one regression assertion for pending action count transitions.
- Exit criteria:
	- Combat smoke remains green with stronger assertions.

4. M1-04: Persistence round-trip equivalence checks
- Owner: Solo (persistence focus)
- Estimate: 1.0 day
- Dependencies: M1-01, M1-03
- Deliverables:
	- Verify save/load restores active session state and combat state invariants.
	- Assert player binding, current combatant, and pending action equivalence after reload.
	- Confirm schema writes are non-destructive to existing migration-owned tables.
- Exit criteria:
	- Persistence smoke passes with explicit equivalence checks.

5. M1-05: Package portability verification
- Owner: Solo (world/packaging focus)
- Estimate: 0.5 day
- Dependencies: M1-04
- Deliverables:
	- Export `.ewm` and re-import into clean target folder.
	- Validate manifest/dependency checks and replay session-combat-save path.
- Exit criteria:
	- Round-trip package smoke is reproducible in one documented command path.

6. M1-06: CI and release gate finalization
- Owner: Solo (build/release focus)
- Estimate: 0.5 day
- Dependencies: M1-02, M1-03, M1-04
- Deliverables:
	- Enforce core smoke workflow as required PR status.
	- Persist smoke log artifacts and failure summaries.
	- Document local command parity with CI.
- Exit criteria:
	- Merge blocked when any required smoke marker is missing.

### Suggested Execution Order

1. Day 1: M1-01, M1-02
2. Day 2: M1-03
3. Day 3: M1-04
4. Day 4: M1-05
5. Day 5: M1-06, milestone sign-off, release candidate branch cut

### Sprint Success Metrics

- 100% pass rate for required core smoke markers on merge commits.
- 0 unresolved P1 defects in host-session, Lua runtime, combat loop, or persistence round-trip.
- One reproducible demo script for the full vertical slice flow.