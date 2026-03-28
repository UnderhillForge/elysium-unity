using System;
using System.Collections.Generic;
using System.Linq;

namespace Elysium.Combat
{
    /// Manages turn order and current turn state in a combat session.
    /// Responsible for initiative ordering and turn progression logic.
    public sealed class CombatTurnTracker
    {
        private List<Combatant> combatants = new List<Combatant>();
        private int currentTurnIndex = 0;
        private int currentRound = 1;
        private List<TurnAction> actionHistory = new List<TurnAction>();
        
        public IReadOnlyList<Combatant> Combatants => combatants;
        public IReadOnlyList<TurnAction> ActionHistory => actionHistory;
        public int CurrentRound => currentRound;
        public int CurrentTurnIndex => currentTurnIndex;
        
        /// Get the combatant whose turn it currently is.
        public Combatant CurrentCombatant => 
            currentTurnIndex >= 0 && currentTurnIndex < combatants.Count 
                ? combatants[currentTurnIndex] 
                : null;
        
        /// Check if combat is still active (someone can act).
        public bool IsActive => 
            combatants.Any(c => !c.IsDefeated && !c.IsStunned);
        
        /// Initialize turn order from initiative rolls.
        /// combatants: list of combatants with InitiativeRoll already calculated
        public void InitializeFromInitiative(List<Combatant> combatantList)
        {
            combatants = new List<Combatant>(combatantList);
            
            // Sort by initiative descending, then by ID for tiebreaker
            combatants = combatants
                .OrderByDescending(c => c.InitiativeRoll)
                .ThenBy(c => c.CombatantId)
                .ToList();
            
            // Assign turn order
            for (int i = 0; i < combatants.Count; i++)
            {
                combatants[i].TurnOrder = i;
                combatants[i].ResetTurnActions();
            }
            
            currentTurnIndex = 0;
            currentRound = 1;
            actionHistory.Clear();
        }

        /// Restore turn order and history from a previously persisted state snapshot.
        public void InitializeFromState(
            List<Combatant> combatantList,
            int persistedRound,
            string currentCombatantId,
            List<TurnAction> persistedActionHistory)
        {
            combatants = combatantList == null
                ? new List<Combatant>()
                : new List<Combatant>(combatantList
                    .OrderBy(c => c.TurnOrder)
                    .ThenBy(c => c.CombatantId));

            for (var i = 0; i < combatants.Count; i++)
            {
                combatants[i].TurnOrder = i;
            }

            currentRound = persistedRound > 0 ? persistedRound : 1;
            currentTurnIndex = 0;
            if (!string.IsNullOrEmpty(currentCombatantId))
            {
                var restoredIndex = combatants.FindIndex(c => c.CombatantId == currentCombatantId);
                if (restoredIndex >= 0)
                {
                    currentTurnIndex = restoredIndex;
                }
            }

            actionHistory = persistedActionHistory == null
                ? new List<TurnAction>()
                : new List<TurnAction>(persistedActionHistory);
        }
        
        /// Record an action taken during a turn.
        public void RecordAction(TurnAction action)
        {
            if (CurrentCombatant != null)
            {
                action.CombatantId = CurrentCombatant.CombatantId;
                action.RoundNumber = currentRound;
                action.TurnNumber = currentTurnIndex;
                action.TimestampUtc = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            actionHistory.Add(action);
        }
        
        /// Advance to the next turn in initiative order.
        /// If end of round reached, resets turn actions and increments round.
        public void AdvanceToNextTurn()
        {
            if (CurrentCombatant != null)
            {
                CurrentCombatant.EndTurn();
            }
            
            currentTurnIndex++;
            
            // Check if round complete
            if (currentTurnIndex >= combatants.Count)
            {
                CompleteRound();
            }
        }
        
        /// Complete current round: reset turn actions and start new round.
        private void CompleteRound()
        {
            // Reset turn states for all combatants
            foreach (var c in combatants)
            {
                c.ResetTurnActions();
            }
            
            currentRound++;
            currentTurnIndex = 0;
        }
        
        /// Skip a turn (for stunned/held combatants, etc).
        public void SkipTurn()
        {
            if (CurrentCombatant != null)
            {
                CurrentCombatant.HasTakenTurn = true;
            }
            AdvanceToNextTurn();
        }
        
        /// Get combatant by ID.
        public Combatant GetCombatant(string id)
        {
            return combatants.FirstOrDefault(c => c.CombatantId == id);
        }
        
        /// Add a new combatant mid-combat (e.g., reinforcements).
        /// Places at end of current turn order.
        public void AddCombatantMidCombat(Combatant combatant)
        {
            combatant.TurnOrder = combatants.Count;
            combatants.Add(combatant);
            combatant.ResetTurnActions();
        }
        
        /// Remove a combatant (death, flee, etc).
        public void RemoveCombatant(string combatantId)
        {
            var c = GetCombatant(combatantId);
            if (c != null)
            {
                combatants.Remove(c);
                
                // Adjust turn index if needed
                if (currentTurnIndex >= combatants.Count && combatants.Count > 0)
                {
                    currentTurnIndex = combatants.Count - 1;
                }
                
                // Rebuild turn order
                for (int i = 0; i < combatants.Count; i++)
                {
                    combatants[i].TurnOrder = i;
                }
            }
        }
        
        /// Get action history for a specific round.
        public List<TurnAction> GetRoundActions(int round)
        {
            return actionHistory.Where(a => a.RoundNumber == round).ToList();
        }
        
        /// Get action history for a specific combatant.
        public List<TurnAction> GetCombatantActions(string combatantId)
        {
            return actionHistory.Where(a => a.CombatantId == combatantId).ToList();
        }
        
        /// Check if anyone who can act remains.
        public bool HasActiveCombatants()
        {
            return combatants.Any(c => !c.IsDefeated);
        }
        
        /// Get next turn's combatant (preview).
        public Combatant PeekNextCombatant()
        {
            var nextIndex = currentTurnIndex + 1;
            if (nextIndex >= combatants.Count)
            {
                // Wrap to next round: first non-defeated combatant
                nextIndex = 0;
            }
            
            return nextIndex >= 0 && nextIndex < combatants.Count 
                ? combatants[nextIndex] 
                : null;
        }
        
        /// Flat representation of initiative order for UI display.
        public string GetInitiativeDisplay()
        {
            var lines = new List<string>
            {
                $"=== Round {currentRound} ===",
                $"Current Turn: {CurrentCombatant?.ActorName ?? "None"}"
            };
            
            lines.Add("Initiative Order:");
            for (int i = 0; i < combatants.Count; i++)
            {
                var c = combatants[i];
                var marker = i == currentTurnIndex ? ">>> " : "    ";
                var status = c.IsDefeated ? " (Defeated)" : c.IsStunned ? " (Stunned)" : "";
                lines.Add($"{marker}{i + 1}. {c.ActorName} (Init: {c.InitiativeRoll}, HP: {c.HitPointsCurrent}/{c.HitPointsMax}){status}");
            }
            
            return string.Join("\n", lines);
        }
    }
}
