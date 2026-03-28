using System;
using Elysium.Networking;
using Elysium.Persistence;

namespace Elysium.World
{
    /// Advances lightweight ambient world simulation state outside of combat and persists it.
    public sealed class WorldSimulationTickService
    {
        public const string TickCountKey = "simulation.tick.count";
        public const string LastTickUtcKey = "simulation.tick.last_utc";
        public const string LastAreaIdKey = "simulation.tick.last_area";
        public const string LastEventKey = "simulation.tick.last_event";
        public const string LastSessionStateKey = "simulation.tick.last_session_state";

        private readonly CampaignPersistenceService persistence;

        public WorldSimulationTickService(string campaignDatabasePath)
        {
            persistence = new CampaignPersistenceService(campaignDatabasePath);
        }

        public bool TryAdvanceTick(
            SessionState sessionState,
            string areaId,
            out WorldSimulationTickSnapshot snapshot,
            out string error)
        {
            snapshot = new WorldSimulationTickSnapshot
            {
                LastAreaId = areaId ?? string.Empty,
                SessionStateName = sessionState.ToString()
            };

            var currentTick = 0;
            if (!TryReadTickCount(out currentTick, out error))
            {
                return false;
            }

            if (sessionState == SessionState.InCombat)
            {
                snapshot.Advanced = false;
                snapshot.TickCount = currentTick;
                snapshot.StatusMessage = "World simulation tick skipped while session is InCombat.";
                error = string.Empty;
                return true;
            }

            var nextTick = currentTick + 1;
            var nowUtc = DateTime.UtcNow.ToString("o");
            var resolvedArea = string.IsNullOrWhiteSpace(areaId) ? "unknown" : areaId;
            var worldEvent = BuildEventName(nextTick);

            if (!persistence.TrySetWorldState(TickCountKey, nextTick.ToString(), out error)
                || !persistence.TrySetWorldState(LastTickUtcKey, nowUtc, out error)
                || !persistence.TrySetWorldState(LastAreaIdKey, resolvedArea, out error)
                || !persistence.TrySetWorldState(LastEventKey, worldEvent, out error)
                || !persistence.TrySetWorldState(LastSessionStateKey, sessionState.ToString(), out error))
            {
                return false;
            }

            snapshot.Advanced = true;
            snapshot.TickCount = nextTick;
            snapshot.UpdatedUtc = nowUtc;
            snapshot.LastAreaId = resolvedArea;
            snapshot.LastEvent = worldEvent;
            snapshot.StatusMessage = "World simulation tick advanced.";
            error = string.Empty;
            return true;
        }

        private bool TryReadTickCount(out int tickCount, out string error)
        {
            tickCount = 0;
            if (!persistence.TryGetWorldState(TickCountKey, out var text, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                error = string.Empty;
                return true;
            }

            if (!int.TryParse(text, out tickCount) || tickCount < 0)
            {
                error = $"Persisted simulation tick count is invalid: '{text}'.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static string BuildEventName(int tickCount)
        {
            if (tickCount % 7 == 0)
            {
                return "npc_patrol_rotated";
            }

            if (tickCount % 5 == 0)
            {
                return "merchant_stock_refreshed";
            }

            if (tickCount % 3 == 0)
            {
                return "ambient_wildlife_shift";
            }

            return "idle_update";
        }
    }

    [Serializable]
    public sealed class WorldSimulationTickSnapshot
    {
        public bool Advanced;
        public int TickCount;
        public string UpdatedUtc = string.Empty;
        public string LastAreaId = string.Empty;
        public string LastEvent = string.Empty;
        public string SessionStateName = string.Empty;
        public string StatusMessage = string.Empty;
    }
}