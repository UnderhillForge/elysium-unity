using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using Elysium.Combat;
using Elysium.Networking;
using Elysium.Persistence;
using UnityEngine;

namespace Elysium.Boot
{
    /// Verifies end-to-end save/load of session and combat state using campaign.db.
    public sealed class CampaignPersistenceSmokeTestRunner : MonoBehaviour
    {
        private const string SchemaSentinelMetadataKey = "smoke.schema.sentinel";
        private const string SchemaSentinelMetadataValue = "do_not_delete";
        private const string SchemaSentinelEncounterInstanceId = "smoke_legacy_encounter_sentinel";
        private const string SchemaSentinelEncounterId = "legacy_sentinel_encounter";
        private const string SchemaSentinelAreaId = "legacy_sentinel_area";

        [SerializeField] private bool runOnStart = false;
        [SerializeField] private string worldProjectFolder = "starter_forest_edge";
        [SerializeField] private string encounterInstanceId = "smoke_encounter_instance";
        [SerializeField] private string areaId = "area_forest_edge_01";

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        private void Start()
        {
            if (runOnStart)
            {
                RunPersistenceSmokeTest();
            }
        }

        public void RunPersistenceSmokeTest()
        {
            try
            {
                LastSummary = RunPersistenceSmokeTestInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"Campaign persistence smoke test failed: {ex}");
            }
        }

        private string RunPersistenceSmokeTestInternal()
        {
            var databasePath = Path.Combine(
                Application.streamingAssetsPath,
                "WorldProjects",
                worldProjectFolder,
                "Databases",
                "campaign.db");

            var persistence = new CampaignPersistenceService(databasePath);
            var log = new StringBuilder();
            log.AppendLine("=== Campaign Persistence Smoke Test ===");
            log.AppendLine($"Database: {databasePath}");

            SeedLegacySentinels(databasePath);
            log.AppendLine("[0] Seeded legacy sentinels for non-destructive schema verification.");

            var session = new SessionService();
            Require(session.TryOpenSession("persist_session_001", worldProjectFolder, out var error), error);
            Require(session.TryRegisterPlayer(new PlayerSessionRecord
            {
                PlayerId = "gm_001",
                DisplayName = "Alice (GM)",
                NetworkClientId = 0,
                Role = PlayerRole.GameMaster,
            }, out error), error);
            Require(session.TryRegisterPlayer(new PlayerSessionRecord
            {
                PlayerId = "player_001",
                DisplayName = "Bob",
                NetworkClientId = 1,
                Role = PlayerRole.Player,
            }, out error), error);
            Require(session.TryAssignCombatant("gm_001", "player_001", "combatant_bob", out error), error);
            Require(session.TryStartCombat("gm_001", out error), error);

            var combat = CombatStateService.CreateForEncounter("enc_persist_001", CombatMode.GMGuided, "gm_001");
            combat.InitializeCombat(new List<Combatant>
            {
                new Combatant
                {
                    CombatantId = "combatant_bob",
                    ActorName = "Bob",
                    CharacterId = "player_001",
                    InitiativeRoll = 18,
                    HitPointsCurrent = 22,
                    HitPointsMax = 22,
                    ArmorClass = 16
                },
                new Combatant
                {
                    CombatantId = "enemy_001",
                    ActorName = "Bandit Captain",
                    CharacterId = "npc_bandit_captain",
                    InitiativeRoll = 12,
                    HitPointsCurrent = 18,
                    HitPointsMax = 18,
                    ArmorClass = 15
                }
            });

            var pending = combat.AttemptAction(new TurnActionRequest
            {
                CombatantId = "combatant_bob",
                ActionName = "Longsword Attack",
                Description = "Player attacks before GM approval.",
                ActionType = TurnActionType.StandardAction,
                TargetCombatantId = "enemy_001"
            }, out error);
            Require(pending != null, error);

            var expectedSession = session.CreateSnapshot();
            var expectedCombat = combat.CreatePersistenceSnapshot();

            Require(persistence.TrySaveCampaignState(session, encounterInstanceId, combat.EncounterId, areaId, combat, out error), error);
            log.AppendLine("[1] Session and combat state saved.");

            Require(persistence.TryGetNormalizedCounts(encounterInstanceId, out var sessionPlayerCount, out var combatantCount, out var actionCount, out error), error);
            if (sessionPlayerCount < 2 || combatantCount != 2 || actionCount != 1)
            {
                throw new InvalidOperationException(
                    $"Normalized persistence counts were unexpected: players={sessionPlayerCount}, combatants={combatantCount}, actions={actionCount}.");
            }
            log.AppendLine($"[2] Normalized tables written: players={sessionPlayerCount}, combatants={combatantCount}, actions={actionCount}.");

            Require(persistence.TryLoadCampaignState(encounterInstanceId, out var loadedSession, out var loadedCombat, out error), error);
            log.AppendLine("[3] Session and combat state loaded.");

            AssertSessionEquivalent(expectedSession, loadedSession.CreateSnapshot());
            AssertCombatEquivalent(expectedCombat, loadedCombat?.CreatePersistenceSnapshot());
            log.AppendLine("[4] Round-trip equivalence validated for session and combat snapshots.");

            Require(persistence.TryGetNormalizedCounts(encounterInstanceId, out var loadedSessionPlayerCount, out var loadedCombatantCount, out var loadedActionCount, out error), error);
            if (loadedSessionPlayerCount != sessionPlayerCount
                || loadedCombatantCount != combatantCount
                || loadedActionCount != actionCount)
            {
                throw new InvalidOperationException(
                    $"Normalized counts changed unexpectedly after load: before=({sessionPlayerCount},{combatantCount},{actionCount}) after=({loadedSessionPlayerCount},{loadedCombatantCount},{loadedActionCount}).");
            }
            log.AppendLine("[5] Normalized counts remained stable after round-trip.");

            AssertLegacySentinelsIntact(databasePath);
            log.AppendLine("[6] Legacy schema sentinels intact (non-destructive runtime path).");

            log.AppendLine($"    Current turn: {loadedCombat.CurrentCombatant.ActorName}");
            log.AppendLine($"    Pending actions: {loadedCombat.PendingActions.Count}");
            log.AppendLine($"    Initiative display: {loadedCombat.GetInitiativeDisplay().Split('\n')[1]}");
            log.AppendLine("=== Smoke Test Complete ===");
            return log.ToString();
        }

