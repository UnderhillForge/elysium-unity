using System.IO;
using UnityEngine;

namespace Elysium.Boot
{
    public sealed class DatabaseBootstrapGuard : MonoBehaviour
    {
        [SerializeField] private string worldProjectFolder = "starter_forest_edge";
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private bool logSuccess = false;

        private void Start()
        {
            if (!runOnStart)
            {
                return;
            }

            CheckDatabases();
        }

        [ContextMenu("Check Databases")]
        public void CheckDatabases()
        {
            var dbRoot = Path.Combine(
                Application.streamingAssetsPath,
                "WorldProjects",
                worldProjectFolder,
                "Databases");

            var worldDbPath = Path.Combine(dbRoot, "world.db");
            var campaignDbPath = Path.Combine(dbRoot, "campaign.db");

            var worldExists = File.Exists(worldDbPath);
            var campaignExists = File.Exists(campaignDbPath);

            if (worldExists && campaignExists)
            {
                if (logSuccess)
                {
                    Debug.Log($"[Elysium] Database check passed for project '{worldProjectFolder}'.");
                }

                return;
            }

            var missing = string.Empty;
            if (!worldExists)
            {
                missing = "world.db";
            }

            if (!campaignExists)
            {
                missing = string.IsNullOrEmpty(missing) ? "campaign.db" : $"{missing}, campaign.db";
            }

            var migrationCommand = "./Database/sql/apply_migrations.sh";

            Debug.LogWarning(
                "[Elysium] Missing database file(s): " + missing +
                " for project '" + worldProjectFolder + "'. " +
                "Run this from repo root once sqlite3 is installed: `" + migrationCommand + "`");
        }
    }
}