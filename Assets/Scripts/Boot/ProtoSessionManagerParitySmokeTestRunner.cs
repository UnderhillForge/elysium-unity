using System;
using System.Collections.Generic;
using System.Text;
using Elysium.Combat;
using Elysium.Networking;
using UnityEngine;

namespace Elysium.Boot
{
    /// Verifies the NGO-facing session manager authorization layer.
    ///
    /// This runner targets the same helper paths used by ElysiumSessionManager's
    /// ServerRpc entry points, so adapted protocols must satisfy the exact
    /// identity and authority rules enforced at the manager boundary.
    public sealed class ProtoSessionManagerParitySmokeTestRunner : MonoBehaviour
    {
        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        public void RunProtoSessionManagerParitySmokeTest()
        {
            try
            {
                LastSummary = RunInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"[ProtoSessionManagerParity] Smoke test failed: {ex}");
            }
        }

        private static string RunInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Proto Session Manager Parity Smoke Test ===");

            var session = new SessionService();
            var exploration = new ExplorationSyncService();
            var combat = new CombatNetworkService();

            Require(session.TryOpenSession("proto_mgr_001", "proto_village_square", out var error), error);

            // Host-side bootstrap GM.
            Require(session.TryRegisterPlayer(new PlayerSessionRecord
            {
                PlayerId = "gm_001",
                DisplayName = "GM",
                NetworkClientId = 0,
                Role = PlayerRole.GameMaster,
            }, out error), error);

            // Manager RPC-style join for players.
            Require(ElysiumSessionManager.TryRegisterPlayerFromClient(session, 1, "player_001", "Alice", out _, out error), error);
            Require(ElysiumSessionManager.TryRegisterPlayerFromClient(session, 2, "player_002", "Bob", out _, out error), error);
            log.AppendLine("Player join path via manager helper — ok");

            var duplicateClientRejected = !ElysiumSessionManager.TryRegisterPlayerFromClient(
                session, 1, "player_alias", "Alias", out _, out var duplicateClientErr);
            Require(duplicateClientRejected, "Duplicate client binding should be rejected.");
            log.AppendLine($"Duplicate client-player binding blocked: {duplicateClientErr} — ok");

            // Seed session assignments through authoritative host service.
            Require(session.TryAssignCharacter("gm_001", "player_001", "pc_proto_fighter", out error), error);
            Require(session.TryAssignCharacter("gm_001", "player_002", "pc_proto_cleric", out error), error);
            Require(session.TryAssignCombatant("gm_001", "player_001", "combatant_player_001", out error), error);
            Require(session.TryAssignCombatant("gm_001", "player_002", "combatant_player_002", out error), error);

            exploration.HostArea("area_proto_village", "Hosted for manager parity test.");
            Require(ElysiumSessionManager.TrySubmitExplorationMovementFromClient(
                session,
                exploration,
                1,
                "player_001",
                "area_proto_village",
                new Vector3(2f, 0f, 4f),
                90f,
                out error), error);
            log.AppendLine("Exploration movement accepted through manager helper — ok");

            var impersonationRejected = !ElysiumSessionManager.TrySubmitExplorationMovementFromClient(
                session,
                exploration,
                2,
                "player_001",
                "area_proto_village",
                new Vector3(7f, 0f, 7f),
                45f,
                out var impersonationErr);
            Require(impersonationRejected, "Movement impersonation should be rejected.");
            log.AppendLine($"Movement impersonation blocked: {impersonationErr} — ok");

            // Start combat and host encounter.
            Require(session.TryStartCombat("gm_001", out error), error);
            var combatState = CombatStateService.CreateForEncounter("enc_proto_mgr_001", CombatMode.GMGuided, "gm_001");
            combatState.InitializeCombat(new List<Combatant>
            {
                new Combatant
                {
                    CombatantId = "combatant_player_001",
                    ActorName = "Alice",
                    CharacterId = "pc_proto_fighter",
                    InitiativeRoll = 20,
                    HitPointsCurrent = 12,
                    HitPointsMax = 12,
                    ArmorClass = 17,
                },
                new Combatant
                {
                    CombatantId = "combatant_player_002",
                    ActorName = "Bob",
                    CharacterId = "pc_proto_cleric",
                    InitiativeRoll = 12,
                    HitPointsCurrent = 9,
                    HitPointsMax = 9,
                    ArmorClass = 16,
                },
                new Combatant
                {
                    CombatantId = "enemy_001",
                    ActorName = "Bandit",
                    CharacterId = "npc_bandit_01",
                    InitiativeRoll = 8,
                    HitPointsCurrent = 10,
                    HitPointsMax = 10,
                    ArmorClass = 13,
                }
            });
            combat.HostEncounter(combatState, "Hosted for session manager parity test.");

            // This is the critical regression guard: player_001 should be able to
            // act through the manager helper even though CombatNetworkService
            // authorizes by combatantId rather than playerId.
            var request = new TurnActionRequest
            {
                CombatantId = "combatant_player_001",
                ActionName = "Slash",
                Description = "Alice attacks bandit",
                ActionType = TurnActionType.StandardAction,
                TargetCombatantId = "enemy_001",
            };

            Require(ElysiumSessionManager.TrySubmitActionFromClient(
                session,
                combat,
                1,
                "player_001",
                request,
                out var resolution,
                out error), error);
            Require(resolution != null, "Expected non-null action resolution.");
            log.AppendLine("Player action accepted through manager helper — ok");

            var actionImpersonationRejected = !ElysiumSessionManager.TrySubmitActionFromClient(
                session,
                combat,
                2,
                "player_001",
                request,
                out _,
                out var actionImpersonationErr);
            Require(actionImpersonationRejected, "Action impersonation should be rejected.");
            log.AppendLine($"Action impersonation blocked: {actionImpersonationErr} — ok");

            var nongmResolveRejected = !ElysiumSessionManager.TryResolveActionFromClient(
                session,
                combat,
                1,
                "player_001",
                resolution.Action.ActionId,
                true,
                "Should fail",
                out var nongmResolveErr);
            Require(nongmResolveRejected, "Non-GM action resolution should be rejected.");
            log.AppendLine($"Non-GM resolve blocked: {nongmResolveErr} — ok");

            Require(ElysiumSessionManager.TryResolveActionFromClient(
                session,
                combat,
                0,
                "gm_001",
                resolution.Action.ActionId,
                true,
                "Hit confirmed",
                out error), error);
            log.AppendLine("GM resolution accepted through manager helper — ok");

            Require(ElysiumSessionManager.TryEndTurnFromClient(
                session,
                combat,
                1,
                "player_001",
                out error), error);
            log.AppendLine("Player end-turn accepted through manager helper — ok");

            var endTurnImpersonationRejected = !ElysiumSessionManager.TryEndTurnFromClient(
                session,
                combat,
                2,
                "player_001",
                out var endTurnErr);
            Require(endTurnImpersonationRejected, "End-turn impersonation should be rejected.");
            log.AppendLine($"End-turn impersonation blocked: {endTurnErr} — ok");

            var combatMovementRejected = !ElysiumSessionManager.TrySubmitExplorationMovementFromClient(
                session,
                exploration,
                2,
                "player_002",
                "area_proto_village",
                new Vector3(6f, 0f, -1f),
                180f,
                out var combatMoveErr);
            Require(combatMovementRejected, "Exploration movement during combat should be rejected.");
            log.AppendLine($"Combat movement lockout via manager helper: {combatMoveErr} — ok");

            log.AppendLine("=== Proto Session Manager Parity Smoke Test COMPLETE ===");
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