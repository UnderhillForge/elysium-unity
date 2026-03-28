using System;
using System.Data;
using System.IO;
using System.Collections.Generic;
using Elysium.Combat;
using Elysium.Networking;
using UnityEngine;

namespace Elysium.Persistence
{
    /// Saves and restores session/combat state to the campaign SQLite database.
    /// Uses existing campaign_metadata and encounter_instances tables.
    public sealed class CampaignPersistenceService
    {
        private const string SessionSnapshotKey = "session.active_snapshot_json";
        private const string WorldStateScope = "world";
        private readonly string databasePath;

        public CampaignPersistenceService(string campaignDatabasePath)
        {
            databasePath = campaignDatabasePath ?? string.Empty;
        }

        public string DatabasePath => databasePath;

        public bool TrySaveSession(SessionService session, out string error)
        {
            if (session == null)
            {
                error = "Session cannot be null.";
                return false;
            }

            return TryExecute(connection =>
            {
                EnsureNormalizedSchema(connection);
                var json = JsonUtility.ToJson(session.CreateSnapshot());
                UpsertMetadata(connection, SessionSnapshotKey, json);
                SaveNormalizedSession(connection, session.CreateSnapshot());
            }, out error);
        }

        public bool TryLoadSession(out SessionService session, out string error)
        {
            session = null;
            SessionService loadedSession = null;
            if (!TryExecute(connection =>
                {
                    EnsureNormalizedSchema(connection);

                    var normalized = TryLoadNormalizedSession(connection);
                    if (normalized != null)
                    {
                        loadedSession = new SessionService();
                        loadedSession.RestoreFromSnapshot(normalized);
                        return;
                    }

                    var json = ReadMetadata(connection, SessionSnapshotKey);
                    if (string.IsNullOrEmpty(json))
                    {
                        throw new InvalidOperationException("No persisted session snapshot found.");
                    }

                    var snapshot = JsonUtility.FromJson<SessionPersistenceSnapshot>(json);
                    loadedSession = new SessionService();
                    loadedSession.RestoreFromSnapshot(snapshot);
                }, out error))
            {
                return false;
            }

            session = loadedSession;
            return true;
        }

