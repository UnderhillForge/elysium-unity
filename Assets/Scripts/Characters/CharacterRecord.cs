using System;
using System.Collections.Generic;
using UnityEngine;

namespace Elysium.Characters
{
    [Serializable]
    public sealed class CharacterRecord
    {
        public string Id = string.Empty;
        public string DisplayName = string.Empty;
        public string Ruleset = Rules.RulesetId.Pathfinder1e;
        public int Level = 1;
        
        // PF1e Ability Scores (3-18 typical range)
        public int AbilityStrength = 10;
        public int AbilityDexterity = 10;
        public int AbilityConstitution = 10;
        public int AbilityIntelligence = 10;
        public int AbilityWisdom = 10;
        public int AbilityCharisma = 10;
        
        // Hit points and AC
        public int HitPointsMax = 8;
        public int HitPointsCurrent = 8;
        public int ArmorClass = 10;
        public int ArmorClassTouch = 10;
        public int ArmorClassFlatFooted = 10;
        
        // Combat stats
        public int BaseAttackBonus = 0;
        public int CriticalThreatRange = 20;  // d20 roll value for threat
        public int CriticalMultiplier = 2;    // damage multiplier on confirmed crit
        
        // Saving throws
        public int SaveFortitude = 0;
        public int SaveReflex = 0;
        public int SaveWill = 0;
        
        // Experience and advancement
        [SerializeField]
        private long experiencePoints = 0;
        public long ExperiencePoints
        {
            get => experiencePoints;
            set => experiencePoints = Math.Max(0, value);
        }
        
        // Skills (serialized as list for JSON compatibility)
        [SerializeField]
        private List<PF1eSkillRank> skillRanks = new List<PF1eSkillRank>();
        public IReadOnlyList<PF1eSkillRank> SkillRanks => skillRanks;
        
        public void SetSkillRanks(List<PF1eSkillRank> ranks)
        {
            skillRanks.Clear();

            if (ranks == null)
            {
                return;
            }

            foreach (var rank in ranks)
            {
                if (rank != null)
                {
                    skillRanks.Add(rank);
                }
            }
        }
        
        /// Get ability modifier for a given ability score.
        /// PF1e: modifier = (score - 10) / 2, rounded down
        public int GetAbilityModifier(PF1eAbility ability)
        {
            var score = ability switch
            {
                PF1eAbility.Strength => AbilityStrength,
                PF1eAbility.Dexterity => AbilityDexterity,
                PF1eAbility.Constitution => AbilityConstitution,
                PF1eAbility.Intelligence => AbilityIntelligence,
                PF1eAbility.Wisdom => AbilityWisdom,
                PF1eAbility.Charisma => AbilityCharisma,
                _ => 10
            };
            return (score - 10) / 2;
        }
    }
    
    public enum PF1eAbility
    {
        Strength = 0,
        Dexterity = 1,
        Constitution = 2,
        Intelligence = 3,
        Wisdom = 4,
        Charisma = 5
    }
    
    [Serializable]
    public sealed class PF1eSkillRank
    {
        public string SkillName = string.Empty;  // e.g., "Acrobatics", "Perception"
        public int Ranks = 0;                     // 0 to level+3
        public bool IsClassSkill = false;         // +3 bonus if true
        
        public int GetBonus(PF1eAbility governingAbility, CharacterRecord character)
        {
            var abilityMod = character.GetAbilityModifier(governingAbility);
            var classBonus = IsClassSkill ? 3 : 0;
            return Ranks + abilityMod + classBonus;
        }
    }
}