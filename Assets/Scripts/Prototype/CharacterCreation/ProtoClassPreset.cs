using System;
using System.Collections.Generic;

namespace Elysium.Prototype.CharacterCreation
{
    /// Canonical PF1e stat block for a class starting at level 1.
    /// Encodes the minimum legally-valid values accepted by
    /// CharacterCreationService so the prototype UI never needs to surface
    /// raw ability-score pickers.
    [Serializable]
    public sealed class ProtoClassPreset
    {
        public string ClassKey        { get; }
        public string DisplayLabel    { get; }
        public int AbilityStrength    { get; }
        public int AbilityDexterity   { get; }
        public int AbilityConstitution{ get; }
        public int AbilityIntelligence{ get; }
        public int AbilityWisdom      { get; }
        public int AbilityCharisma    { get; }
        public int HitPointsMax       { get; }
        public int ArmorClass         { get; }
        public int ArmorClassTouch    { get; }
        public int ArmorClassFlatFooted { get; }
        public int BaseAttackBonus    { get; }
        public int SaveFortitude      { get; }
        public int SaveReflex         { get; }
        public int SaveWill           { get; }

        public ProtoClassPreset(
            string classKey,
            string displayLabel,
            int str, int dex, int con,
            int intel, int wis, int cha,
            int hpMax,
            int ac, int acTouch, int acFlatFooted,
            int bab,
            int fort, int reflex, int will)
        {
            ClassKey              = classKey ?? throw new ArgumentNullException(nameof(classKey));
            DisplayLabel          = displayLabel ?? classKey;
            AbilityStrength       = str;
            AbilityDexterity      = dex;
            AbilityConstitution   = con;
            AbilityIntelligence   = intel;
            AbilityWisdom         = wis;
            AbilityCharisma       = cha;
            HitPointsMax          = hpMax;
            ArmorClass            = ac;
            ArmorClassTouch       = acTouch;
            ArmorClassFlatFooted  = acFlatFooted;
            BaseAttackBonus       = bab;
            SaveFortitude         = fort;
            SaveReflex            = reflex;
            SaveWill              = will;
        }
    }

    /// Static registry of level-1 class presets shipped with the prototype lane.
    /// All values pass CharacterCreationService validation (scores 3–18, hp > 0, AC > 0).
    public static class ProtoClassPresetRegistry
    {
        private static readonly Dictionary<string, ProtoClassPreset> _presets =
            new Dictionary<string, ProtoClassPreset>(StringComparer.OrdinalIgnoreCase)
        {
            ["Fighter"] = new ProtoClassPreset(
                "Fighter", "Fighter",
                str: 16, dex: 13, con: 14,
                intel: 10, wis: 10, cha: 8,
                hpMax: 12,
                ac: 17, acTouch: 11, acFlatFooted: 16,
                bab: 1,
                fort: 4, reflex: 1, will: 1),

            ["Cleric"] = new ProtoClassPreset(
                "Cleric", "Cleric",
                str: 10, dex: 10, con: 12,
                intel: 10, wis: 16, cha: 14,
                hpMax: 9,
                ac: 16, acTouch: 10, acFlatFooted: 16,
                bab: 0,
                fort: 3, reflex: 1, will: 5),

            ["Rogue"] = new ProtoClassPreset(
                "Rogue", "Rogue",
                str: 12, dex: 16, con: 12,
                intel: 12, wis: 10, cha: 10,
                hpMax: 9,
                ac: 14, acTouch: 13, acFlatFooted: 11,
                bab: 0,
                fort: 1, reflex: 4, will: 1),

            ["Wizard"] = new ProtoClassPreset(
                "Wizard", "Wizard",
                str: 8, dex: 14, con: 12,
                intel: 18, wis: 12, cha: 10,
                hpMax: 7,
                ac: 12, acTouch: 12, acFlatFooted: 10,
                bab: 0,
                fort: 1, reflex: 2, will: 3),

            ["Ranger"] = new ProtoClassPreset(
                "Ranger", "Ranger",
                str: 14, dex: 16, con: 12,
                intel: 10, wis: 12, cha: 8,
                hpMax: 11,
                ac: 15, acTouch: 13, acFlatFooted: 12,
                bab: 1,
                fort: 3, reflex: 3, will: 2),
        };

        public static IReadOnlyDictionary<string, ProtoClassPreset> All => _presets;

        public static bool TryGet(string classKey, out ProtoClassPreset preset)
            => _presets.TryGetValue(classKey ?? string.Empty, out preset);
    }
}
