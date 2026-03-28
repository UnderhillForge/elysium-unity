using System;
using System.Collections.Generic;
using System.Text;
using Elysium.Networking;
using UnityEngine;

namespace Elysium.Boot
{
    /// Verifies authoritative exploration movement synchronization and combat lockout.
    public sealed class ExplorationSyncSmokeTestRunner : MonoBehaviour
    {
        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        public void RunExplorationSyncSmokeTest()
        {
            try
            {
                LastSummary = RunExplorationSyncSmokeTestInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"Exploration sync smoke test failed: {ex}");
            }
        }

        private string RunExplorationSyncSmokeTestInternal()
        {
            var log = new StringBuilder();
            var publishedSnapshots = new List<ExplorationNetworkSnapshot>();

            var session = new SessionService();
            var sync = new ExplorationSyncService();
            sync.SnapshotPublished += snapshot => publishedSnapshots.Add(snapshot);

            Require(session.TryOpenSession("exploration_session", "area_forest_edge", out var error), error);
            Require(session.TryRegisterPlayer(new PlayerSessionRecord
            {
                PlayerId = "gm_001",
                DisplayName = "GM",
                Role = PlayerRole.GameMaster,
                NetworkClientId = 0,
            }, out error), error);
            Require(session.TryRegisterPlayer(new PlayerSessionRecord
            {
                PlayerId = "player_001",
                DisplayName = "Alice",
                Role = PlayerRole.Player,
                NetworkClientId = 1,
            }, out error), error);
            Require(session.TryRegisterPlayer(new PlayerSessionRecord
            {
                PlayerId = "player_002",
                DisplayName = "Bob",
                Role = PlayerRole.Player,
                NetworkClientId = 2,
            }, out error), error);

            Require(session.TryAssignCharacter("gm_001", "player_001", "pc_ranger_001", out error), error);
            Require(session.TryAssignCharacter("gm_001", "player_002", "pc_cleric_001", out error), error);

            sync.HostArea("area_forest_edge", "Exploration area hosted.");
            if (!string.Equals(sync.CurrentSnapshot.AreaId, "area_forest_edge", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Hosted area did not publish correctly.");
            }

            log.AppendLine("=== Exploration Sync Smoke Test ===");
            log.AppendLine($"Hosted area: {sync.CurrentSnapshot.AreaId}");

            Require(sync.TryUpdateMovement(session, "player_001", "area_forest_edge", new Vector3(2f, 0f, 3f), 90f, out error), error);
            var player1State = sync.GetParticipant("player_001");
            if (player1State == null || player1State.Position != new Vector3(2f, 0f, 3f))
            {
                throw new InvalidOperationException("Player 1 movement state was not recorded correctly.");
            }

            log.AppendLine($"Player 1 moved to {player1State.Position} facing {player1State.FacingYaw} — ok");

            var wrongAreaRejected = !sync.TryUpdateMovement(session, "player_001", "wrong_area", new Vector3(1f, 0f, 1f), 0f, out var wrongAreaError);
            if (!wrongAreaRejected)
            {
                throw new InvalidOperationException("Wrong-area movement should be rejected.");
            }

            log.AppendLine($"Wrong-area movement rejected: {wrongAreaError} — ok");

            var unknownRejected = !sync.TryUpdateMovement(session, "ghost", "area_forest_edge", new Vector3(0f, 0f, 0f), 0f, out var unknownError);
            if (!unknownRejected)
            {
                throw new InvalidOperationException("Unknown-player movement should be rejected.");
            }

            log.AppendLine($"Unknown-player movement rejected: {unknownError} — ok");

            Require(session.TryStartCombat("gm_001", out error), error);
            var combatLocked = !sync.TryUpdateMovement(session, "player_002", "area_forest_edge", new Vector3(4f, 0f, 1f), 180f, out var combatLockError);
            if (!combatLocked)
            {
                throw new InvalidOperationException("Movement during combat should be rejected.");
            }

            log.AppendLine($"Combat lockout enforced: {combatLockError} — ok");

            session.EndCombat();
            Require(sync.TryUpdateMovement(session, "player_002", "area_forest_edge", new Vector3(4f, 0f, 1f), 180f, out error), error);
            var player2State = sync.GetParticipant("player_002");
            if (player2State == null || player2State.Position != new Vector3(4f, 0f, 1f))
            {
                throw new InvalidOperationException("Player 2 movement after combat was not recorded correctly.");
            }

            if (publishedSnapshots.Count < 5)
            {
                throw new InvalidOperationException($"Expected at least 5 snapshots, got {publishedSnapshots.Count}.");
            }

            EnsureSnapshotOrder(publishedSnapshots);

            log.AppendLine($"Player 2 moved after combat to {player2State.Position} — ok");
            log.AppendLine($"Snapshots published: {publishedSnapshots.Count}");
            log.AppendLine($"Latest status: {sync.CurrentSnapshot.StatusMessage}");
            log.AppendLine("=== Exploration Sync Smoke Test COMPLETE ===");
            return log.ToString();
        }

        private static void EnsureSnapshotOrder(IReadOnlyList<ExplorationNetworkSnapshot> snapshots)
        {
            long lastPublishedAtUtc = long.MinValue;
            for (var i = 0; i < snapshots.Count; i++)
            {
                if (snapshots[i].PublishedAtUtc < lastPublishedAtUtc)
                {
                    throw new InvalidOperationException("Exploration snapshot timestamps are not monotonic.");
                }

                lastPublishedAtUtc = snapshots[i].PublishedAtUtc;
            }
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