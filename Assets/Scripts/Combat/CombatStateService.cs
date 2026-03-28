using System;
using System.Collections.Generic;
using UnityEngine;

namespace Elysium.Combat
{
    /// Orchestrates turn-based combat state and progression for an encounter.
    /// Acts as the primary interface for GM-guided combat management.
    public sealed class CombatStateService
    {
        private string encounterId = string.Empty;
        private CombatMode combatMode = CombatMode.GMGuided;
        private string gmId = string.Empty;              // GM authority (GM-guided mode only)
        private CombatTurnTracker turnTracker;
        private List<TurnAction> allActions = new List<TurnAction>();
        private List<TurnAction> pendingActions = new List<TurnAction>();
        
        public string EncounterId => encounterId;
        public CombatMode CombatMode => combatMode;
        public string GMId => gmId;
        public bool RequiresGMApproval => combatMode == CombatMode.GMGuided;
        public bool IsPlayerTraining => combatMode == CombatMode.PlayerTraining;
        public IReadOnlyList<TurnAction> AllActions => allActions;
        public IReadOnlyList<TurnAction> PendingActions => pendingActions;
        
        /// Current round number.
        public int CurrentRound => turnTracker?.CurrentRound ?? 0;
        
        /// Combatant whose turn it is.
        public Combatant CurrentCombatant => turnTracker?.CurrentCombatant;
        
        /// All combatants in this encounter.
        public IReadOnlyList<Combatant> Combatants => turnTracker?.Combatants;
        
        /// Check if combat session is active (someone can still act).
        public bool IsActive => turnTracker?.IsActive ?? false;
        
        /// Create a new combat state for an encounter.
        public static CombatStateService CreateForEncounter(
            string _encounterId,
            CombatMode mode,
            string _gmId = "")
        {
            return new CombatStateService
            {
                encounterId = _encounterId,
                combatMode = mode,
                gmId = _gmId,
                turnTracker = new CombatTurnTracker()
            };
        }
        
        /// Initialize combat with combatants and roll initiative.
        public void InitializeCombat(List<Combatant> combatants)
        {
            if (turnTracker == null)
            {
                turnTracker = new CombatTurnTracker();
            }
            
            turnTracker.InitializeFromInitiative(combatants);
            allActions.Clear();
            pendingActions.Clear();
        }
        
        /// Get current initiative display for UI/logging.
        public string GetInitiativeDisplay()
        {
            return turnTracker?.GetInitiativeDisplay() ?? "No combat initialized";
        }
        
        /// Attempt to perform an action during the current turn.
        /// In GM-guided mode, returns a request that needs GM approval.
        /// In training mode, auto-resolves the action.
        public ActionResolution AttemptAction(
            TurnActionRequest request,
            out string validationError)
        {
            validationError = string.Empty;
            
            // Validate it's the current combatant's turn
            if (request.CombatantId != CurrentCombatant?.CombatantId)
            {
                validationError = $"Not {request.CombatantId}'s turn. Current: {CurrentCombatant?.CombatantId}";
                return null;
            }
            
            // Validate action economy
            if (!ValidateActionCost(request.ActionType, out validationError))
            {
                return null;
            }
            
            // Create action entry
            var action = new TurnAction
            {
                ActionName = request.ActionName,
                ActionDescription = request.Description,
                ActionType = request.ActionType,
                TargetCombatantId = request.TargetCombatantId
            };
            
            var resolution = new ActionResolution
            {
                Action = action,
                RequiresGMApproval = RequiresGMApproval,
                IsAutoResolved = IsPlayerTraining
            };

            allActions.Add(action);
            
            if (IsPlayerTraining)
            {
                // Auto-resolve in training mode
                resolution.Succeeded = true;
                resolution.ResultMessage = $"{request.ActionName} succeeded";
                action.IsResolved = true;
                action.Succeeded = true;
                action.ResolutionResult = resolution.ResultMessage;
                ConsumeActionCost(request.ActionType);
                turnTracker.RecordAction(action);
            }
            else
            {
                // Queue for GM approval in guided mode
                pendingActions.Add(action);
                resolution.Succeeded = false;  // Pending approval
                resolution.ResultMessage = $"Awaiting GM approval: {request.ActionName}";
            }
            
            return resolution;
        }
        
