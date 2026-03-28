using System;

namespace Elysium.Combat
{
    /// Represents a saving throw check.
    [Serializable]
    public sealed class SavingThrow
    {
        public string ActorId = string.Empty;
        public string ActorName = string.Empty;
        public SavingThrowType SaveType = SavingThrowType.Reflex;
        
        public int DieResult = 0;           // d20 roll (1-20)
        public int SaveBonus = 0;           // Ability mod + class bonuses + items
        public int DifficultyClass = 10;
        public int TotalSave => DieResult + SaveBonus;
        
        public bool IsPassed => TotalSave >= DifficultyClass;
        
        /// For some effects, treat a natural 1 as auto-fail.
        public bool IsNatural1 => DieResult == 1;
        
        /// For some effects, treat a natural 20 as auto-pass.
        public bool IsNatural20 => DieResult == 20;
        
        public override string ToString()
        {
            var result = IsPassed ? "Passed" : "Failed";
            return $"{ActorName} {SaveType}: {DieResult} + {SaveBonus} = {TotalSave} vs DC {DifficultyClass}: {result}";
        }
    }
    
    public enum SavingThrowType
    {
        Fortitude = 0,  // CON-based, resists poison/disease/physical
        Reflex = 1,     // DEX-based, avoids AOE/traps
        Will = 2        // WIS-based, resists mind-affecting effects
    }
}
