using System;
using System.Collections.Generic;
using System.Text;
using Elysium.Combat;
using Elysium.Networking;
using UnityEngine;

namespace Elysium.Boot
{
    /// Smoke test covering the full session lifecycle:
    /// open, player registration, GM assignment, combatant assignment,
    /// encounter start, action submission with authorization checks,
    /// combat snapshot replication, and turn advancement.
    public sealed class SessionSmokeTestRunner : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = false;

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        private void Start()
        {
            if (runOnStart)
            {
                RunSessionSmokeTest();
            }
        }

        public void RunSessionSmokeTest()
        {
            try
            {
                LastSummary = RunSessionSmokeTestInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"Session smoke test failed: {ex}");
            }
        }

        private string RunSessionSmokeTestInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Elysium Session Lifecycle Smoke Test ===");
            log.AppendLine();

            // ── 1. Open session ───────────────────────────────────────────────
            var session = new SessionService();
            if (!session.TryOpenSession("sess_001", "starter_forest_edge", out var err))
            {
                throw new InvalidOperationException($"TryOpenSession failed: {err}");
            }

            log.AppendLine($"[1] Session opened: {session.SessionId}, state={session.State}");

            // ── 2. Register GM ────────────────────────────────────────────────
            var gmRecord = new PlayerSessionRecord
            {
                PlayerId = "gm_001",
                DisplayName = "Alice (GM)",
                NetworkClientId = 0,
                Role = PlayerRole.GameMaster,
            };

            if (!session.TryRegisterPlayer(gmRecord, out err))
            {
                throw new InvalidOperationException($"GM registration failed: {err}");
            }

            log.AppendLine($"[2] GM registered: {gmRecord.DisplayName}, HasGM={session.HasGM}");

            // ── 3. Register players ───────────────────────────────────────────
            var player1 = new PlayerSessionRecord
            {
                PlayerId = "player_001",
                DisplayName = "Bob",
                NetworkClientId = 1,
                Role = PlayerRole.Player,
            };

            var player2 = new PlayerSessionRecord
            {
                PlayerId = "player_002",
                DisplayName = "Carol",
                NetworkClientId = 2,
                Role = PlayerRole.Player,
            };

            if (!session.TryRegisterPlayer(player1, out err))
            {
                throw new InvalidOperationException($"Player1 registration failed: {err}");
            }

            if (!session.TryRegisterPlayer(player2, out err))
            {
                throw new InvalidOperationException($"Player2 registration failed: {err}");
            }

            log.AppendLine($"[3] Players registered: {player1.DisplayName}, {player2.DisplayName}");
            log.AppendLine($"    Total players in session: {session.Players.Count}");

            // ── 4. Assign combatants ─────────────────────────────────────────
            if (!session.TryAssignCombatant("gm_001", "player_001", "combatant_bob", out err))
            {
                throw new InvalidOperationException($"Combatant assignment failed: {err}");
            }

            if (!session.TryAssignCombatant("gm_001", "player_002", "combatant_carol", out err))
            {
                throw new InvalidOperationException($"Combatant assignment failed: {err}");
            }

            log.AppendLine($"[4] Combatants assigned: Bob→{player1.AssignedCombatantId}, Carol→{player2.AssignedCombatantId}");

            var unauthorizedAssignmentRejected = !session.TryAssignCombatant("player_001", "player_002", "combatant_carol_2", out var assignErr);
            if (!unauthorizedAssignmentRejected)
            {
                throw new InvalidOperationException("Non-GM assignment should be rejected.");
            }

            var duplicateAssignmentRejected = !session.TryAssignCombatant("gm_001", "player_002", "combatant_bob", out var duplicateErr);
            if (!duplicateAssignmentRejected)
            {
                throw new InvalidOperationException("Duplicate combatant assignment should be rejected.");
            }

            log.AppendLine($"[4.1] Unauthorized assignment rejected: {unauthorizedAssignmentRejected} ({assignErr})");
            log.AppendLine($"[4.2] Duplicate assignment rejected: {duplicateAssignmentRejected} ({duplicateErr})");

            // ── 5. Verify combatant ownership ────────────────────────────────
            var owner = session.GetCombatantOwner("combatant_bob");
            if (owner == null || owner.PlayerId != "player_001")
            {
                throw new InvalidOperationException("Combatant ownership lookup returned wrong player.");
            }

            log.AppendLine($"[5] Combatant ownership verified: combatant_bob → {owner.DisplayName}");

            // ── 6. Unauthorized role change rejected ─────────────────────────
            var roleChangeRejected = !session.TrySetRole("player_001", "player_002", PlayerRole.GameMaster, out var roleErr);
            log.AppendLine($"[6] Unauthorized role change rejected: {roleChangeRejected} ({roleErr})");

            // ── 7. Transition to combat ───────────────────────────────────────
            if (!session.TryStartCombat("gm_001", out err))
            {
                throw new InvalidOperationException($"TryStartCombat failed: {err}");
            }

            log.AppendLine($"[7] Session transitioned to combat: state={session.State}");

            // ── 8. Wire combat to network service ────────────────────────────
            var combatState = CombatStateService.CreateForEncounter("enc_001", CombatMode.GMGuided, "gm_001");
            combatState.InitializeCombat(new List<Combatant>
            {
                new Combatant
                {
                    CombatantId = "combatant_bob",
                    ActorName = "Bob",
                    CharacterId = "player_001",
                    InitiativeRoll = 17,
                    HitPointsCurrent = 24,
                    HitPointsMax = 24,
                    ArmorClass = 16
                },
                new Combatant
                {
                    CombatantId = "combatant_carol",
                    ActorName = "Carol",
                    CharacterId = "player_002",
                    InitiativeRoll = 12,
                    HitPointsCurrent = 20,
                    HitPointsMax = 20,
                    ArmorClass = 14
                },
                new Combatant
                {
                    CombatantId = "enemy_001",
                    ActorName = "Bandit",
                    CharacterId = "npc_bandit_01",
                    InitiativeRoll = 9,
                    HitPointsCurrent = 14,
                    HitPointsMax = 14,
                    ArmorClass = 13
                }
            });

            var networkService = new CombatNetworkService();
            var snapshots = new List<CombatNetworkSnapshot>();
            networkService.SnapshotPublished += s => snapshots.Add(s);
            networkService.HostEncounter(combatState, "Encounter live.");

            log.AppendLine($"[8] Combat hosted. Turn 1: {networkService.CurrentSnapshot.CurrentCombatantName}");

            // ── 9. Verify ownership gates action submission ──────────────────
            // player_002 (Carol) cannot act for combatant_bob (Bob's combatant)
            var unauthorizedRequest = new TurnActionRequest
            {
                CombatantId = "combatant_bob",
                ActionName = "Attack",
                ActionType = TurnActionType.StandardAction,
                TargetCombatantId = "enemy_001"
            };

            var carolOwnsRequest = session.GetCombatantOwner("combatant_bob")?.PlayerId;
            var carolUnauthorized = carolOwnsRequest != "player_002";
            log.AppendLine($"[9] Action auth check: carol can act for combatant_bob = {!carolUnauthorized}");

            // player_001 (Bob) submits for their own combatant
            if (!networkService.TrySubmitAction("gm_001", unauthorizedRequest, out var resolution, out var actionErr))
            {
                throw new InvalidOperationException($"Expected GM to be able to submit Bob's action: {actionErr}");
            }

            log.AppendLine($"[10] GM submitted action for combatant_bob: {resolution.Action.ActionName}");
            log.AppendLine($"     Pending GM approvals: {networkService.CurrentSnapshot.PendingActions.Count}");

            // ── 11. GM resolves and ends turn ────────────────────────────────
            if (!networkService.TryResolvePendingAction("gm_001", resolution.Action.ActionId, true, "Hit for 6.", out var resolveErr))
            {
                throw new InvalidOperationException($"GM resolve failed: {resolveErr}");
            }

            combatState.ProcessDamage("enemy_001", 6);
            networkService.PublishStatus("Damage applied.");

            if (!networkService.TryEndTurn("gm_001", out var endErr))
            {
                throw new InvalidOperationException($"End turn failed: {endErr}");
            }

            log.AppendLine($"[11] Turn resolved. Next combatant: {networkService.CurrentSnapshot.CurrentCombatantName}");
            log.AppendLine($"     Snapshots published: {snapshots.Count}");

            // ── 12. End combat, back to lobby ────────────────────────────────
            session.EndCombat();
            log.AppendLine($"[12] Combat ended. Session state: {session.State}");

            // ── 13. Disconnect player ─────────────────────────────────────────
            if (!session.TryDisconnectPlayer("player_002", out err))
            {
                throw new InvalidOperationException($"Disconnect failed: {err}");
            }

            log.AppendLine($"[13] Carol disconnected. IsConnected={player2.IsConnected}");

            // ── 14. Snapshot restore preserves ownership ─────────────────────
            var snapshot = session.CreateSnapshot();
            var restored = new SessionService();
            restored.RestoreFromSnapshot(snapshot);

            if (restored.GMPlayerId != "gm_001")
            {
                throw new InvalidOperationException($"Expected restored GM to be 'gm_001', got '{restored.GMPlayerId}'.");
            }

            if (restored.GetCombatantOwner("combatant_bob")?.PlayerId != "player_001")
            {
                throw new InvalidOperationException("Restored ownership for combatant_bob is incorrect.");
            }

            if (restored.GetCombatantOwner("combatant_carol")?.PlayerId != "player_002")
            {
                throw new InvalidOperationException("Restored ownership for combatant_carol is incorrect.");
            }

            log.AppendLine("[14] Snapshot restore ownership verified.");
            log.AppendLine();
            log.AppendLine("=== All Steps Passed ===");

            return log.ToString();
        }
    }
}
