using System;
using System.Collections.Generic;
using System.Text;
using Elysium.Combat;
using Elysium.Networking;
using UnityEngine;

namespace Elysium.Boot
{
    /// Smoke test for host-authoritative combat synchronization without requiring live clients.
    public sealed class CombatNetworkingSmokeTestRunner : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = false;

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        private void Start()
        {
            if (runOnStart)
            {
                RunNetworkingSmokeTest();
            }
        }

        public void RunNetworkingSmokeTest()
        {
            try
            {
                LastSummary = RunNetworkingSmokeTestInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"Combat networking smoke test failed: {ex}");
            }
        }

        private string RunNetworkingSmokeTestInternal()
        {
            var log = new StringBuilder();
            var publishedSnapshots = new List<CombatNetworkSnapshot>();

            var combatState = CombatStateService.CreateForEncounter("enc_net_001", CombatMode.GMGuided, "gm_001");
            combatState.InitializeCombat(new List<Combatant>
            {
                new Combatant
                {
                    CombatantId = "party_001",
                    ActorName = "Aragorn",
                    CharacterId = "party_001",
                    InitiativeRoll = 19,
                    HitPointsCurrent = 28,
                    HitPointsMax = 28,
                    ArmorClass = 17
                },
                new Combatant
                {
                    CombatantId = "enemy_001",
                    ActorName = "Bandit Captain",
                    CharacterId = "enemy_001",
                    InitiativeRoll = 15,
                    HitPointsCurrent = 18,
                    HitPointsMax = 18,
                    ArmorClass = 15
                }
            });

            var networkService = new CombatNetworkService();
            networkService.SnapshotPublished += snapshot => publishedSnapshots.Add(snapshot);
            networkService.HostEncounter(combatState, "Hosted combat created.");

            if (publishedSnapshots.Count != 1)
            {
                throw new InvalidOperationException($"Expected 1 snapshot after hosting, got {publishedSnapshots.Count}.");
            }

            EnsurePendingCount(networkService, 0, "initial host state");

            log.AppendLine("=== Combat Networking Smoke Test ===");
            log.AppendLine($"Initial Current Combatant: {networkService.CurrentSnapshot.CurrentCombatantName}");
            log.AppendLine($"Initial Pending Actions: {networkService.CurrentSnapshot.PendingActions.Count}");

            var badRequest = new TurnActionRequest
            {
                CombatantId = "enemy_001",
                ActionName = "Illegal Attack",
                Description = "Wrong player tries to act.",
                ActionType = TurnActionType.StandardAction,
                TargetCombatantId = "party_001"
            };

            var rejected = networkService.TrySubmitAction("party_001", badRequest, out _, out var rejectionError);
            log.AppendLine($"Unauthorized request rejected: {!rejected} ({rejectionError})");
            if (rejected)
            {
                throw new InvalidOperationException("Unauthorized request should be rejected.");
            }

            EnsurePendingCount(networkService, 0, "after rejected action request");

            var validRequest = new TurnActionRequest
            {
                CombatantId = "party_001",
                ActionName = "Longsword Attack",
                Description = "Player attacks the captain.",
                ActionType = TurnActionType.StandardAction,
                TargetCombatantId = "enemy_001"
            };

            if (!networkService.TrySubmitAction("party_001", validRequest, out var resolution, out var actionError))
            {
                throw new InvalidOperationException($"Expected valid request to succeed: {actionError}");
            }

            log.AppendLine($"Action submitted: {resolution.Action.ActionName}");
            log.AppendLine($"Pending approvals after submit: {networkService.CurrentSnapshot.PendingActions.Count}");
            EnsurePendingCount(networkService, 1, "after valid action submission");

            if (!networkService.TryResolvePendingAction("gm_001", resolution.Action.ActionId, true, "Hit for 9 damage.", out var approvalError))
            {
                throw new InvalidOperationException($"Expected GM approval to succeed: {approvalError}");
            }

            EnsurePendingCount(networkService, 0, "after GM approval");

            combatState.ProcessDamage("enemy_001", 9);
            networkService.PublishStatus("Damage applied to enemy_001.");

            log.AppendLine($"Pending approvals after GM decision: {networkService.CurrentSnapshot.PendingActions.Count}");
            log.AppendLine($"Enemy HP after synchronized damage: {networkService.CurrentSnapshot.Combatants[1].HitPointsCurrent}");
            EnsurePendingCount(networkService, 0, "after damage publish");

            if (!networkService.TryEndTurn("party_001", out var endTurnError))
            {
                throw new InvalidOperationException($"Expected turn end to succeed: {endTurnError}");
            }

            EnsurePendingCount(networkService, 0, "after end turn");

            if (!string.Equals(networkService.CurrentSnapshot.CurrentCombatantId, "enemy_001", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Expected turn to advance to enemy_001, got '{networkService.CurrentSnapshot.CurrentCombatantId}'.");
            }

            EnsureSnapshotOrder(publishedSnapshots);

            log.AppendLine($"Next combatant: {networkService.CurrentSnapshot.CurrentCombatantName}");
            log.AppendLine($"Snapshots published: {publishedSnapshots.Count}");
            log.AppendLine($"Latest status: {networkService.CurrentSnapshot.StatusMessage}");
            log.AppendLine("=== Smoke Test Complete ===");

            return log.ToString();
        }

        private static void EnsurePendingCount(CombatNetworkService networkService, int expected, string phase)
        {
            var actual = networkService.CurrentSnapshot?.PendingActions?.Count ?? -1;
            if (actual != expected)
            {
                throw new InvalidOperationException(
                    $"Pending action count mismatch at {phase}: expected {expected}, got {actual}.");
            }
        }

        private static void EnsureSnapshotOrder(IReadOnlyList<CombatNetworkSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0)
            {
                throw new InvalidOperationException("No snapshots were published.");
            }

            var sawPendingStatus = false;
            var sawApprovalStatus = false;
            var sawAdvanceStatus = false;

            long lastPublishedAtUtc = long.MinValue;
            for (var i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                if (snapshot.PublishedAtUtc < lastPublishedAtUtc)
                {
                    throw new InvalidOperationException("Snapshot publish timestamps are not monotonic.");
                }

                lastPublishedAtUtc = snapshot.PublishedAtUtc;

                if (!sawPendingStatus && snapshot.StatusMessage.StartsWith("Pending GM approval", StringComparison.Ordinal))
                {
                    sawPendingStatus = true;
                    continue;
                }

                if (!sawApprovalStatus && snapshot.StatusMessage.StartsWith("GM approved action", StringComparison.Ordinal))
                {
                    if (!sawPendingStatus)
                    {
                        throw new InvalidOperationException("GM approval snapshot arrived before pending snapshot.");
                    }

                    sawApprovalStatus = true;
                    continue;
                }

                if (!sawAdvanceStatus && snapshot.StatusMessage.StartsWith("Turn advanced by", StringComparison.Ordinal))
                {
                    if (!sawApprovalStatus)
                    {
                        throw new InvalidOperationException("Turn advance snapshot arrived before approval snapshot.");
                    }

                    sawAdvanceStatus = true;
                }
            }

            if (!sawPendingStatus || !sawApprovalStatus || !sawAdvanceStatus)
            {
                throw new InvalidOperationException(
                    "Expected pending, approval, and turn-advance snapshot statuses were not all observed.");
            }
        }
    }
}