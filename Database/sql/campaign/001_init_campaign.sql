BEGIN TRANSACTION;

CREATE TABLE IF NOT EXISTS schema_migrations (
    version INTEGER PRIMARY KEY,
    description TEXT NOT NULL,
    applied_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS campaign_metadata (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS parties (
    party_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS party_members (
    party_id TEXT NOT NULL,
    character_id TEXT NOT NULL,
    display_name TEXT NOT NULL,
    level INTEGER NOT NULL,
    class_name TEXT,
    joined_utc TEXT NOT NULL,
    PRIMARY KEY (party_id, character_id),
    FOREIGN KEY (party_id) REFERENCES parties(party_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS quest_progress (
    party_id TEXT NOT NULL,
    quest_id TEXT NOT NULL,
    state TEXT NOT NULL,
    progress_json TEXT,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (party_id, quest_id),
    FOREIGN KEY (party_id) REFERENCES parties(party_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS fog_discovery (
    party_id TEXT NOT NULL,
    area_id TEXT NOT NULL,
    cell_x INTEGER NOT NULL,
    cell_z INTEGER NOT NULL,
    discovered_utc TEXT NOT NULL,
    PRIMARY KEY (party_id, area_id, cell_x, cell_z),
    FOREIGN KEY (party_id) REFERENCES parties(party_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS encounter_instances (
    encounter_instance_id TEXT PRIMARY KEY,
    encounter_id TEXT NOT NULL,
    area_id TEXT NOT NULL,
    state TEXT NOT NULL,
    round_number INTEGER NOT NULL DEFAULT 0,
    active_combatant_id TEXT,
    snapshot_json TEXT,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS npc_runtime_state (
    party_id TEXT NOT NULL,
    npc_id TEXT NOT NULL,
    area_id TEXT NOT NULL,
    pos_x REAL,
    pos_y REAL,
    pos_z REAL,
    hp_current INTEGER,
    conditions_json TEXT,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (party_id, npc_id),
    FOREIGN KEY (party_id) REFERENCES parties(party_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS container_runtime_state (
    party_id TEXT NOT NULL,
    container_id TEXT NOT NULL,
    area_id TEXT NOT NULL,
    loot_json TEXT,
    is_opened INTEGER NOT NULL DEFAULT 0,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (party_id, container_id),
    FOREIGN KEY (party_id) REFERENCES parties(party_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS world_state_kv (
    scope TEXT NOT NULL,
    state_key TEXT NOT NULL,
    state_value TEXT,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (scope, state_key)
);

CREATE TABLE IF NOT EXISTS dice_audit_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    party_id TEXT,
    encounter_instance_id TEXT,
    actor_id TEXT,
    action_type TEXT,
    roll_formula TEXT,
    roll_total INTEGER,
    roll_json TEXT,
    created_utc TEXT NOT NULL,
    FOREIGN KEY (party_id) REFERENCES parties(party_id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS save_checkpoints (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    party_id TEXT,
    label TEXT,
    reason TEXT,
    world_revision TEXT,
    campaign_revision TEXT,
    created_utc TEXT NOT NULL,
    FOREIGN KEY (party_id) REFERENCES parties(party_id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_fog_party_area ON fog_discovery(party_id, area_id);
CREATE INDEX IF NOT EXISTS idx_encounter_state ON encounter_instances(state);
CREATE INDEX IF NOT EXISTS idx_dice_log_party ON dice_audit_log(party_id);

INSERT OR REPLACE INTO schema_migrations(version, description, applied_utc)
VALUES (1, 'Initial campaign schema', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

COMMIT;
