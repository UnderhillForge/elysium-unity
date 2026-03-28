using Elysium.Packaging;
using Elysium.Persistence;
using Elysium.World.Lua;
using System;

namespace Elysium.World
{
    /// Applies capability-checked, ownership-checked world mutations persisted to campaign.db.
    public sealed class WorldMutationService
    {
        private readonly string worldProjectFolder;
        private readonly CampaignPersistenceService persistence;

        public WorldMutationService(string worldProjectFolder, string campaignDatabasePath)
        {
            this.worldProjectFolder = worldProjectFolder ?? string.Empty;
            persistence = new CampaignPersistenceService(campaignDatabasePath);
        }

        public bool TryReadState(string key, LuaSandboxPolicy policy, out string value, out string error)
        {
            return TryReadState(requesterPlayerId: string.Empty, key, policy, enforceOwnership: false, out value, out error);
        }

        public bool TryReadState(
            string requesterPlayerId,
            string key,
            LuaSandboxPolicy policy,
            bool enforceOwnership,
            out string value,
            out string error)
        {
            value = string.Empty;

            if (policy == null)
            {
                error = "Lua sandbox policy is required.";
                return false;
            }

            if (!policy.AllowWorldRead)
            {
                error = "Lua capability denied by policy: world.read";
                return false;
            }

            if (!TryValidateKey(key, out error))
            {
                return false;
            }

            if (enforceOwnership
                && !WorldProjectLoader.TryLoadFromStreamingAssets(worldProjectFolder, requesterPlayerId, out _, out error))
            {
                return false;
            }

            return persistence.TryGetWorldState(key, out value, out error);
        }

        public bool TryWriteState(string requesterPlayerId, string key, string value, LuaSandboxPolicy policy, out string error)
        {
            if (policy == null)
            {
                error = "Lua sandbox policy is required.";
                return false;
            }

            if (!policy.AllowWorldWrite)
            {
                error = "Lua capability denied by policy: world.write";
                return false;
            }

            if (!TryValidateKey(key, out error))
            {
                return false;
            }

            if (!WorldProjectLoader.TryLoadFromStreamingAssets(worldProjectFolder, requesterPlayerId, out _, out error))
            {
                return false;
            }

            return persistence.TrySetWorldState(key, value, out error);
        }

        private static bool TryValidateKey(string key, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                error = "World state key is empty.";
                return false;
            }

            var trimmed = key.Trim();
            if (trimmed.Length < 3 || trimmed.Length > 128)
            {
                error = "World state key length must be between 3 and 128 characters.";
                return false;
            }

            if (trimmed.StartsWith(".", StringComparison.Ordinal)
                || trimmed.EndsWith(".", StringComparison.Ordinal)
                || trimmed.Contains("..", StringComparison.Ordinal)
                || !trimmed.Contains(".", StringComparison.Ordinal))
            {
                error = "World state key must be namespaced (for example: system.weather.state).";
                return false;
            }

            for (var i = 0; i < trimmed.Length; i++)
            {
                var c = trimmed[i];
                var valid = (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || c == '.'
                    || c == '_'
                    || c == '-';
                if (!valid)
                {
                    error = "World state key contains invalid characters. Allowed: a-z, A-Z, 0-9, '.', '_' and '-'.";
                    return false;
                }
            }

            return true;
        }
    }
}