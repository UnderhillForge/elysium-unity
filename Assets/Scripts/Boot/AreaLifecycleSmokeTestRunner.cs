using System;
using System.Text;
using Elysium.Networking;
using Elysium.Packaging;
using Elysium.World;
using UnityEngine;

namespace Elysium.Boot
{
    /// Exercises the AreaLifecycleService and its integration with HeadlessSessionBootstrap.
    /// Covers:
    ///   1. AreaLifecycleService starts Unloaded.
    ///   2. TryActivateArea loads area.json, transitions to Active, fires event.
    ///   3. Activated area fields (AreaId, DisplayName, Biome, EntrySpawnId) are populated.
    ///   4. Double-activate is rejected while an area is already Active.
    ///   5. DeactivateArea transitions back to Unloaded and fires event.
    ///   6. TryActivateArea on a missing area returns false with a clear error.
    ///   7. HeadlessSessionBootstrap with AreaLifecycleService activates entry area on Boot().
    ///   8. HeadlessSessionBootstrap.Shutdown() deactivates the area.
    public sealed class AreaLifecycleSmokeTestRunner : MonoBehaviour
    {
        private const string SourceProjectFolder = "starter_forest_edge";
        private const string EntryAreaId = "area_forest_edge";
        private const string ExpectedDisplayName = "Forest Edge";
        private const string ExpectedBiome = "TemperateForest";
        private const string ExpectedEntrySpawnId = "spawn_player_entry";
        private const string SmokeSessionId = "smoke_area_lifecycle_session";

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        public void RunAreaLifecycleSmokeTest()
        {
            try
            {
                LastSummary = RunAreaLifecycleSmokeTestInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"Area lifecycle smoke test failed: {ex}");
            }
        }

        private string RunAreaLifecycleSmokeTestInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Area Lifecycle Smoke Test ===");

            // Need world project root path for direct AreaLifecycleService calls.
            if (!WorldProjectLoader.TryLoadFromStreamingAssets(SourceProjectFolder, out var project, out var loadErr))
                throw new InvalidOperationException($"Failed to load starter project: {loadErr}");

            var rootPath = project.RootPath;

            // 1. Service starts Unloaded.
            var areaService = new AreaLifecycleService();
            if (areaService.State != AreaState.Unloaded)
                throw new InvalidOperationException($"Initial state should be Unloaded, got {areaService.State}.");
            if (areaService.HasActiveArea)
                throw new InvalidOperationException("HasActiveArea should be false before activation.");
            log.AppendLine("Initial state Unloaded — ok");

            // 2. TryActivateArea succeeds, fires event.
            var activatedAreaId = string.Empty;
            areaService.AreaActivated += id => activatedAreaId = id;

            if (!areaService.TryActivateArea(rootPath, EntryAreaId, out var activateErr))
                throw new InvalidOperationException($"TryActivateArea failed: {activateErr}");

            if (areaService.State != AreaState.Active)
                throw new InvalidOperationException($"State should be Active after activation, got {areaService.State}.");
            if (activatedAreaId != EntryAreaId)
                throw new InvalidOperationException($"AreaActivated event fired with '{activatedAreaId}', expected '{EntryAreaId}'.");
            log.AppendLine($"Activation succeeded, event fired with '{activatedAreaId}' — ok");

            // 3. Area definition fields populated.
            var def = areaService.ActiveAreaDefinition;
            AssertField(def.AreaId, EntryAreaId, "AreaId");
            AssertField(def.DisplayName, ExpectedDisplayName, "DisplayName");
            AssertField(def.Biome, ExpectedBiome, "Biome");
            AssertField(def.EntrySpawnId, ExpectedEntrySpawnId, "EntrySpawnId");
            if (def.SizeMeters.x <= 0 || def.SizeMeters.z <= 0)
                throw new InvalidOperationException($"SizeMeters should be positive, got ({def.SizeMeters.x},{def.SizeMeters.z}).");
            log.AppendLine($"AreaDefinition fields: areaId='{def.AreaId}', biome='{def.Biome}', size=({def.SizeMeters.x},{def.SizeMeters.z}) — ok");

            // 4. Double-activate rejected.
            if (areaService.TryActivateArea(rootPath, EntryAreaId, out var doubleErr))
                throw new InvalidOperationException("Double-activate should have been rejected.");
            if (string.IsNullOrEmpty(doubleErr))
                throw new InvalidOperationException("Double-activate error message was empty.");
            log.AppendLine($"Double-activate rejected: '{doubleErr}' — ok");

            // 5. DeactivateArea transitions to Unloaded and fires event.
            var deactivatedAreaId = string.Empty;
            areaService.AreaDeactivated += id => deactivatedAreaId = id;
            areaService.DeactivateArea();

            if (areaService.State != AreaState.Unloaded)
                throw new InvalidOperationException($"State should be Unloaded after deactivation, got {areaService.State}.");
            if (deactivatedAreaId != EntryAreaId)
                throw new InvalidOperationException($"AreaDeactivated event fired with '{deactivatedAreaId}', expected '{EntryAreaId}'.");
            if (areaService.ActiveAreaDefinition != null)
                throw new InvalidOperationException("ActiveAreaDefinition should be null after deactivation.");
            log.AppendLine($"Deactivation succeeded, event fired with '{deactivatedAreaId}' — ok");

            // 6. Missing area returns false with a clear error.
            if (areaService.TryActivateArea(rootPath, "area_does_not_exist", out var missingErr))
                throw new InvalidOperationException("Missing area should have returned false.");
            if (string.IsNullOrEmpty(missingErr))
                throw new InvalidOperationException("Missing area error message was empty.");
            log.AppendLine($"Missing area rejected: '{missingErr}' — ok");

            // 7. HeadlessSessionBootstrap with AreaLifecycleService activates entry area on Boot().
            var transport = new HeadlessNetworkTransport();
            var sessionService = new SessionService();
            var areaLifecycle = new AreaLifecycleService();
            var bootstrap = new HeadlessSessionBootstrap(transport, sessionService, areaLifecycle);

            if (!bootstrap.Boot(SmokeSessionId, SourceProjectFolder, out var bootErr))
                throw new InvalidOperationException($"HeadlessSessionBootstrap.Boot() with area failed: {bootErr}");

            if (bootstrap.AreaLifecycle.State != AreaState.Active)
                throw new InvalidOperationException($"Area should be Active after boot, got {bootstrap.AreaLifecycle.State}.");
            if (bootstrap.AreaLifecycle.ActiveAreaId != EntryAreaId)
                throw new InvalidOperationException($"Active area '{bootstrap.AreaLifecycle.ActiveAreaId}' != expected '{EntryAreaId}'.");

            log.AppendLine($"HeadlessSessionBootstrap auto-activated entry area '{bootstrap.AreaLifecycle.ActiveAreaId}' — ok");

            // 8. Shutdown deactivates area.
            bootstrap.Shutdown();
            if (bootstrap.AreaLifecycle.State != AreaState.Unloaded)
                throw new InvalidOperationException($"Area should be Unloaded after shutdown, got {bootstrap.AreaLifecycle.State}.");

            log.AppendLine("Shutdown deactivated area — ok");
            log.AppendLine("=== Area Lifecycle Smoke Test COMPLETE ===");
            return log.ToString();
        }

        private static void AssertField(string actual, string expected, string fieldName)
        {
            if (actual != expected)
                throw new InvalidOperationException(
                    $"AreaDefinition.{fieldName}: expected '{expected}', got '{actual}'.");
        }
    }
}
