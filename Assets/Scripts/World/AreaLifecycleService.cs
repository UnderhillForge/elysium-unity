using System;
using System.IO;
using Elysium.Packaging;
using UnityEngine;

namespace Elysium.World
{
    /// Manages area load/activate/deactivate/unload lifecycle for a world project.
    /// One area may be Active at a time. Designed for both host and headless boot paths.
    ///
    /// Lifecycle:
    ///   Unloaded → (TryActivateArea) → Active → (DeactivateArea) → Unloaded
    public sealed class AreaLifecycleService
    {
        private const string AreasFolderName = "Areas";
        private const string AreaFileName = "area.json";

        public event Action<string> AreaActivated;
        public event Action<string> AreaDeactivated;

        public string ActiveAreaId { get; private set; } = string.Empty;
        public AreaDefinition ActiveAreaDefinition { get; private set; }
        public AreaState State { get; private set; } = AreaState.Unloaded;
        public bool HasActiveArea => State == AreaState.Active;

        /// Load and activate the entry area of a world project.
        public bool TryActivateEntryArea(WorldProject worldProject, out string error)
        {
            if (worldProject == null) throw new ArgumentNullException(nameof(worldProject));
            var areaId = worldProject.Definition.EntryAreaId;
            return TryActivateArea(worldProject.RootPath, areaId, out error);
        }

        /// Load and activate a specific area by ID within a world project root.
        public bool TryActivateArea(string worldRootPath, string areaId, out string error)
        {
            if (string.IsNullOrWhiteSpace(worldRootPath))
            {
                error = "World root path is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(areaId))
            {
                error = "Area ID is empty.";
                return false;
            }

            if (State == AreaState.Active)
            {
                error = $"Cannot activate area '{areaId}': area '{ActiveAreaId}' is already active. " +
                        "Call DeactivateArea() first.";
                return false;
            }

            var areaJsonPath = Path.Combine(worldRootPath, AreasFolderName, areaId, AreaFileName);
            if (!File.Exists(areaJsonPath))
            {
                error = $"area.json not found for area '{areaId}': {areaJsonPath}";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(areaJsonPath);
            }
            catch (Exception ex)
            {
                error = $"Failed reading area.json for '{areaId}': {ex.Message}";
                return false;
            }

            AreaDefinition definition;
            try
            {
                definition = JsonUtility.FromJson<AreaDefinition>(json);
            }
            catch (Exception ex)
            {
                error = $"Failed parsing area.json for '{areaId}': {ex.Message}";
                return false;
            }

            if (definition == null)
            {
                error = $"area.json for '{areaId}' was empty or invalid.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.AreaId))
            {
                error = $"area.json for '{areaId}' is missing required field: areaId";
                return false;
            }

            ActiveAreaId = definition.AreaId;
            ActiveAreaDefinition = definition;
            State = AreaState.Active;

            Debug.Log($"[AreaLifecycleService] Area activated: '{ActiveAreaId}' ({definition.DisplayName})");
            AreaActivated?.Invoke(ActiveAreaId);

            error = string.Empty;
            return true;
        }

        /// Deactivate the currently active area. No-op if already unloaded.
        public void DeactivateArea()
        {
            if (State == AreaState.Unloaded)
                return;

            var previousAreaId = ActiveAreaId;
            ActiveAreaId = string.Empty;
            ActiveAreaDefinition = null;
            State = AreaState.Unloaded;

            Debug.Log($"[AreaLifecycleService] Area deactivated: '{previousAreaId}'");
            AreaDeactivated?.Invoke(previousAreaId);
        }
    }
}
