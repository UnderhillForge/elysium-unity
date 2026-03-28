using System;

namespace Elysium.Combat
{
    /// Represents a single initiative roll result.
    [Serializable]
    public sealed class InitiativeRoll
    {
        public string ActorId = string.Empty;
        public string ActorName = string.Empty;
        public int DieResult = 0;           // d20 roll (1-20)
        public int InitiativeBonus = 0;     // DEX mod + other bonuses
        public int TotalInitiative => DieResult + InitiativeBonus;
        public int InitiativeOrder = 0;     // Position in turn order (0 = first, 1 = second, etc.)
        
        public override string ToString()
        {
            return $"{ActorName}: {DieResult} + {InitiativeBonus} = {TotalInitiative}";
        }
    }
}