        public bool TrySaveEncounterState(
            string encounterInstanceId,
            string encounterId,
            string areaId,
            CombatStateService combatState,
            out string error)
        {
            if (combatState == null)
            {
                error = "Combat state cannot be null.";
                return false;
            }

            return TryExecute(connection =>
            {
                EnsureNormalizedSchema(connection);
                var snapshot = combatState.CreatePersistenceSnapshot();
                var summary = combatState.CreateEncounterSnapshot();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT OR REPLACE INTO encounter_instances(" +
                    "encounter_instance_id, encounter_id, area_id, state, round_number, active_combatant_id, snapshot_json, updated_utc) " +
                    "VALUES(@instanceId, @encounterId, @areaId, @state, @round, @activeCombatantId, @snapshotJson, @updatedUtc);";
                AddParameter(command, "@instanceId", encounterInstanceId);
                AddParameter(command, "@encounterId", string.IsNullOrEmpty(encounterId) ? combatState.EncounterId : encounterId);
                AddParameter(command, "@areaId", areaId ?? string.Empty);
                AddParameter(command, "@state", summary.IsActive ? "Active" : "Inactive");
                AddParameter(command, "@round", summary.RoundNumber);
                AddParameter(command, "@activeCombatantId", summary.CurrentCombatantId);
                AddParameter(command, "@snapshotJson", JsonUtility.ToJson(snapshot));
                AddParameter(command, "@updatedUtc", GetUtcNowIso8601());
                command.ExecuteNonQuery();

                SaveNormalizedEncounter(connection, encounterInstanceId, snapshot);
            }, out error);
        }

        public bool TryLoadEncounterState(
            string encounterInstanceId,
            out CombatStateService combatState,
            out string error)
        {
            combatState = null;
            CombatStateService loadedCombatState = null;
            if (!TryExecute(connection =>
                {
                    EnsureNormalizedSchema(connection);

                    var normalized = TryLoadNormalizedEncounter(connection, encounterInstanceId);
                    if (normalized != null)
                    {
                        loadedCombatState = CombatStateService.RestoreFromPersistenceSnapshot(normalized);
                        return;
                    }

                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "SELECT snapshot_json FROM encounter_instances WHERE encounter_instance_id = @instanceId LIMIT 1;";
                    AddParameter(command, "@instanceId", encounterInstanceId);
                    var json = command.ExecuteScalar() as string;
                    if (string.IsNullOrEmpty(json))
                    {
                        throw new InvalidOperationException($"No persisted encounter found for '{encounterInstanceId}'.");
                    }

                    var snapshot = JsonUtility.FromJson<CombatPersistenceSnapshot>(json);
                    loadedCombatState = CombatStateService.RestoreFromPersistenceSnapshot(snapshot);
                }, out error))
            {
                return false;
            }

            combatState = loadedCombatState;
            return true;
        }

        public bool TryGetNormalizedCounts(
            string encounterInstanceId,
            out int sessionPlayerCount,
            out int combatantCount,
            out int actionCount,
            out string error)
        {
            sessionPlayerCount = 0;
            combatantCount = 0;
            actionCount = 0;

            var localSessionPlayerCount = 0;
            var localCombatantCount = 0;
            var localActionCount = 0;

            if (!TryExecute(connection =>
            {
                EnsureNormalizedSchema(connection);

                using (var sessionPlayers = connection.CreateCommand())
                {
                    sessionPlayers.CommandText = "SELECT COUNT(*) FROM session_players;";
                    localSessionPlayerCount = Convert.ToInt32(sessionPlayers.ExecuteScalar());
                }

                using (var combatants = connection.CreateCommand())
                {
                    combatants.CommandText = "SELECT COUNT(*) FROM encounter_combatants WHERE encounter_instance_id = @instanceId;";
                    AddParameter(combatants, "@instanceId", encounterInstanceId);
                    localCombatantCount = Convert.ToInt32(combatants.ExecuteScalar());
                }

                using (var actions = connection.CreateCommand())
                {
                    actions.CommandText = "SELECT COUNT(*) FROM encounter_actions WHERE encounter_instance_id = @instanceId;";
                    AddParameter(actions, "@instanceId", encounterInstanceId);
                    localActionCount = Convert.ToInt32(actions.ExecuteScalar());
                }
            }, out error))
            {
                return false;
            }

            sessionPlayerCount = localSessionPlayerCount;
            combatantCount = localCombatantCount;
            actionCount = localActionCount;
            return true;
        }

        public bool TrySaveCampaignState(
            SessionService session,
            string encounterInstanceId,
            string encounterId,
            string areaId,
            CombatStateService combatState,
            out string error)
        {
            if (!TrySaveSession(session, out error))
            {
                return false;
            }

            if (combatState == null)
            {
                error = string.Empty;
                return true;
            }

            return TrySaveEncounterState(encounterInstanceId, encounterId, areaId, combatState, out error);
        }

        public bool TryLoadCampaignState(
            string encounterInstanceId,
            out SessionService session,
            out CombatStateService combatState,
            out string error)
        {
            session = null;
            combatState = null;

            if (!TryLoadSession(out session, out error))
            {
                return false;
            }

            if (string.IsNullOrEmpty(encounterInstanceId))
            {
                error = string.Empty;
                return true;
            }

            return TryLoadEncounterState(encounterInstanceId, out combatState, out error);
        }

        public bool TrySetWorldState(string key, string value, out string error)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                error = "World state key is empty.";
                return false;
            }

            return TryExecute(connection =>
            {
                EnsureNormalizedSchema(connection);
                using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT OR REPLACE INTO world_state_kv(scope, state_key, state_value, updated_utc) VALUES(@scope, @key, @value, @updatedUtc);";
                AddParameter(command, "@scope", WorldStateScope);
                AddParameter(command, "@key", key);
                AddParameter(command, "@value", value ?? string.Empty);
                AddParameter(command, "@updatedUtc", GetUtcNowIso8601());
                command.ExecuteNonQuery();
            }, out error);
        }