        private static void Require(bool condition, string error)
        {
            if (!condition)
            {
                throw new InvalidOperationException(error);
            }
        }

        private static void AssertSessionEquivalent(SessionPersistenceSnapshot expected, SessionPersistenceSnapshot actual)
        {
            if (expected == null || actual == null)
            {
                throw new InvalidOperationException("Session round-trip equivalence failed: snapshot was null.");
            }

            if (!string.Equals(expected.SessionId, actual.SessionId, StringComparison.Ordinal)
                || !string.Equals(expected.WorldProjectId, actual.WorldProjectId, StringComparison.Ordinal)
                || expected.State != actual.State
                || !string.Equals(expected.GMPlayerId, actual.GMPlayerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Session round-trip equivalence failed: core session fields changed.");
            }

            if (expected.Players.Count != actual.Players.Count)
            {
                throw new InvalidOperationException(
                    $"Session round-trip equivalence failed: expected {expected.Players.Count} players, got {actual.Players.Count}.");
            }

            var expectedPlayers = expected.Players.OrderBy(p => p.PlayerId, StringComparer.Ordinal).ToArray();
            var actualPlayers = actual.Players.OrderBy(p => p.PlayerId, StringComparer.Ordinal).ToArray();
            for (var i = 0; i < expectedPlayers.Length; i++)
            {
                var e = expectedPlayers[i];
                var a = actualPlayers[i];
                if (!string.Equals(e.PlayerId, a.PlayerId, StringComparison.Ordinal)
                    || !string.Equals(e.DisplayName, a.DisplayName, StringComparison.Ordinal)
                    || e.NetworkClientId != a.NetworkClientId
                    || e.Role != a.Role
                    || !string.Equals(e.AssignedCombatantId, a.AssignedCombatantId, StringComparison.Ordinal)
                    || !string.Equals(e.AssignedCharacterId, a.AssignedCharacterId, StringComparison.Ordinal)
                    || e.JoinedAtUtc != a.JoinedAtUtc
                    || e.IsConnected != a.IsConnected)
                {
                    throw new InvalidOperationException(
                        $"Session round-trip equivalence failed for player '{e.PlayerId}'.");
                }
            }

            if (actual.Players.Count(p => p.IsGM) != 1)
            {
                throw new InvalidOperationException("Session round-trip equivalence failed: expected exactly one GM after restore.");
            }
        }

        private static void AssertCombatEquivalent(CombatPersistenceSnapshot expected, CombatPersistenceSnapshot actual)
        {
            if (expected == null || actual == null)
            {
                throw new InvalidOperationException("Combat round-trip equivalence failed: snapshot was null.");
            }

            if (!string.Equals(expected.EncounterId, actual.EncounterId, StringComparison.Ordinal)
                || expected.CombatMode != actual.CombatMode
                || !string.Equals(expected.GMId, actual.GMId, StringComparison.Ordinal)
                || expected.CurrentRound != actual.CurrentRound
                || !string.Equals(expected.CurrentCombatantId, actual.CurrentCombatantId, StringComparison.Ordinal)
                || expected.IsActive != actual.IsActive)
            {
                throw new InvalidOperationException("Combat round-trip equivalence failed: core encounter fields changed.");
            }

            AssertCombatantCollectionsEquivalent(expected.Combatants, actual.Combatants);
            AssertActionCollectionsEquivalent(expected.AllActions, actual.AllActions, "all actions");
            AssertActionCollectionsEquivalent(expected.PendingActions, actual.PendingActions, "pending actions");
            AssertActionCollectionsEquivalent(expected.ActionHistory, actual.ActionHistory, "action history");
        }

        private static void AssertCombatantCollectionsEquivalent(List<Combatant> expected, List<Combatant> actual)
        {
            expected ??= new List<Combatant>();
            actual ??= new List<Combatant>();

            if (expected.Count != actual.Count)
            {
                throw new InvalidOperationException(
                    $"Combat round-trip equivalence failed: expected {expected.Count} combatants, got {actual.Count}.");
            }

            var expectedOrdered = expected.OrderBy(c => c.CombatantId, StringComparer.Ordinal).ToArray();
            var actualOrdered = actual.OrderBy(c => c.CombatantId, StringComparer.Ordinal).ToArray();
            for (var i = 0; i < expectedOrdered.Length; i++)
            {
                var e = expectedOrdered[i];
                var a = actualOrdered[i];

                if (!string.Equals(e.CombatantId, a.CombatantId, StringComparison.Ordinal)
                    || !string.Equals(e.ActorName, a.ActorName, StringComparison.Ordinal)
                    || !string.Equals(e.CharacterId, a.CharacterId, StringComparison.Ordinal)
                    || e.InitiativeRoll != a.InitiativeRoll
                    || e.TurnOrder != a.TurnOrder
                    || e.HitPointsCurrent != a.HitPointsCurrent
                    || e.HitPointsMax != a.HitPointsMax
                    || e.ArmorClass != a.ArmorClass
                    || e.ArmorClassTouch != a.ArmorClassTouch
                    || e.ArmorClassFlatFooted != a.ArmorClassFlatFooted
                    || e.HasTakenTurn != a.HasTakenTurn
                    || e.IsDefeated != a.IsDefeated
                    || e.IsStunned != a.IsStunned
                    || e.StandardActionsRemaining != a.StandardActionsRemaining
                    || e.MoveActionsRemaining != a.MoveActionsRemaining
                    || e.SwiftActionsRemaining != a.SwiftActionsRemaining
                    || e.FreeActionsRemaining != a.FreeActionsRemaining)
                {
                    throw new InvalidOperationException(
                        $"Combat round-trip equivalence failed for combatant '{e.CombatantId}'.");
                }

                var expectedConditions = (e.ActiveConditions ?? new List<string>()).OrderBy(value => value, StringComparer.Ordinal);
                var actualConditions = (a.ActiveConditions ?? new List<string>()).OrderBy(value => value, StringComparer.Ordinal);
                if (!expectedConditions.SequenceEqual(actualConditions, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Combat round-trip equivalence failed: conditions changed for combatant '{e.CombatantId}'.");
                }
            }
        }

        private static void AssertActionCollectionsEquivalent(List<TurnAction> expected, List<TurnAction> actual, string label)
        {
            expected ??= new List<TurnAction>();
            actual ??= new List<TurnAction>();

            if (expected.Count != actual.Count)
            {
                throw new InvalidOperationException(
                    $"Combat round-trip equivalence failed: expected {expected.Count} {label}, got {actual.Count}.");
            }

            var expectedOrdered = expected.OrderBy(a => a.ActionId, StringComparer.Ordinal).ToArray();
            var actualOrdered = actual.OrderBy(a => a.ActionId, StringComparer.Ordinal).ToArray();
            for (var i = 0; i < expectedOrdered.Length; i++)
            {
                var e = expectedOrdered[i];
                var a = actualOrdered[i];

                if (!string.Equals(e.ActionId, a.ActionId, StringComparison.Ordinal)
                    || !string.Equals(e.CombatantId, a.CombatantId, StringComparison.Ordinal)
                    || !string.Equals(e.TargetCombatantId, a.TargetCombatantId, StringComparison.Ordinal)
                    || e.RoundNumber != a.RoundNumber
                    || e.TurnNumber != a.TurnNumber
                    || e.ActionType != a.ActionType
                    || !string.Equals(e.ActionName, a.ActionName, StringComparison.Ordinal)
                    || !string.Equals(e.ActionDescription, a.ActionDescription, StringComparison.Ordinal)
                    || e.IsResolved != a.IsResolved
                    || e.Succeeded != a.Succeeded
                    || !string.Equals(e.ResolutionResult, a.ResolutionResult, StringComparison.Ordinal)
                    || e.TimestampUtc != a.TimestampUtc)
                {
                    throw new InvalidOperationException(
                        $"Combat round-trip equivalence failed in {label} for action '{e.ActionId}'.");
                }
            }
        }

        private static void SeedLegacySentinels(string databasePath)
        {
            using var connection = CreateSqliteConnection(databasePath);
            connection.Open();

            using (var metadataCommand = connection.CreateCommand())
            {
                metadataCommand.CommandText =
                    "INSERT OR REPLACE INTO campaign_metadata(key, value, updated_utc) VALUES(@key, @value, @updatedUtc);";
                AddParameter(metadataCommand, "@key", SchemaSentinelMetadataKey);
                AddParameter(metadataCommand, "@value", SchemaSentinelMetadataValue);
                AddParameter(metadataCommand, "@updatedUtc", GetUtcNowIso8601());
                metadataCommand.ExecuteNonQuery();
            }

            using (var encounterCommand = connection.CreateCommand())
            {
                encounterCommand.CommandText =
                    "INSERT OR REPLACE INTO encounter_instances(encounter_instance_id, encounter_id, area_id, state, round_number, active_combatant_id, snapshot_json, updated_utc) " +
                    "VALUES(@instanceId, @encounterId, @areaId, @state, @roundNumber, @activeCombatantId, @snapshotJson, @updatedUtc);";
                AddParameter(encounterCommand, "@instanceId", SchemaSentinelEncounterInstanceId);
                AddParameter(encounterCommand, "@encounterId", SchemaSentinelEncounterId);
                AddParameter(encounterCommand, "@areaId", SchemaSentinelAreaId);
                AddParameter(encounterCommand, "@state", "Inactive");
                AddParameter(encounterCommand, "@roundNumber", 0);
                AddParameter(encounterCommand, "@activeCombatantId", string.Empty);
                AddParameter(encounterCommand, "@snapshotJson", "{}");
                AddParameter(encounterCommand, "@updatedUtc", GetUtcNowIso8601());
                encounterCommand.ExecuteNonQuery();
            }
        }

        private static void AssertLegacySentinelsIntact(string databasePath)
        {
            using var connection = CreateSqliteConnection(databasePath);
            connection.Open();

            using (var metadataCommand = connection.CreateCommand())
            {
                metadataCommand.CommandText =
                    "SELECT value FROM campaign_metadata WHERE key = @key LIMIT 1;";
                AddParameter(metadataCommand, "@key", SchemaSentinelMetadataKey);
                var value = metadataCommand.ExecuteScalar() as string;
                if (!string.Equals(value, SchemaSentinelMetadataValue, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Legacy schema sentinel metadata was removed or altered.");
                }
            }

            using (var encounterCommand = connection.CreateCommand())
            {
                encounterCommand.CommandText =
                    "SELECT encounter_id, area_id FROM encounter_instances WHERE encounter_instance_id = @instanceId LIMIT 1;";
                AddParameter(encounterCommand, "@instanceId", SchemaSentinelEncounterInstanceId);
                using var reader = encounterCommand.ExecuteReader();
                if (!reader.Read())
                {
                    throw new InvalidOperationException("Legacy schema sentinel encounter row was removed.");
                }

                var encounterId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var areaId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (!string.Equals(encounterId, SchemaSentinelEncounterId, StringComparison.Ordinal)
                    || !string.Equals(areaId, SchemaSentinelAreaId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Legacy schema sentinel encounter row was mutated unexpectedly.");
                }
            }
        }

        private static IDbConnection CreateSqliteConnection(string path)
        {
            var connectionType = Type.GetType("Mono.Data.Sqlite.SqliteConnection, Mono.Data.Sqlite");
            if (connectionType == null || !typeof(IDbConnection).IsAssignableFrom(connectionType))
            {
                throw new InvalidOperationException("Mono.Data.Sqlite provider assembly is unavailable.");
            }

            return (IDbConnection)Activator.CreateInstance(connectionType, $"URI=file:{path}");
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