using System;

namespace Elysium.Combat
{
    /// Represents a skill check attempt.
    [Serializable]
    public sealed class SkillCheck
    {
        public string ActorId = string.Empty;
        public string ActorName = string.Empty;
        public string SkillName = string.Empty;   // e.g., "Perception", "Stealth"
        
        public int DieResult = 0;           // d20 roll (1-20)
        public int SkillBonus = 0;          // Ranks + ability mod + synergy bonuses
        public int DifficultyClass = 10;
        public int TotalSkillCheck => DieResult + SkillBonus;
        
        public bool IsSuccessful => TotalSkillCheck >= DifficultyClass;
        public int MarginOfSuccess => TotalSkillCheck - DifficultyClass;  // Positive if passed, negative if failed
        
        public bool IsNatural1 => DieResult == 1;
        public bool IsNatural20 => DieResult == 20;
        
        public override string ToString()
        {
            var result = IsSuccessful ? $"Success (+{MarginOfSuccess})" : $"Failed ({MarginOfSuccess})";
            return $"{ActorName} {SkillName}: {DieResult} + {SkillBonus} = {TotalSkillCheck} vs DC {DifficultyClass}: {result}";
        }
    }
}
