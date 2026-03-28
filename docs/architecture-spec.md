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