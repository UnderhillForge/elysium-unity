using System;
using UnityEngine;

namespace Elysium.Characters
{
    /// Minimal PF1e-lite request payload for creating a player character.
    [Serializable]
    public sealed class CharacterCreationRequest
    {
        public string Id = string.Empty;
        public string DisplayName = string.Empty;
        public string Ruleset = Rules.RulesetId.Pathfinder1e;
        public int Level = 1;

        public int AbilityStrength = 10;
        public int AbilityDexterity = 10;
        public int AbilityConstitution = 10;
        public int AbilityIntelligence = 10;
        public int AbilityWisdom = 10;
        public int AbilityCharisma = 10;

        public int HitPointsMax = 8;
        public int HitPointsCurrent = 8;
        public int ArmorClass = 10;
        public int ArmorClassTouch = 10;
        public int ArmorClassFlatFooted = 10;

        public int BaseAttackBonus = 0;
        public int CriticalThreatRange = 20;
        public int CriticalMultiplier = 2;

        public int SaveFortitude = 0;
        public int SaveReflex = 0;
        public int SaveWill = 0;

        public long ExperiencePoints = 0;
    }
}