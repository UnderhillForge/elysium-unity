using Elysium.Packaging;
using Elysium.Persistence;
using Elysium.World.Lua;

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

            if (!WorldProjectLoader.TryLoadFromStreamingAssets(worldProjectFolder, requesterPlayerId, out _, out error))
            {
                return false;
            }

            return persistence.TrySetWorldState(key, value, out error);
        }
    }
}