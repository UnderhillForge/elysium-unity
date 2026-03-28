# EWM World Package Format Spec

## Purpose

`.ewm` is the portable distribution format for Elysium world content. It is built from an editable folder-based world project and supports both clean template exports and live campaign snapshot exports.

## Internal World Project Layout

```text
WorldProject/
  project.json
  preview.png
  Databases/
    world.db
    campaign.db
  Areas/
    area_forest_edge/
      area.json
      terrain.json
      placements.json
      triggers.json
      encounters.json
      navmask.bin
  Actors/
    npcs.json
    factions.json
  Quests/
    quests.json
    dialog.json
  Scripts/
    world/
    placeables/
    triggers/
    npcs/
    assets/
  Assets/
    manifest.json
    bundles/
  Dependencies/
    dependencies.json
```

## `.ewm` Container

`.ewm` should be a single-file archive with deterministic layout.

```text
package.ewm
  manifest.json
  ewm-integrity.json
  preview.png
  Databases/
  Areas/
  Actors/
  Quests/
  Scripts/
  Assets/
  Dependencies/
```

## Export Modes

### Template

Includes:

- authored world content
- distributable assets
- NPC, encounter, quest, dialog, and trigger definitions
- attached Lua scripts
- clean authored database state

Excludes:

- active party progress
- live fog-of-war progress
- transient server/session state
- host-local settings, logs, and caches

### Campaign Snapshot

Includes everything in template mode, plus:

- active world state
- party and inventory progress
- encounter progress
- discovered fog state
- campaign database state needed to resume play

## Manifest Schema

```json
{
  "formatVersion": 1,
  "packageId": "com.elysium.world.forest-edge",
  "displayName": "Forest Edge",
  "author": "Creator Name",
  "packageMode": "Template",
  "gameVersion": "0.1.0",
  "apiVersion": "1.0.0",
  "ruleset": "Pathfinder1e",
  "entryAreaId": "area_forest_edge",
  "createdUtc": "2026-03-26T00:00:00Z",
  "dependencies": [],
  "dependencyRequirements": [
    {
      "id": "com.elysium.rules.pathfinder1e.core",
      "minVersion": "0.1.0"
    }
  ],
  "lua": {
    "enabled": true,
    "hostApiVersion": "1.0.0",
    "scripts": [
      {
        "id": "world.init",
        "path": "Scripts/world/init.lua",
        "attachmentKind": "World",
        "capabilities": ["world.read", "quest.write"]
      }
    ]
  },
  "assets": {
    "embedded": true,
    "manifestPath": "Assets/manifest.json"
  }
}
```

## Lua Header Metadata

Lua script metadata can be inferred from header comments during export:

```lua
-- @id: trigger.ambush.on_enter
-- @attachment: Trigger
-- @capabilities: encounter.control,world.read
```

Supported keys:

- `@id`: overrides script id in manifest
- `@attachment`: `Asset`, `Placeable`, `Trigger`, `Npc`, `World`
- `@capabilities`: comma-separated capability list

If headers are not present, exporter infers attachment kind from script folder and leaves capabilities empty.

## Integrity File

`ewm-integrity.json` stores SHA-256 hashes for package payload files. Import verifies hashes before any world project installation.

If integrity metadata is missing, import can continue with a warning.

## Validation Rules

- Import must reject unsupported `formatVersion` values.
- Import must warn on mismatched `gameVersion` or `apiVersion`.
- Import should verify dependency requirements against installed rule/content packs when available.
- Import should validate `ewm-integrity.json` hashes before installation.
- Embedded assets must be redistributable.
- Lua scripts must declare capabilities and attachment kinds.
- Package import must never rely on absolute paths.
- Campaign snapshot imports should be clearly labeled as resume-state content.

## Security Model

- `.ewm` may include Lua scripts.
- Lua runs only through the sandboxed host runtime.
- `.ewm` does not include arbitrary native plugins or unrestricted executable code.