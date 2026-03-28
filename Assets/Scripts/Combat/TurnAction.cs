using System;

namespace Elysium.Combat
{
    /// Represents a single action taken during a turn.
    [Serializable]
    public sealed class TurnAction
    {
        public string ActionId = System.Guid.NewGuid().ToString();
        public string CombatantId = string.Empty;   // Who performed the action
        public int RoundNumber = 0;
        public int TurnNumber = 0;                  // Turn within the round
        
        public TurnActionType ActionType = TurnActionType.StandardAction;
        public string ActionName = string.Empty;    // e.g., "Attack", "Cast Spell", "Move"
        public string ActionDescription = string.Empty;
        
        // Action resolution
        public bool IsResolved = false;
        public bool Succeeded = false;              // true if action succeeded
        public string ResolutionResult = string.Empty;
        
        // Metadata
        public string TargetCombatantId = string.Empty;  // e.g., for attack actions
        public long TimestampUtc = 0;
        
        public override string ToString()
        {
            return $"[R{RoundNumber}] {ActionName} by {CombatantId}";
        }
    }
    
    public enum TurnActionType
    {
        StandardAction = 0,   // Attack, cast spell, use special ability
        MoveAction = 1,       // Move, draw weapon
        SwiftAction = 2,      // Bonus action, free action-like
        FreeAction = 3,       // Full-round equivalent
        Reaction = 4,         // Opportunity attack, readied action
        FullRoundAction = 5   // Takes entire turn (charge, full attack)
    }
}
