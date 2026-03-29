using System;

namespace Elysium.Prototype.CharacterCreation
{
    /// Lightweight appearance and identity choices surfaced to a prototype
    /// character-creation UI. Values are intentionally coarse — they map to
    /// visual presets rather than precise numeric stats, which stay PF1e-locked
    /// via the class preset lookup inside ProtoCharacterCreationAdapter.
    [Serializable]
    public sealed class ProtoCharacterAppearance
    {
        /// Player-visible name (will be used to derive the gallery ID slug).
        public string DisplayName = string.Empty;

        /// Key into ProtoClassPreset registry (e.g. "Fighter", "Cleric", "Rogue").
        public string ClassKey = string.Empty;

        /// Optional portrait asset key for preview rendering.
        /// Left empty if no portrait selection has been made.
        public string PortraitKey = string.Empty;

        /// Race display label used in preview text only.
        /// Does not affect PF1e stat generation in the current prototype scope.
        public string RaceLabel = string.Empty;
    }

    /// Race display labels available in the prototype character-creation screen.
    public static class ProtoRaceLabel
    {
        public const string Human    = "Human";
        public const string Elf      = "Elf";
        public const string Dwarf    = "Dwarf";
        public const string Halfling = "Halfling";
        public const string Gnome    = "Gnome";
    }
}
