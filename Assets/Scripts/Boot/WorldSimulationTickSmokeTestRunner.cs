using System;
using System.IO;
using System.Text;
using Elysium.Networking;
using Elysium.Persistence;
using Elysium.World;
using UnityEngine;

namespace Elysium.Boot
{
    /// Verifies ambient world simulation ticks advance outside combat and persist to campaign.db.
    public sealed class WorldSimulationTickSmokeTestRunner : MonoBehaviour
    {
        private const string WorldProjectFolder = "starter_forest_edge";
        private const string SessionId = "simulation_tick_smoke";
        private const string GmId = "gm_001";

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        public void RunWorldSimulationTickSmokeTest()
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
                Debug.LogError($"World simulation tick smoke test failed: {ex}");
            }
        }

        private static string RunInternal()
        {
            var log = new StringBuilder();
            var campaignDatabasePath = Path.Combine(
                Application.streamingAssetsPath,
                "WorldProjects",
                WorldProjectFolder,
                "Databases",
                "campaign.db");

            var simulation = new WorldSimulationTickService(campaignDatabasePath);
            var persistence = new CampaignPersistenceService(campaignDatabasePath);
            var session = new SessionService();
            var baseline = ReadTickCount(persistence);

            Require(session.TryOpenSession(SessionId, WorldProjectFolder, out var error), error);

            Require(simulation.TryAdvanceTick(SessionState.Lobby, "area_forest_edge", out var firstTick, out error), error);
            Require(firstTick.Advanced, "Tick should advance in Lobby state.");
            Require(firstTick.TickCount == baseline + 1, "Tick counter did not increment after first tick.");

            Require(session.TryRegisterPlayer(new PlayerSessionRecord
            {
                PlayerId = GmId,
                DisplayName = "Smoke GM",
                Role = PlayerRole.GameMaster,
                NetworkClientId = 0,
            }, out error), error);

            Require(session.TryStartCombat(GmId, out error), error);
            Require(simulation.TryAdvanceTick(SessionState.InCombat, "area_forest_edge", out var combatTick, out error), error);
            Require(!combatTick.Advanced, "Tick should be skipped while session is InCombat.");
            Require(combatTick.TickCount == baseline + 1, "Combat skip should not change tick counter.");

            session.EndCombat();
            Require(simulation.TryAdvanceTick(SessionState.Lobby, "area_forest_edge", out var secondTick, out error), error);
            Require(secondTick.Advanced, "Tick should resume after combat ends.");
            Require(secondTick.TickCount == baseline + 2, "Tick counter did not increment after combat ended.");

            var persistedCount = ReadTickCount(persistence);
            if (persistedCount != baseline + 2)
            {
                throw new InvalidOperationException($"Persisted simulation tick mismatch. Expected {baseline + 2}, got {persistedCount}.");
            }

            Require(persistence.TryGetWorldState(WorldSimulationTickService.LastEventKey, out var lastEvent, out error), error);
            if (string.IsNullOrWhiteSpace(lastEvent))
            {
                throw new InvalidOperationException("Persisted simulation event key is empty.");
            }

            log.AppendLine("=== World Simulation Tick Smoke Test ===");
            log.AppendLine($"Baseline tick count: {baseline}");
            log.AppendLine($"First tick advanced to {firstTick.TickCount} in {firstTick.SessionStateName} — ok");
            log.AppendLine($"Combat tick skipped with count {combatTick.TickCount} — ok");
            log.AppendLine($"Second tick advanced to {secondTick.TickCount} with event '{secondTick.LastEvent}' — ok");
            log.AppendLine($"Persisted tick count verified: {persistedCount} — ok");
            log.AppendLine("=== World Simulation Tick Smoke Test COMPLETE ===");
            return log.ToString();
        }

        private static int ReadTickCount(CampaignPersistenceService persistence)
        {
            if (!persistence.TryGetWorldState(WorldSimulationTickService.TickCountKey, out var text, out var error))
            {
                throw new InvalidOperationException(error);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            if (!int.TryParse(text, out var value) || value < 0)
            {
                throw new InvalidOperationException($"Invalid persisted tick count: '{text}'.");
            }

            return value;
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