        public bool TryGetWorldState(string key, out string value, out string error)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                error = "World state key is empty.";
                return false;
            }

            string loadedValue = null;
            if (!TryExecute(connection =>
            {
                EnsureNormalizedSchema(connection);
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT state_value FROM world_state_kv WHERE scope = @scope AND state_key = @key LIMIT 1;";
                AddParameter(command, "@scope", WorldStateScope);
                AddParameter(command, "@key", key);
                loadedValue = command.ExecuteScalar() as string;
            }, out error))
            {
                return false;
            }

            value = loadedValue ?? string.Empty;
            return true;
        }

        private bool TryExecute(Action<IDbConnection> operation, out string error)
        {
            if (operation == null)
            {
                error = "No database operation was provided.";
                return false;
            }

            if (string.IsNullOrEmpty(databasePath))
            {
                error = "Campaign database path is empty.";
                return false;
            }

            if (!File.Exists(databasePath))
            {
                error = $"Campaign database was not found at '{databasePath}'.";
                return false;
            }

            try
            {
                using var connection = CreateSqliteConnection(databasePath);
                connection.Open();
                operation(connection);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static IDbConnection CreateSqliteConnection(string path)
        {
            var connectionType = Type.GetType("Mono.Data.Sqlite.SqliteConnection, Mono.Data.Sqlite");
            if (connectionType == null)
            {
                throw new InvalidOperationException(
                    "Mono.Data.Sqlite provider assembly is unavailable. Add a reference to Mono.Data.Sqlite in Unity.");
            }

            if (!typeof(IDbConnection).IsAssignableFrom(connectionType))
            {
                throw new InvalidOperationException("Mono.Data.Sqlite.SqliteConnection does not implement IDbConnection.");
            }

            return (IDbConnection)Activator.CreateInstance(connectionType, $"URI=file:{path}");
        }

        private static void EnsureNormalizedSchema(IDbConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
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
    assigned_character_id TEXT,
    joined_at_utc INTEGER NOT NULL,
    is_connected INTEGER NOT NULL,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (session_id, player_id)
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
    PRIMARY KEY (encounter_instance_id, combatant_id)
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
    PRIMARY KEY (encounter_instance_id, action_id)
);";
            command.ExecuteNonQuery();

            EnsureColumnExists(connection, "session_players", "assigned_character_id", "TEXT");
            EnsureWorldStateSchema(connection);
        }

        private static void EnsureWorldStateSchema(IDbConnection connection)
        {
            if (!TableExists(connection, "world_state_kv"))
            {
                using var create = connection.CreateCommand();
                create.CommandText = @"
CREATE TABLE world_state_kv (
    scope TEXT NOT NULL,
    state_key TEXT NOT NULL,
    state_value TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (scope, state_key)
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_world_state_kv_scope_key ON world_state_kv(scope, state_key);";
                create.ExecuteNonQuery();
                return;
            }

            EnsureColumnExists(connection, "world_state_kv", "scope", "TEXT NOT NULL DEFAULT 'world'");

            using (var backfill = connection.CreateCommand())
            {
                backfill.CommandText = "UPDATE world_state_kv SET scope = @scope WHERE scope IS NULL OR scope = '';";
                AddParameter(backfill, "@scope", WorldStateScope);
                backfill.ExecuteNonQuery();
            }

            using var index = connection.CreateCommand();
            index.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_world_state_kv_scope_key ON world_state_kv(scope, state_key);";
            index.ExecuteNonQuery();
        }

        private static bool TableExists(IDbConnection connection, string tableName)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = @tableName;";
            AddParameter(command, "@tableName", tableName);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        private static void EnsureColumnExists(
            IDbConnection connection,
            string tableName,
            string columnName,
            string columnDefinition)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
            alter.ExecuteNonQuery();
        }

        private static void SaveNormalizedSession(IDbConnection connection, SessionPersistenceSnapshot snapshot)
        {
            using var transaction = connection.BeginTransaction();

            using (var deletePlayers = connection.CreateCommand())
            {
                deletePlayers.Transaction = transaction;
                deletePlayers.CommandText = "DELETE FROM session_players WHERE session_id = @sessionId;";
                AddParameter(deletePlayers, "@sessionId", snapshot.SessionId);
                deletePlayers.ExecuteNonQuery();
            }

            using (var upsertSession = connection.CreateCommand())
            {
                upsertSession.Transaction = transaction;
                upsertSession.CommandText =
                    "INSERT OR REPLACE INTO active_sessions(session_id, world_project_id, state, gm_player_id, updated_utc) " +
                    "VALUES(@sessionId, @worldProjectId, @state, @gmPlayerId, @updatedUtc);";
                AddParameter(upsertSession, "@sessionId", snapshot.SessionId);
                AddParameter(upsertSession, "@worldProjectId", snapshot.WorldProjectId);
                AddParameter(upsertSession, "@state", (int)snapshot.State);
                AddParameter(upsertSession, "@gmPlayerId", snapshot.GMPlayerId);
                AddParameter(upsertSession, "@updatedUtc", GetUtcNowIso8601());
                upsertSession.ExecuteNonQuery();
            }

            for (var i = 0; i < snapshot.Players.Count; i++)
            {
                var player = snapshot.Players[i];
                using var insertPlayer = connection.CreateCommand();
                insertPlayer.Transaction = transaction;
                insertPlayer.CommandText =
                    "INSERT OR REPLACE INTO session_players(session_id, player_id, display_name, network_client_id, role, assigned_combatant_id, assigned_character_id, joined_at_utc, is_connected, updated_utc) " +
                    "VALUES(@sessionId, @playerId, @displayName, @networkClientId, @role, @assignedCombatantId, @assignedCharacterId, @joinedAtUtc, @isConnected, @updatedUtc);";
                AddParameter(insertPlayer, "@sessionId", snapshot.SessionId);
                AddParameter(insertPlayer, "@playerId", player.PlayerId);
                AddParameter(insertPlayer, "@displayName", player.DisplayName);
                AddParameter(insertPlayer, "@networkClientId", (long)player.NetworkClientId);
                AddParameter(insertPlayer, "@role", (int)player.Role);
                AddParameter(insertPlayer, "@assignedCombatantId", player.AssignedCombatantId);
                AddParameter(insertPlayer, "@assignedCharacterId", player.AssignedCharacterId);
                AddParameter(insertPlayer, "@joinedAtUtc", player.JoinedAtUtc);
                AddParameter(insertPlayer, "@isConnected", player.IsConnected ? 1 : 0);
                AddParameter(insertPlayer, "@updatedUtc", GetUtcNowIso8601());
                insertPlayer.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        private static SessionPersistenceSnapshot TryLoadNormalizedSession(IDbConnection connection)
        {
            using var sessionCommand = connection.CreateCommand();
            sessionCommand.CommandText =
                "SELECT session_id, world_project_id, state, gm_player_id FROM active_sessions ORDER BY updated_utc DESC LIMIT 1;";
            using var reader = sessionCommand.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            var snapshot = new SessionPersistenceSnapshot
            {
                SessionId = reader.GetString(0),
                WorldProjectId = reader.GetString(1),
                State = (SessionState)reader.GetInt32(2),
                GMPlayerId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3)
            };

            using var playersCommand = connection.CreateCommand();
            playersCommand.CommandText =
                "SELECT player_id, display_name, network_client_id, role, assigned_combatant_id, assigned_character_id, joined_at_utc, is_connected " +
                "FROM session_players WHERE session_id = @sessionId ORDER BY joined_at_utc ASC;";
            AddParameter(playersCommand, "@sessionId", snapshot.SessionId);
            using var playerReader = playersCommand.ExecuteReader();
            while (playerReader.Read())
            {
                snapshot.Players.Add(new PlayerSessionRecord
                {
                    PlayerId = playerReader.GetString(0),
                    DisplayName = playerReader.GetString(1),
                    NetworkClientId = Convert.ToUInt64(playerReader.GetInt64(2)),
                    Role = (PlayerRole)playerReader.GetInt32(3),
                    AssignedCombatantId = playerReader.IsDBNull(4) ? string.Empty : playerReader.GetString(4),
                    AssignedCharacterId = playerReader.IsDBNull(5) ? string.Empty : playerReader.GetString(5),
                    JoinedAtUtc = playerReader.GetInt64(6),
                    IsConnected = playerReader.GetInt32(7) != 0,
                });
            }

            return snapshot;
        }

        private static void SaveNormalizedEncounter(IDbConnection connection, string encounterInstanceId, CombatPersistenceSnapshot snapshot)
        {
            using var transaction = connection.BeginTransaction();

            using (var deleteCombatants = connection.CreateCommand())
            {
                deleteCombatants.Transaction = transaction;
                deleteCombatants.CommandText = "DELETE FROM encounter_combatants WHERE encounter_instance_id = @instanceId;";
                AddParameter(deleteCombatants, "@instanceId", encounterInstanceId);
                deleteCombatants.ExecuteNonQuery();
            }

            using (var deleteActions = connection.CreateCommand())
            {
                deleteActions.Transaction = transaction;
                deleteActions.CommandText = "DELETE FROM encounter_actions WHERE encounter_instance_id = @instanceId;";
                AddParameter(deleteActions, "@instanceId", encounterInstanceId);
                deleteActions.ExecuteNonQuery();
            }

            for (var i = 0; i < snapshot.Combatants.Count; i++)
            {
                var combatant = snapshot.Combatants[i];
                using var insertCombatant = connection.CreateCommand();
                insertCombatant.Transaction = transaction;
                insertCombatant.CommandText =
                    "INSERT OR REPLACE INTO encounter_combatants(encounter_instance_id, combatant_id, actor_name, character_id, initiative_roll, turn_order, hit_points_current, hit_points_max, armor_class, armor_class_touch, armor_class_flat_footed, has_taken_turn, is_defeated, is_stunned, standard_actions_remaining, move_actions_remaining, swift_actions_remaining, free_actions_remaining, active_conditions_json, updated_utc) " +
                    "VALUES(@instanceId, @combatantId, @actorName, @characterId, @initiativeRoll, @turnOrder, @hitPointsCurrent, @hitPointsMax, @armorClass, @armorClassTouch, @armorClassFlatFooted, @hasTakenTurn, @isDefeated, @isStunned, @standardActionsRemaining, @moveActionsRemaining, @swiftActionsRemaining, @freeActionsRemaining, @conditionsJson, @updatedUtc);";
                AddParameter(insertCombatant, "@instanceId", encounterInstanceId);
                AddParameter(insertCombatant, "@combatantId", combatant.CombatantId);
                AddParameter(insertCombatant, "@actorName", combatant.ActorName);
                AddParameter(insertCombatant, "@characterId", combatant.CharacterId);
                AddParameter(insertCombatant, "@initiativeRoll", combatant.InitiativeRoll);
                AddParameter(insertCombatant, "@turnOrder", combatant.TurnOrder);
                AddParameter(insertCombatant, "@hitPointsCurrent", combatant.HitPointsCurrent);
                AddParameter(insertCombatant, "@hitPointsMax", combatant.HitPointsMax);
                AddParameter(insertCombatant, "@armorClass", combatant.ArmorClass);
                AddParameter(insertCombatant, "@armorClassTouch", combatant.ArmorClassTouch);
                AddParameter(insertCombatant, "@armorClassFlatFooted", combatant.ArmorClassFlatFooted);
                AddParameter(insertCombatant, "@hasTakenTurn", combatant.HasTakenTurn ? 1 : 0);
                AddParameter(insertCombatant, "@isDefeated", combatant.IsDefeated ? 1 : 0);
                AddParameter(insertCombatant, "@isStunned", combatant.IsStunned ? 1 : 0);
                AddParameter(insertCombatant, "@standardActionsRemaining", combatant.StandardActionsRemaining);
                AddParameter(insertCombatant, "@moveActionsRemaining", combatant.MoveActionsRemaining);
                AddParameter(insertCombatant, "@swiftActionsRemaining", combatant.SwiftActionsRemaining);
                AddParameter(insertCombatant, "@freeActionsRemaining", combatant.FreeActionsRemaining);
                AddParameter(insertCombatant, "@conditionsJson", JsonUtility.ToJson(new StringListWrapper { Values = combatant.ActiveConditions ?? new List<string>() }));
                AddParameter(insertCombatant, "@updatedUtc", GetUtcNowIso8601());
                insertCombatant.ExecuteNonQuery();
            }

            for (var i = 0; i < snapshot.AllActions.Count; i++)
            {
                var action = snapshot.AllActions[i];
                var isPending = snapshot.PendingActions.Exists(p => p.ActionId == action.ActionId);
                using var insertAction = connection.CreateCommand();
                insertAction.Transaction = transaction;
                insertAction.CommandText =
                    "INSERT OR REPLACE INTO encounter_actions(encounter_instance_id, action_id, combatant_id, target_combatant_id, round_number, turn_number, action_type, action_name, action_description, is_resolved, succeeded, resolution_result, timestamp_utc, is_pending, updated_utc) " +
                    "VALUES(@instanceId, @actionId, @combatantId, @targetCombatantId, @roundNumber, @turnNumber, @actionType, @actionName, @actionDescription, @isResolved, @succeeded, @resolutionResult, @timestampUtc, @isPending, @updatedUtc);";
                AddParameter(insertAction, "@instanceId", encounterInstanceId);
                AddParameter(insertAction, "@actionId", action.ActionId);
                AddParameter(insertAction, "@combatantId", action.CombatantId);
                AddParameter(insertAction, "@targetCombatantId", action.TargetCombatantId);
                AddParameter(insertAction, "@roundNumber", action.RoundNumber);
                AddParameter(insertAction, "@turnNumber", action.TurnNumber);
                AddParameter(insertAction, "@actionType", (int)action.ActionType);
                AddParameter(insertAction, "@actionName", action.ActionName);
                AddParameter(insertAction, "@actionDescription", action.ActionDescription);
                AddParameter(insertAction, "@isResolved", action.IsResolved ? 1 : 0);
                AddParameter(insertAction, "@succeeded", action.Succeeded ? 1 : 0);
                AddParameter(insertAction, "@resolutionResult", action.ResolutionResult);
                AddParameter(insertAction, "@timestampUtc", action.TimestampUtc);
                AddParameter(insertAction, "@isPending", isPending ? 1 : 0);
                AddParameter(insertAction, "@updatedUtc", GetUtcNowIso8601());
                insertAction.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        private static CombatPersistenceSnapshot TryLoadNormalizedEncounter(IDbConnection connection, string encounterInstanceId)
        {
            using var encounterCommand = connection.CreateCommand();
            encounterCommand.CommandText =
                "SELECT encounter_id, round_number, active_combatant_id, state, snapshot_json FROM encounter_instances WHERE encounter_instance_id = @instanceId LIMIT 1;";
            AddParameter(encounterCommand, "@instanceId", encounterInstanceId);
            using var encounterReader = encounterCommand.ExecuteReader();
            if (!encounterReader.Read())
            {
                return null;
            }

            using var combatantsCommand = connection.CreateCommand();
            combatantsCommand.CommandText =
                "SELECT combatant_id, actor_name, character_id, initiative_roll, turn_order, hit_points_current, hit_points_max, armor_class, armor_class_touch, armor_class_flat_footed, has_taken_turn, is_defeated, is_stunned, standard_actions_remaining, move_actions_remaining, swift_actions_remaining, free_actions_remaining, active_conditions_json " +
                "FROM encounter_combatants WHERE encounter_instance_id = @instanceId ORDER BY turn_order ASC;";
            AddParameter(combatantsCommand, "@instanceId", encounterInstanceId);
            using var combatantReader = combatantsCommand.ExecuteReader();
            var combatants = new List<Combatant>();
            while (combatantReader.Read())
            {
                var conditionsJson = combatantReader.IsDBNull(17) ? string.Empty : combatantReader.GetString(17);
                var conditionWrapper = string.IsNullOrEmpty(conditionsJson)
                    ? new StringListWrapper()
                    : JsonUtility.FromJson<StringListWrapper>(conditionsJson) ?? new StringListWrapper();
                combatants.Add(new Combatant
                {
                    CombatantId = combatantReader.GetString(0),
                    ActorName = combatantReader.GetString(1),
                    CharacterId = combatantReader.IsDBNull(2) ? string.Empty : combatantReader.GetString(2),
                    InitiativeRoll = combatantReader.GetInt32(3),
                    TurnOrder = combatantReader.GetInt32(4),
                    HitPointsCurrent = combatantReader.GetInt32(5),
                    HitPointsMax = combatantReader.GetInt32(6),
                    ArmorClass = combatantReader.GetInt32(7),
                    ArmorClassTouch = combatantReader.GetInt32(8),
                    ArmorClassFlatFooted = combatantReader.GetInt32(9),
                    HasTakenTurn = combatantReader.GetInt32(10) != 0,
                    IsDefeated = combatantReader.GetInt32(11) != 0,
                    IsStunned = combatantReader.GetInt32(12) != 0,
                    StandardActionsRemaining = combatantReader.GetInt32(13),
                    MoveActionsRemaining = combatantReader.GetInt32(14),
                    SwiftActionsRemaining = combatantReader.GetInt32(15),
                    FreeActionsRemaining = combatantReader.GetInt32(16),
                    ActiveConditions = conditionWrapper.Values ?? new List<string>()
                });
            }

            if (combatants.Count == 0)
            {
                return null;
            }

            var legacySnapshot = encounterReader.IsDBNull(4)
                ? null
                : JsonUtility.FromJson<CombatPersistenceSnapshot>(encounterReader.GetString(4));

            using var actionsCommand = connection.CreateCommand();
            actionsCommand.CommandText =
                "SELECT action_id, combatant_id, target_combatant_id, round_number, turn_number, action_type, action_name, action_description, is_resolved, succeeded, resolution_result, timestamp_utc, is_pending " +
                "FROM encounter_actions WHERE encounter_instance_id = @instanceId ORDER BY timestamp_utc ASC, action_id ASC;";
            AddParameter(actionsCommand, "@instanceId", encounterInstanceId);
            using var actionReader = actionsCommand.ExecuteReader();
            var allActions = new List<TurnAction>();
            var pendingActions = new List<TurnAction>();
            var actionHistory = new List<TurnAction>();
            while (actionReader.Read())
            {
                var action = new TurnAction
                {
                    ActionId = actionReader.GetString(0),
                    CombatantId = actionReader.IsDBNull(1) ? string.Empty : actionReader.GetString(1),
                    TargetCombatantId = actionReader.IsDBNull(2) ? string.Empty : actionReader.GetString(2),
                    RoundNumber = actionReader.GetInt32(3),
                    TurnNumber = actionReader.GetInt32(4),
                    ActionType = (TurnActionType)actionReader.GetInt32(5),
                    ActionName = actionReader.GetString(6),
                    ActionDescription = actionReader.IsDBNull(7) ? string.Empty : actionReader.GetString(7),
                    IsResolved = actionReader.GetInt32(8) != 0,
                    Succeeded = actionReader.GetInt32(9) != 0,
                    ResolutionResult = actionReader.IsDBNull(10) ? string.Empty : actionReader.GetString(10),
                    TimestampUtc = actionReader.GetInt64(11),
                };
                allActions.Add(action);
                if (actionReader.GetInt32(12) != 0)
                {
                    pendingActions.Add(action);
                }
                if (action.IsResolved)
                {
                    actionHistory.Add(action);
                }
            }

            return new CombatPersistenceSnapshot
            {
                EncounterId = encounterReader.IsDBNull(0) ? string.Empty : encounterReader.GetString(0),
                CurrentRound = encounterReader.IsDBNull(1) ? 1 : encounterReader.GetInt32(1),
                CurrentCombatantId = encounterReader.IsDBNull(2) ? string.Empty : encounterReader.GetString(2),
                IsActive = !encounterReader.IsDBNull(3) && string.Equals(encounterReader.GetString(3), "Active", StringComparison.OrdinalIgnoreCase),
                CombatMode = legacySnapshot?.CombatMode ?? DeriveCombatModeFromActions(pendingActions),
                Combatants = combatants,
                AllActions = allActions,
                PendingActions = pendingActions,
                ActionHistory = actionHistory,
                GMId = legacySnapshot?.GMId ?? string.Empty
            };
        }

        private static CombatMode DeriveCombatModeFromActions(List<TurnAction> pendingActions)
        {
            return pendingActions != null && pendingActions.Count > 0
                ? CombatMode.GMGuided
                : CombatMode.PlayerTraining;
        }

        [Serializable]
        private sealed class StringListWrapper
        {
            public List<string> Values = new List<string>();
        }

        private static void UpsertMetadata(IDbConnection connection, string key, string value)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT OR REPLACE INTO campaign_metadata(key, value, updated_utc) VALUES(@key, @value, @updatedUtc);";
            AddParameter(command, "@key", key);
            AddParameter(command, "@value", value ?? string.Empty);
            AddParameter(command, "@updatedUtc", GetUtcNowIso8601());
            command.ExecuteNonQuery();
        }

        private static string ReadMetadata(IDbConnection connection, string key)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM campaign_metadata WHERE key = @key LIMIT 1;";
            AddParameter(command, "@key", key);
            return command.ExecuteScalar() as string;
        }

        private static void AddParameter(IDbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        private static string GetUtcNowIso8601()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }
    }
}