        /// GM approves or denies a pending action (GM-guided mode only).
        public bool ResolveActionApproval(
            string actionId,
            bool approved,
            string gmResolution = "")
        {
            var action = allActions.Find(a => a.ActionId == actionId);
            if (action == null)
            {
                return false;
            }
            
            action.IsResolved = true;
            action.Succeeded = approved;
            action.ResolutionResult = approved 
                ? gmResolution 
                : $"GM denied action: {gmResolution}";

            pendingActions.RemoveAll(a => a.ActionId == actionId);
            
            if (approved)
            {
                ConsumeActionCostForAction(action.ActionType);
            }

            turnTracker.RecordAction(action);
            return true;
        }
        
        /// End current combatant's turn and advance to next.
        public void EndCurrentTurn()
        {
            turnTracker.AdvanceToNextTurn();
        }
        
        /// Skip current turn (for stunned combatants, etc).
        public void SkipCurrentTurn()
        {
            turnTracker.SkipTurn();
        }
        
        /// Apply damage and check for defeat.
        public void ProcessDamage(string targetId, int damageAmount)
        {
            var target = turnTracker.GetCombatant(targetId);
            if (target != null)
            {
                target.HitPointsCurrent = Mathf.Max(target.HitPointsCurrent - damageAmount, -10);
                if (target.HitPointsCurrent <= 0)
                {
                    target.IsDefeated = true;
                }
            }
        }
        
        /// Heal combatant.
        public void ProcessHealing(string targetId, int healAmount)
        {
            var target = turnTracker.GetCombatant(targetId);
            if (target != null)
            {
                target.HitPointsCurrent = Mathf.Min(target.HitPointsCurrent + healAmount, target.HitPointsMax);
            }
        }
        
        /// Get action history for a round.
        public List<TurnAction> GetRoundActions(int round)
        {
            return turnTracker.GetRoundActions(round);
        }
        
        /// Get all actions by a combatant.
        public List<TurnAction> GetCombatantHistory(string combatantId)
        {
            return turnTracker.GetCombatantActions(combatantId);
        }

        /// Create a compact snapshot of the current encounter state.
        public EncounterSnapshot CreateEncounterSnapshot()
        {
            return new EncounterSnapshot
            {
                EncounterId = encounterId,
                RoundNumber = CurrentRound,
                IsActive = IsActive,
                Mode = combatMode,
                GMId = gmId,
                CurrentCombatantId = CurrentCombatant?.CombatantId ?? string.Empty,
                CombatantIds = turnTracker == null
                    ? new List<string>()
                    : new List<string>(System.Linq.Enumerable.Select(turnTracker.Combatants, c => c.CombatantId))
            };
        }

        /// Create a persistence snapshot containing enough state to resume combat later.
        public CombatPersistenceSnapshot CreatePersistenceSnapshot()
        {
            return new CombatPersistenceSnapshot
            {
                EncounterId = encounterId,
                CombatMode = combatMode,
                GMId = gmId,
                CurrentRound = CurrentRound,
                CurrentCombatantId = CurrentCombatant?.CombatantId ?? string.Empty,
                IsActive = IsActive,
                Combatants = Combatants == null
                    ? new List<Combatant>()
                    : new List<Combatant>(Combatants),
                AllActions = new List<TurnAction>(allActions),
                PendingActions = new List<TurnAction>(pendingActions),
                ActionHistory = turnTracker == null
                    ? new List<TurnAction>()
                    : new List<TurnAction>(turnTracker.ActionHistory)
            };
        }

