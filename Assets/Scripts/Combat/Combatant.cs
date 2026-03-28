using System;
using System.Collections.Generic;
using Elysium.Characters;

namespace Elysium.Combat
{
    /// Represents a single participant in combat (player character or NPC).
    [Serializable]
    public sealed class Combatant
    {
        public string CombatantId = string.Empty;   // Unique ID in this encounter
        public string ActorName = string.Empty;     // Display name
        public string CharacterId = string.Empty;   // Reference to CharacterRecord (if applicable)
        
        // Initiative and turn order
        public int InitiativeRoll = 0;              // d20 result + DEX mod
        public int TurnOrder = 0;                   // Position in initiative (0 = first)
        
        // Character stats (snapshot at combat start)
        public int HitPointsCurrent = 0;
        public int HitPointsMax = 0;
        public int ArmorClass = 10;
        public int ArmorClassTouch = 10;
        public int ArmorClassFlatFooted = 10;
        
        // Turn state
        public bool HasTakenTurn = false;           // true if already acted in this round
        public bool IsDefeated = false;             // true if HP <= 0
        public bool IsStunned = false;              // true if cannot act this turn
        
        // Actions on current turn
        public int StandardActionsRemaining = 1;    // Usually 1 per turn
        public int MoveActionsRemaining = 1;        // Usually 1 per turn
        public int SwiftActionsRemaining = 1;       // Usually 1 per turn
        public int FreeActionsRemaining = 1;        // Usually 1 per turn
        
        // Conditions
        public List<string> ActiveConditions = new List<string>();  // e.g., "stunned", "prone", "invisible"
        
        /// Check if this combatant can take actions.
        public bool CanActThisTurn => !HasTakenTurn && !IsDefeated && !IsStunned;
        
        /// Get remaining standard actions (typical: 1).
        public bool HasStandardAction => StandardActionsRemaining > 0;
        
        /// Get remaining move actions (typical: 1).
        public bool HasMoveAction => MoveActionsRemaining > 0;
        
        /// Get remaining swift actions (typical: 1).
        public bool HasSwiftAction => SwiftActionsRemaining > 0;
        
        /// Reset action economy for a new turn.
        public void ResetTurnActions()
        {
            StandardActionsRemaining = 1;
            MoveActionsRemaining = 1;
            SwiftActionsRemaining = 1;
            FreeActionsRemaining = 1;
            HasTakenTurn = false;
            IsStunned = false;
        }
        
        /// Consume a standard action.
        public bool TryUseStandardAction()
        {
            if (StandardActionsRemaining > 0)
            {
                StandardActionsRemaining--;
                return true;
            }
            return false;
        }
        
        /// Consume a move action.
        public bool TryUseMoveAction()
        {
            if (MoveActionsRemaining > 0)
            {
                MoveActionsRemaining--;
                return true;
            }
            return false;
        }
        
        /// Consume a swift action.
        public bool TryUseSwiftAction()
        {
            if (SwiftActionsRemaining > 0)
            {
                SwiftActionsRemaining--;
                return true;
            }
            return false;
        }
        
        /// Mark turn as complete.
        public void EndTurn()
        {
            HasTakenTurn = true;
        }
    }
}
