using System;
using System.IO;
using System.Text;
using Elysium.Packaging;
using UnityEngine;

namespace Elysium.Boot
{
    /// Verifies world ownership and access control policy.
    /// Positive path: owner and collaborator both get edit access.
    /// Negative paths: empty player ID is denied; non-owner non-collaborator is denied.
    /// Validator path: project with no ownerPlayerId triggers an ownership warning.
    public sealed class WorldOwnershipSmokeTestRunner : MonoBehaviour
    {
        private const string SourceProjectFolder = "starter_forest_edge";
        private const string OwnerPlayerId = "gm.elysium.default";
        private const string CollaboratorPlayerId = "gm.collab.smoke";
        private const string StrangerPlayerId = "gm.stranger.smoke";

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        public void RunOwnershipSmokeTest()
        {
            try
            {
                LastSummary = RunOwnershipSmokeTestInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"World ownership smoke test failed: {ex}");
            }
        }

        private string RunOwnershipSmokeTestInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("=== World Ownership Smoke Test ===");

            // 1. Load and verify OwnerPlayerId is present on the starter project.
            if (!WorldProjectLoader.TryLoadFromStreamingAssets(SourceProjectFolder, out var project, out var loadErr))
                throw new InvalidOperationException($"Failed to load starter project: {loadErr}");

            var definition = project.Definition;

            if (string.IsNullOrWhiteSpace(definition.OwnerPlayerId))
                throw new InvalidOperationException("project.json did not deserialize ownerPlayerId.");

            log.AppendLine($"OwnerPlayerId: '{definition.OwnerPlayerId}' — ok");

            // 2. Owner access — must be granted.
            AssertCanEdit(definition, OwnerPlayerId, expectedAllow: true, log);

            // 3. Empty requestingPlayerId — must be denied.
            AssertCanEdit(definition, string.Empty, expectedAllow: false, log);

            // 4. Stranger — must be denied.
            AssertCanEdit(definition, StrangerPlayerId, expectedAllow: false, log);

            // 5. Collaborator access — add collaborator to a synthetic definition, verify grant.
            var syntheticDefinition = BuildSyntheticDefinition(
                ownerId: OwnerPlayerId,
                collaborators: new[] { CollaboratorPlayerId });

            AssertCanEdit(syntheticDefinition, CollaboratorPlayerId, expectedAllow: true, log);
            AssertCanEdit(syntheticDefinition, StrangerPlayerId, expectedAllow: false, log);
            log.AppendLine("Collaborator access gates — ok");

            // 6. Open-authoring project (no ownerPlayerId) — any non-empty ID can edit.
            var openDefinition = BuildSyntheticDefinition(ownerId: string.Empty, collaborators: new string[0]);
            AssertCanEdit(openDefinition, StrangerPlayerId, expectedAllow: true, log);
            AssertCanEdit(openDefinition, string.Empty, expectedAllow: false, log);
            log.AppendLine("Open-authoring access gates — ok");

            // 7. Ownership-enforced loader overload — owner succeeds, stranger fails.
            if (!WorldProjectLoader.TryLoadFromStreamingAssets(SourceProjectFolder, OwnerPlayerId, out _, out var _))
                throw new InvalidOperationException("Ownership-enforced loader denied the legitimate owner.");
            log.AppendLine("Ownership-enforced loader (owner) — ok");

            if (WorldProjectLoader.TryLoadFromStreamingAssets(SourceProjectFolder, StrangerPlayerId, out _, out var denialErr))
                throw new InvalidOperationException("Ownership-enforced loader should have denied the stranger.");
            if (string.IsNullOrEmpty(denialErr))
                throw new InvalidOperationException("Denial error message was empty.");
            log.AppendLine($"Ownership-enforced loader (stranger) denied — '{denialErr}' — ok");

            // 8. Validator flags missing ownership metadata.
            var noOwnerDefinition = BuildSyntheticDefinition(ownerId: string.Empty, collaborators: new string[0]);
            var noOwnerProject = BuildSyntheticProject(noOwnerDefinition);
            var validationResult = WorldProjectValidator.Validate(noOwnerProject, EwmPackageMode.Template);
            var hasOwnershipWarning = validationResult.Warnings.Exists(w => w.Contains("[Ownership]"));
            if (!hasOwnershipWarning)
                throw new InvalidOperationException("WorldProjectValidator did not emit [Ownership] warning for a project with no owner.");
            log.AppendLine("Validator [Ownership] warning for ownerless project — ok");

            log.AppendLine("=== World Ownership Smoke Test COMPLETE ===");
            return log.ToString();
        }

        private static void AssertCanEdit(
            Elysium.World.Authoring.WorldProjectDefinition definition,
            string requestingPlayerId,
            bool expectedAllow,
            StringBuilder log)
        {
            var actual = WorldAccessPolicy.CanEdit(definition, requestingPlayerId);
            if (actual != expectedAllow)
            {
                var displayId = string.IsNullOrEmpty(requestingPlayerId) ? "<empty>" : requestingPlayerId;
                throw new InvalidOperationException(
                    $"WorldAccessPolicy.CanEdit('{displayId}') returned {actual}, expected {expectedAllow}.");
            }

            var displayPlayer = string.IsNullOrEmpty(requestingPlayerId) ? "<empty>" : requestingPlayerId;
            log.AppendLine($"  CanEdit('{displayPlayer}') = {actual} (expected {expectedAllow}) — ok");
        }

        /// Builds a synthetic WorldProjectDefinition via JSON round-trip so JsonUtility
        /// private-field serialization is exercised without needing a real project.json on disk.
        private static Elysium.World.Authoring.WorldProjectDefinition BuildSyntheticDefinition(
            string ownerId,
            string[] collaborators)
        {
            // Build a minimal project.json payload, deserialize it through JsonUtility.
            var collaboratorsJson = BuildJsonArray(collaborators);
            var json = "{" +
                "\"projectId\":\"smoke.synthetic\"," +
                "\"displayName\":\"Smoke Synthetic\"," +
                "\"author\":\"Smoke\"," +
                $"\"ownerPlayerId\":\"{EscapeJson(ownerId)}\"," +
                $"\"collaborators\":{collaboratorsJson}," +
                "\"ruleset\":\"Pathfinder1e\"," +
                "\"entryAreaId\":\"area_smoke\"," +
                "\"worldDatabasePath\":\"Databases/world.db\"," +
                "\"campaignDatabasePath\":\"Databases/campaign.db\"," +
                "\"createdUtc\":\"2026-03-28T00:00:00Z\"," +
                "\"updatedUtc\":\"2026-03-28T00:00:00Z\"," +
                "\"defaultPackageMode\":\"Template\"," +
                "\"notes\":\"\"" +
                "}";
            return JsonUtility.FromJson<Elysium.World.Authoring.WorldProjectDefinition>(json);
        }

        private static WorldProject BuildSyntheticProject(Elysium.World.Authoring.WorldProjectDefinition definition)
        {
            // Use a temp path that can stand in as the root; validator checks OwnerPlayerId
            // from the definition, not the filesystem, for the ownership check.
            var rootPath = Path.Combine(Application.temporaryCachePath, "smoke_synthetic_world");
            return new WorldProject(rootPath, definition);
        }

        private static string BuildJsonArray(string[] items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"');
                sb.Append(EscapeJson(items[i]));
                sb.Append('"');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
