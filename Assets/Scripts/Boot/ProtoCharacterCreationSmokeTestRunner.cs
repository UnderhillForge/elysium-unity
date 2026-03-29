using System;
using System.IO;
using System.Text;
using Elysium.Characters;
using Elysium.Prototype.CharacterCreation;
using UnityEngine;

namespace Elysium.Boot
{
    /// Smoke test for the prototype character-creation adapter layer.
    /// Verifies that ProtoCharacterCreationAdapter produces requests that
    /// CharacterCreationService accepts without modification.
    public sealed class ProtoCharacterCreationSmokeTestRunner : MonoBehaviour
    {
        private const string SourceProjectFolder = "starter_forest_edge";

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        public void RunProtoCharacterCreationSmokeTest()
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
                Debug.LogError($"[ProtoCharacterCreation] Smoke test failed: {ex}");
            }
        }

        private string RunInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Proto Character Creation Smoke Test ===");

            // ---- 1. Validate each registered class preset round-trips through the adapter ----
            foreach (var kv in ProtoClassPresetRegistry.All)
            {
                var appearance = new ProtoCharacterAppearance
                {
                    DisplayName = $"Test {kv.Key}",
                    ClassKey    = kv.Key,
                    RaceLabel   = ProtoRaceLabel.Human,
                };

                Require(ProtoCharacterCreationAdapter.TryBuildRequest(appearance, out var req, out var err), err);
                Require(req != null, $"Adapter returned null request for class '{kv.Key}'.");
                Require(string.Equals(req.Ruleset, Rules.RulesetId.Pathfinder1e, StringComparison.Ordinal),
                    $"Adapter did not set Pathfinder1e ruleset for '{kv.Key}'.");
                Require(req.HitPointsCurrent == req.HitPointsMax,
                    $"HitPointsCurrent should equal HitPointsMax for fresh character (class: '{kv.Key}').");

                log.AppendLine($"  Preset '{kv.Key}' → adapter output valid — ok");
            }

            // ---- 2. Validate that CharacterCreationService accepts every preset output ----
            var tempRoot = PrepareTempWorldCopy();
            var gallery  = new CharacterGalleryService();
            var creator  = new CharacterCreationService();

            Require(gallery.TryLoadFromWorldRoot(tempRoot, out var existing, out var galleryErr), galleryErr);

            foreach (var kv in ProtoClassPresetRegistry.All)
            {
                var appearance = new ProtoCharacterAppearance
                {
                    DisplayName = $"Proto {kv.Key}",
                    ClassKey    = kv.Key,
                };

                Require(ProtoCharacterCreationAdapter.TryBuildRequest(appearance, out var req, out var adapterErr), adapterErr);
                Require(creator.TryCreateCharacter(req, existing, out var created, out var createErr), createErr);
                Require(created != null, $"Created character is null for class '{kv.Key}'.");

                log.AppendLine($"  CharacterCreationService accepted preset '{kv.Key}' → id='{created.Id}' — ok");
            }

            // ---- 3. Validate rejection paths ----
            // Null appearance
            var nullRejected = !ProtoCharacterCreationAdapter.TryBuildRequest(null, out _, out var nullErr);
            Require(nullRejected, "Null appearance should be rejected.");
            log.AppendLine($"  Null appearance rejected: '{nullErr}' — ok");

            // Empty display name
            var emptyName = new ProtoCharacterAppearance { DisplayName = "", ClassKey = "Fighter" };
            var emptyNameRejected = !ProtoCharacterCreationAdapter.TryBuildRequest(emptyName, out _, out var emptyNameErr);
            Require(emptyNameRejected, "Empty DisplayName should be rejected.");
            log.AppendLine($"  Empty DisplayName rejected: '{emptyNameErr}' — ok");

            // Unknown class key
            var unknownClass = new ProtoCharacterAppearance { DisplayName = "Test", ClassKey = "Necromancer" };
            var unknownRejected = !ProtoCharacterCreationAdapter.TryBuildRequest(unknownClass, out _, out var unknownErr);
            Require(unknownRejected, "Unknown ClassKey should be rejected.");
            log.AppendLine($"  Unknown ClassKey rejected: '{unknownErr}' — ok");

            log.AppendLine("=== Proto Character Creation Smoke Test COMPLETE ===");
            return log.ToString();
        }

        private static string PrepareTempWorldCopy()
        {
            var sourceRoot = Path.Combine(Application.streamingAssetsPath, "WorldProjects", SourceProjectFolder);
            var tempRoot   = Path.Combine(Application.persistentDataPath, "ProtoCreationSmoke", Guid.NewGuid().ToString("N"));
            CopyDirectory(sourceRoot, tempRoot);
            return tempRoot;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(file, dest, overwrite: true);
            }
        }

        private static void Require(bool condition, string error)
        {
            if (!condition)
                throw new InvalidOperationException(error);
        }
    }
}
