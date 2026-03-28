using System;
using UnityEngine;

namespace Elysium.World
{
    /// Nested type for area.json sizeMeters field.
    [Serializable]
    public struct AreaExtents
    {
        [SerializeField] public float x;
        [SerializeField] public float z;
    }

    /// Deserialized representation of an area.json file.
    /// Follows the same pattern as WorldProjectDefinition — immutable after load.
    [Serializable]
    public sealed class AreaDefinition
    {
        [SerializeField] private string areaId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private string biome = string.Empty;
        [SerializeField] private AreaExtents sizeMeters;
        [SerializeField] private string entrySpawnId = string.Empty;
        [SerializeField] private string lightingProfile = string.Empty;
        [SerializeField] private string musicCue = string.Empty;

        public string AreaId => areaId;
        public string DisplayName => displayName;
        public string Biome => biome;
        public AreaExtents SizeMeters => sizeMeters;
        public string EntrySpawnId => entrySpawnId;
        public string LightingProfile => lightingProfile;
        public string MusicCue => musicCue;
    }
}
