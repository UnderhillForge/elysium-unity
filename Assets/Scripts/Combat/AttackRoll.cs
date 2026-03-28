using System;

namespace Elysium.Combat
{
    /// Represents a single attack roll attempt (to-hit).
    [Serializable]
    public sealed class AttackRoll
    {
        public string AttackerId = string.Empty;
        public string DefenderId = string.Empty;
        public int AttackDieResult = 0;     // d20 roll (1-20)
        public int AttackBonus = 0;         // BAB + STR/DEX mod + item bonuses
        public int DefenderAC = 10;
        public int TotalAttackRoll => AttackDieResult + AttackBonus;
        
        public bool IsHit => TotalAttackRoll >= DefenderAC;
        public bool IsCriticalThreat => AttackDieResult >= 20 - CriticalThreatRange + 1; // e.g., 20 for 20, or 19-20 for keen
        public int CriticalThreatRange = 20; // Minimum d20 roll for threat
        public int CriticalMultiplier = 2;   // Damage multiplier if crit confirmed
        
        public int DamageBaseResult = 0;    // Weapon die result
        public int DamageBonus = 0;         // STR/DEX mod + other bonuses
        public int TotalDamage => DamageBaseResult + DamageBonus;
        public int CriticalDamage => (DamageBaseResult + DamageBonus) * CriticalMultiplier;
        
        public bool IsFlatFooted = false;   // Bypass DEX to AC
        public bool IsTouchAttack = false;   // Target touch AC instead
        
        public DamageType DamageType = DamageType.Bludgeoning;
        
        public override string ToString()
        {
            var result = IsHit ? "Hit" : "Miss";
            if (IsHit && IsCriticalThreat)
                result += " (Crit Threat)";
            return $"{AttackDieResult} + {AttackBonus} = {TotalAttackRoll} vs AC {DefenderAC}: {result}";
        }
    }
    
    public enum DamageType
    {
        Bludgeoning = 0,
        Piercing = 1,
        Slashing = 2,
        Fire = 3,
        Cold = 4,
        Electricity = 5,
        Acid = 6,
        Sonic = 7,
        Positive = 8,
        Negative = 9,
        Force = 10
    }
}
