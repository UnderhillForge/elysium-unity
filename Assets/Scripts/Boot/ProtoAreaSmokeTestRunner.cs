using System;
using System.Text;
using Elysium.Packaging;
using Elysium.World;
using UnityEngine;

namespace Elysium.Boot
{
    /// Smoke test for the prototype village-square world project and its
    /// integration with AreaLifecycleService.
    ///
    /// Verifies:
    ///  1. proto_village_square world project loads from StreamingAssets.
    ///  2. EntryAreaId resolves to "area_proto_village".
    ///  3. AreaLifecycleService activates the proto area successfully.
    ///  4. AreaDefinition fields match the authored area.json values.
    ///  5. Activate → Deactivate → Re-activate cycle completes without error.
    ///  6. ProtoCharacterCreationAdapter can create a character for this world project
    ///     (full Spike A + Spike C integration path).
    public sealed class ProtoAreaSmokeTestRunner : MonoBehaviour
    {
        private const string ProtoProjectFolder = "proto_village_square";
        private const string ExpectedAreaId      = "area_proto_village";
        private const string ExpectedDisplayName = "Proto Village Square";
        private const string ExpectedBiome       = "Village";
        private const string ExpectedEntrySpawnId = "spawn_proto_entry";

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        public void RunProtoAreaSmokeTest()
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
                Debug.LogError($"[ProtoArea] Smoke test failed: {ex}");
            }
        }

        private static string RunInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Proto Area Smoke Test ===");

            // ---- 1. World project loads ----
            Require(
                WorldProjectLoader.TryLoadFromStreamingAssets(ProtoProjectFolder, out var project, out var loadErr),
                $"Failed to load proto world project: {loadErr}");
            Require(project != null, "WorldProject was null after successful load.");
            log.AppendLine($"  Loaded project '{project.Definition.DisplayName}' — ok");

            // ---- 2. EntryAreaId correct ----
            Require(
                string.Equals(project.Definition.EntryAreaId, ExpectedAreaId, StringComparison.Ordinal),
                $"EntryAreaId '{project.Definition.EntryAreaId}' != expected '{ExpectedAreaId}'.");
            log.AppendLine($"  EntryAreaId == '{ExpectedAreaId}' — ok");

            // ---- 3. AreaLifecycleService activates the entry area ----
            var areaService = new AreaLifecycleService();
            Require(areaService.State == AreaState.Unloaded, "Initial state should be Unloaded.");

            var activatedId = string.Empty;
            areaService.AreaActivated += id => activatedId = id;

            Require(
                areaService.TryActivateEntryArea(project, out var activateErr),
                $"TryActivateEntryArea failed: {activateErr}");
            Require(areaService.State == AreaState.Active,
                $"State should be Active, got {areaService.State}.");
            Require(string.Equals(activatedId, ExpectedAreaId, StringComparison.Ordinal),
                $"AreaActivated event returned '{activatedId}', expected '{ExpectedAreaId}'.");
            log.AppendLine($"  Area activated: '{activatedId}' — ok");

            // ---- 4. AreaDefinition fields ----
            var def = areaService.ActiveAreaDefinition;
            Require(def != null, "ActiveAreaDefinition is null after activation.");
            AssertField(def.AreaId,       ExpectedAreaId,       "AreaId");
            AssertField(def.DisplayName,  ExpectedDisplayName,  "DisplayName");
            AssertField(def.Biome,        ExpectedBiome,        "Biome");
            AssertField(def.EntrySpawnId, ExpectedEntrySpawnId, "EntrySpawnId");
            Require(def.SizeMeters.x > 0 && def.SizeMeters.z > 0,
                $"SizeMeters must be positive, got ({def.SizeMeters.x}, {def.SizeMeters.z}).");
            log.AppendLine($"  AreaDefinition: biome='{def.Biome}', size=({def.SizeMeters.x},{def.SizeMeters.z}) — ok");

            // ---- 5. Deactivate → Re-activate cycle ----
            var deactivatedId = string.Empty;
            areaService.AreaDeactivated += id => deactivatedId = id;
            areaService.DeactivateArea();

            Require(areaService.State == AreaState.Unloaded,
                $"State should be Unloaded after deactivation, got {areaService.State}.");
            Require(string.Equals(deactivatedId, ExpectedAreaId, StringComparison.Ordinal),
                $"AreaDeactivated event returned '{deactivatedId}', expected '{ExpectedAreaId}'.");
            Require(areaService.ActiveAreaDefinition == null,
                "ActiveAreaDefinition should be null after deactivation.");
            log.AppendLine("  Deactivation succeeded — ok");

            // Re-activate
            Require(areaService.TryActivateEntryArea(project, out var reactivateErr),
                $"Re-activation failed: {reactivateErr}");
            Require(areaService.State == AreaState.Active,
                $"State should be Active after re-activation, got {areaService.State}.");
            areaService.DeactivateArea();
            log.AppendLine("  Deactivate → Re-activate → Deactivate cycle — ok");

            // ---- 6. Spike A + Spike C integration: create character, assign to session entry area ----
            var gallery  = new Characters.CharacterGalleryService();
            Require(gallery.TryLoadFromStreamingAssets(ProtoProjectFolder, out var protoChars, out var galErr),
                $"Gallery load from proto project failed: {galErr}");
            Require(protoChars != null && protoChars.Count > 0,
                "Proto world project gallery has no starter characters.");
            log.AppendLine($"  Gallery loaded: {protoChars.Count} starter character(s) — ok");

            var appearance = new Prototype.CharacterCreation.ProtoCharacterAppearance
            {
                DisplayName = "Spike C Integration Hero",
                ClassKey    = "Fighter",
            };
            Require(Prototype.CharacterCreation.ProtoCharacterCreationAdapter.TryBuildRequest(
                        appearance, out var req, out var adapterErr), adapterErr);
            var creator = new Characters.CharacterCreationService();
            Require(creator.TryCreateCharacter(req, protoChars, out var created, out var createErr), createErr);
            Require(created != null, "Created character is null.");
            log.AppendLine($"  Created character '{created.Id}' via adapter for proto world — ok");

            log.AppendLine("=== Proto Area Smoke Test COMPLETE ===");
            return log.ToString();
        }

        private static void AssertField(string actual, string expected, string fieldName)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Field '{fieldName}': expected '{expected}', got '{actual}'.");
        }

        private static void Require(bool condition, string error)
        {
            if (!condition)
                throw new InvalidOperationException(error);
        }
    }
}
