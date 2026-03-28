using System;
using System.Collections.Generic;
using System.IO;
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

            if (loadedSession.State != SessionState.InCombat)
            {
                throw new InvalidOperationException($"Expected loaded session state InCombat, got {loadedSession.State}.");
            }

            if (loadedSession.GetCombatantOwner("combatant_bob")?.PlayerId != "player_001")
            {
                throw new InvalidOperationException("Loaded session lost combatant assignment.");
            }

            if (loadedCombat == null)
            {
                throw new InvalidOperationException("Loaded combat state was null.");
            }

            if (loadedCombat.CurrentCombatant?.CombatantId != "combatant_bob")
            {
                throw new InvalidOperationException("Loaded combat restored the wrong current combatant.");
            }

            if (loadedCombat.PendingActions.Count != 1)
            {
                throw new InvalidOperationException($"Expected 1 pending action after load, got {loadedCombat.PendingActions.Count}.");
            }

            var loadedPendingAction = loadedCombat.PendingActions[0];
            if (loadedPendingAction.ActionName != "Longsword Attack")
            {
                throw new InvalidOperationException("Loaded pending action did not match the saved action.");
            }

            log.AppendLine("[4] Loaded session state validated.");
            log.AppendLine("[5] Loaded combat state validated.");
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
    }
}