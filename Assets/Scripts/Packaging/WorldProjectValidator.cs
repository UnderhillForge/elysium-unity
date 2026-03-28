using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Elysium.Packaging
{
    public static class WorldProjectValidator
    {
        private static readonly string[] RequiredProjectFiles =
        {
            "project.json",
            "Actors/npcs.json",
            "Actors/factions.json",
            "Quests/quests.json",
            "Quests/dialog.json",
            "Assets/manifest.json",
            "Dependencies/dependencies.json",
        };

        private static readonly string[] RequiredAreaFiles =
        {
            "area.json",
            "terrain.json",
            "placements.json",
            "triggers.json",
            "encounters.json",
        };

        private static readonly string[] LuaReferenceFiles =
        {
            "Areas/{AREA}/placements.json",
            "Areas/{AREA}/triggers.json",
            "Actors/npcs.json",
        };

        public static WorldProjectValidationResult Validate(WorldProject worldProject, EwmPackageMode packageMode)
        {
            var result = new WorldProjectValidationResult();
            var root = worldProject.RootPath;
            var areaId = worldProject.Definition.EntryAreaId;

            ValidateRequiredProjectFiles(root, result);
            ValidateRequiredAreaFiles(root, areaId, result);
            ValidateDatabases(root, worldProject, packageMode, result);
            ValidateLuaReferences(root, areaId, result);

            return result;
        }

        private static void ValidateRequiredProjectFiles(string root, WorldProjectValidationResult result)
        {
            foreach (var relativePath in RequiredProjectFiles)
            {
                var fullPath = Path.Combine(root, relativePath);
                if (!File.Exists(fullPath))
                {
                    result.Errors.Add($"Missing required file: {relativePath}");
                }
            }
        }

        private static void ValidateRequiredAreaFiles(string root, string areaId, WorldProjectValidationResult result)
        {
            var areaDirectory = Path.Combine(root, "Areas", areaId);
            if (!Directory.Exists(areaDirectory))
            {
                result.Errors.Add($"Missing entry area directory: Areas/{areaId}");
                return;
            }

            foreach (var fileName in RequiredAreaFiles)
            {
                var areaFilePath = Path.Combine(areaDirectory, fileName);
                if (!File.Exists(areaFilePath))
                {
                    result.Errors.Add($"Missing required area file: Areas/{areaId}/{fileName}");
                }
            }
        }

        private static void ValidateDatabases(
            string root,
            WorldProject worldProject,
            EwmPackageMode packageMode,
            WorldProjectValidationResult result)
        {
            var worldDbPath = Path.Combine(root, worldProject.Definition.WorldDatabasePath);
            var campaignDbPath = Path.Combine(root, worldProject.Definition.CampaignDatabasePath);

            if (!File.Exists(worldDbPath))
            {
                if (packageMode == EwmPackageMode.Template)
                {
                    result.Warnings.Add("World database missing for template export. Authoring metadata can still be exported.");
                }
                else
                {
                    result.Errors.Add("World database is required for campaign snapshot export.");
                }
            }

            if (packageMode == EwmPackageMode.CampaignSnapshot && !File.Exists(campaignDbPath))
            {
                result.Errors.Add("Campaign database is required for campaign snapshot export.");
            }
            else if (packageMode == EwmPackageMode.Template && !File.Exists(campaignDbPath))
            {
                result.Warnings.Add("Campaign database not found. Template export will not include resume state.");
            }
        }

        private static void ValidateLuaReferences(string root, string areaId, WorldProjectValidationResult result)
        {
            var missingScripts = new HashSet<string>();

            foreach (var pattern in LuaReferenceFiles)
            {
                var relativePath = pattern.Replace("{AREA}", areaId);
                var fullPath = Path.Combine(root, relativePath);

                if (!File.Exists(fullPath))
                {
                    continue;
                }

                var json = File.ReadAllText(fullPath);
                foreach (var scriptPath in ExtractLuaPathValues(json))
                {
                    var normalized = scriptPath.Replace('/', Path.DirectorySeparatorChar);
                    var fullScriptPath = Path.Combine(root, normalized);
                    if (!File.Exists(fullScriptPath))
                    {
                        missingScripts.Add(scriptPath);
                    }
                }
            }

            foreach (var script in missingScripts)
            {
                result.Errors.Add($"Lua attachment file is missing: {script}");
            }
        }

        private static IEnumerable<string> ExtractLuaPathValues(string json)
        {
            var matches = Regex.Matches(
                json,
                "\"(?:luaAttachment|onEnterLua|onExitLua|onUseLua)\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    yield return match.Groups[1].Value;
                }
            }
        }
    }
}