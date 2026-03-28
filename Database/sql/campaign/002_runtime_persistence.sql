BEGIN TRANSACTION;

CREATE TABLE IF NOT EXISTS active_sessions (
    session_id TEXT PRIMARY KEY,
    world_project_id TEXT NOT NULL,
    state INTEGER NOT NULL,
    gm_player_id TEXT,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS session_players (
    session_id TEXT NOT NULL,
    player_id TEXT NOT NULL,
    display_name TEXT NOT NULL,
    network_client_id INTEGER NOT NULL,
    role INTEGER NOT NULL,
    assigned_combatant_id TEXT,
    joined_at_utc INTEGER NOT NULL,
    is_connected INTEGER NOT NULL,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (session_id, player_id),
    FOREIGN KEY (session_id) REFERENCES active_sessions(session_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS encounter_combatants (
    encounter_instance_id TEXT NOT NULL,
    combatant_id TEXT NOT NULL,
    actor_name TEXT NOT NULL,
    character_id TEXT,
    initiative_roll INTEGER NOT NULL,
    turn_order INTEGER NOT NULL,
    hit_points_current INTEGER NOT NULL,
    hit_points_max INTEGER NOT NULL,
    armor_class INTEGER NOT NULL,
    armor_class_touch INTEGER NOT NULL,
    armor_class_flat_footed INTEGER NOT NULL,
    has_taken_turn INTEGER NOT NULL,
    is_defeated INTEGER NOT NULL,
    is_stunned INTEGER NOT NULL,
    standard_actions_remaining INTEGER NOT NULL,
    move_actions_remaining INTEGER NOT NULL,
    swift_actions_remaining INTEGER NOT NULL,
    free_actions_remaining INTEGER NOT NULL,
    active_conditions_json TEXT,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (encounter_instance_id, combatant_id),
    FOREIGN KEY (encounter_instance_id) REFERENCES encounter_instances(encounter_instance_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS encounter_actions (
    encounter_instance_id TEXT NOT NULL,
    action_id TEXT NOT NULL,
    combatant_id TEXT,
    target_combatant_id TEXT,
    round_number INTEGER NOT NULL,
    turn_number INTEGER NOT NULL,
    action_type INTEGER NOT NULL,
    action_name TEXT NOT NULL,
    action_description TEXT,
    is_resolved INTEGER NOT NULL,
    succeeded INTEGER NOT NULL,
    resolution_result TEXT,
    timestamp_utc INTEGER NOT NULL,
    is_pending INTEGER NOT NULL,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (encounter_instance_id, action_id),
    FOREIGN KEY (encounter_instance_id) REFERENCES encounter_instances(encounter_instance_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_session_players_session_id ON session_players(session_id);
CREATE INDEX IF NOT EXISTS idx_encounter_combatants_instance ON encounter_combatants(encounter_instance_id, turn_order);
CREATE INDEX IF NOT EXISTS idx_encounter_actions_instance_pending ON encounter_actions(encounter_instance_id, is_pending, round_number, turn_number);

INSERT OR REPLACE INTO schema_migrations(version, description, applied_utc)
VALUES (2, 'Normalized runtime persistence tables for sessions and combat', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

COMMIT;