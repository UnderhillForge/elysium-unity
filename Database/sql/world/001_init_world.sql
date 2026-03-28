BEGIN TRANSACTION;

CREATE TABLE IF NOT EXISTS schema_migrations (
    version INTEGER PRIMARY KEY,
    description TEXT NOT NULL,
    applied_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS project_metadata (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS areas (
    area_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    biome TEXT,
    size_x INTEGER NOT NULL,
    size_z INTEGER NOT NULL,
    entry_spawn_id TEXT,
    lighting_profile TEXT,
    music_cue TEXT,
    source_json TEXT,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS terrain_layers (
    area_id TEXT NOT NULL,
    layer_id TEXT NOT NULL,
    weight REAL NOT NULL,
    PRIMARY KEY (area_id, layer_id),
    FOREIGN KEY (area_id) REFERENCES areas(area_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS spawns (
    spawn_id TEXT PRIMARY KEY,
    area_id TEXT NOT NULL,
    pos_x REAL NOT NULL,
    pos_y REAL NOT NULL,
    pos_z REAL NOT NULL,
    FOREIGN KEY (area_id) REFERENCES areas(area_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS placeables (
    placeable_id TEXT PRIMARY KEY,
    area_id TEXT NOT NULL,
    asset_ref TEXT NOT NULL,
    pos_x REAL NOT NULL,
    pos_y REAL NOT NULL,
    pos_z REAL NOT NULL,
    rot_y REAL NOT NULL,
    scale REAL NOT NULL DEFAULT 1.0,
    lua_attachment TEXT,
    source_json TEXT,
    FOREIGN KEY (area_id) REFERENCES areas(area_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS triggers (
    trigger_id TEXT PRIMARY KEY,
    area_id TEXT NOT NULL,
    trigger_kind TEXT NOT NULL,
    pos_x REAL NOT NULL,
    pos_y REAL NOT NULL,
    pos_z REAL NOT NULL,
    radius REAL,
    on_enter_lua TEXT,
    on_exit_lua TEXT,
    enabled INTEGER NOT NULL DEFAULT 1,
    source_json TEXT,
    FOREIGN KEY (area_id) REFERENCES areas(area_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS encounters (
    encounter_id TEXT PRIMARY KEY,
    area_id TEXT NOT NULL,
    display_name TEXT NOT NULL,
    trigger_id TEXT,
    source_json TEXT,
    FOREIGN KEY (area_id) REFERENCES areas(area_id) ON DELETE CASCADE,
    FOREIGN KEY (trigger_id) REFERENCES triggers(trigger_id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS encounter_npcs (
    encounter_id TEXT NOT NULL,
    npc_id TEXT NOT NULL,
    order_index INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (encounter_id, npc_id),
    FOREIGN KEY (encounter_id) REFERENCES encounters(encounter_id) ON DELETE CASCADE,
    FOREIGN KEY (npc_id) REFERENCES npcs(npc_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS npcs (
    npc_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    creature_ref TEXT NOT NULL,
    lua_attachment TEXT,
    source_json TEXT
);

CREATE TABLE IF NOT EXISTS factions (
    faction_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    default_disposition TEXT NOT NULL,
    source_json TEXT
);

CREATE TABLE IF NOT EXISTS quests (
    quest_id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    default_state TEXT NOT NULL,
    source_json TEXT
);

CREATE TABLE IF NOT EXISTS quest_steps (
    quest_id TEXT NOT NULL,
    step_index INTEGER NOT NULL,
    description TEXT NOT NULL,
    PRIMARY KEY (quest_id, step_index),
    FOREIGN KEY (quest_id) REFERENCES quests(quest_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS dialogs (
    dialog_id TEXT PRIMARY KEY,
    speaker TEXT,
    source_json TEXT
);

CREATE TABLE IF NOT EXISTS dialog_lines (
    dialog_id TEXT NOT NULL,
    line_index INTEGER NOT NULL,
    line_text TEXT NOT NULL,
    PRIMARY KEY (dialog_id, line_index),
    FOREIGN KEY (dialog_id) REFERENCES dialogs(dialog_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS lua_scripts (
    script_path TEXT PRIMARY KEY,
    script_id TEXT,
    attachment_kind TEXT NOT NULL,
    capabilities_csv TEXT,
    checksum_sha256 TEXT,
    enabled INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS assets_manifest (
    asset_id TEXT PRIMARY KEY,
    asset_type TEXT NOT NULL,
    asset_path TEXT NOT NULL,
    redistributable INTEGER NOT NULL,
    source_json TEXT
);

CREATE TABLE IF NOT EXISTS dependencies (
    dependency_id TEXT PRIMARY KEY,
    min_version TEXT
);

CREATE INDEX IF NOT EXISTS idx_placeables_area_id ON placeables(area_id);
CREATE INDEX IF NOT EXISTS idx_triggers_area_id ON triggers(area_id);
CREATE INDEX IF NOT EXISTS idx_encounters_area_id ON encounters(area_id);

INSERT OR REPLACE INTO schema_migrations(version, description, applied_utc)
VALUES (1, 'Initial world schema', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

COMMIT;