        /// Restore a combat state service from a persisted snapshot.
        public static CombatStateService RestoreFromPersistenceSnapshot(CombatPersistenceSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var restored = CreateForEncounter(snapshot.EncounterId, snapshot.CombatMode, snapshot.GMId);
            restored.turnTracker.InitializeFromState(
                snapshot.Combatants,
                snapshot.CurrentRound,
                snapshot.CurrentCombatantId,
                snapshot.ActionHistory);
            restored.allActions = snapshot.AllActions == null
                ? new List<TurnAction>()
                : new List<TurnAction>(snapshot.AllActions);
            restored.pendingActions = snapshot.PendingActions == null
                ? new List<TurnAction>()
                : new List<TurnAction>(snapshot.PendingActions);
            return restored;
        }
        
        private bool ValidateActionCost(TurnActionType actionType, out string error)
        {
            error = string.Empty;
            var currentCombatant = CurrentCombatant;
            
            if (currentCombatant == null)
            {
                error = "No current combatant";
                return false;
            }
            
            switch (actionType)
            {
                case TurnActionType.StandardAction:
                    if (!currentCombatant.HasStandardAction)
                    {
                        error = "No standard actions remaining";
                        return false;
                    }
                    return true;

                case TurnActionType.MoveAction:
                    if (!currentCombatant.HasMoveAction)
                    {
                        error = "No move actions remaining";
                        return false;
                    }
                    return true;

                case TurnActionType.SwiftAction:
                    if (!currentCombatant.HasSwiftAction)
                    {
                        error = "No swift actions remaining";
                        return false;
                    }
                    return true;

                case TurnActionType.FullRoundAction:
                    if (currentCombatant.StandardActionsRemaining < 1 || currentCombatant.MoveActionsRemaining < 1)
                    {
                        error = "Cannot perform full-round action";
                        return false;
                    }
                    return true;

                case TurnActionType.FreeAction:
                default:
                    return true;
            }
        }
        
        private void ConsumeActionCost(TurnActionType actionType)
        {
            var current = CurrentCombatant;
            if (current == null) return;
            
            switch (actionType)
            {
                case TurnActionType.StandardAction:
                    current.TryUseStandardAction();
                    break;
                case TurnActionType.MoveAction:
                    current.TryUseMoveAction();
                    break;
                case TurnActionType.SwiftAction:
                    current.TryUseSwiftAction();
                    break;
                case TurnActionType.FullRoundAction:
                    current.TryUseStandardAction();
                    current.TryUseMoveAction();
                    break;
            }
        }
        
        private void ConsumeActionCostForAction(TurnActionType actionType)
        {
            ConsumeActionCost(actionType);
        }
    }
    
    /// Request to perform an action during a turn.
    public sealed class TurnActionRequest
    {
        public string CombatantId = string.Empty;
        public string ActionName = string.Empty;
        public string Description = string.Empty;
        public TurnActionType ActionType = TurnActionType.StandardAction;
        public string TargetCombatantId = string.Empty;  // Optional: for targeted actions
    }
    
    /// Result of action resolution.
    public sealed class ActionResolution
    {
        public TurnAction Action { get; set; }
        public bool RequiresGMApproval { get; set; }
        public bool IsAutoResolved { get; set; }
        public bool Succeeded { get; set; }
        public string ResultMessage { get; set; } = string.Empty;
    }

    [Serializable]
    public sealed class CombatPersistenceSnapshot
    {
        public string EncounterId = string.Empty;
        public CombatMode CombatMode = CombatMode.GMGuided;
        public string GMId = string.Empty;
        public int CurrentRound;
        public string CurrentCombatantId = string.Empty;
        public bool IsActive;
        public List<Combatant> Combatants = new List<Combatant>();
        public List<TurnAction> AllActions = new List<TurnAction>();
        public List<TurnAction> PendingActions = new List<TurnAction>();
        public List<TurnAction> ActionHistory = new List<TurnAction>();
    }
}
