using System;
using System.Collections.Generic;
using Elysium.Combat;

namespace Elysium.Networking
{
    [Serializable]
    public sealed class CombatNetworkSnapshot
    {
        public string EncounterId = string.Empty;
        public CombatMode CombatMode = CombatMode.GMGuided;
        public string GMId = string.Empty;
        public bool IsActive;
        public int CurrentRound;
        public string CurrentCombatantId = string.Empty;
        public string CurrentCombatantName = string.Empty;
        public long PublishedAtUtc;
        public string StatusMessage = string.Empty;
        public List<CombatantNetworkState> Combatants = new List<CombatantNetworkState>();
        public List<PendingCombatActionState> PendingActions = new List<PendingCombatActionState>();

        public static CombatNetworkSnapshot FromCombatState(
            CombatStateService combatState,
            string statusMessage = "")
        {
            var snapshot = new CombatNetworkSnapshot
            {
                EncounterId = combatState?.EncounterId ?? string.Empty,
                CombatMode = combatState?.CombatMode ?? CombatMode.GMGuided,
                GMId = combatState?.GMId ?? string.Empty,
                IsActive = combatState?.IsActive ?? false,
                CurrentRound = combatState?.CurrentRound ?? 0,
                CurrentCombatantId = combatState?.CurrentCombatant?.CombatantId ?? string.Empty,
                CurrentCombatantName = combatState?.CurrentCombatant?.ActorName ?? string.Empty,
                PublishedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                StatusMessage = statusMessage ?? string.Empty,
            };

            if (combatState == null)
            {
                return snapshot;
            }

            var combatants = combatState.Combatants;
            if (combatants != null)
            {
                foreach (var combatant in combatants)
                {
                    snapshot.Combatants.Add(new CombatantNetworkState
                    {
                        CombatantId = combatant.CombatantId,
                        ActorName = combatant.ActorName,
                        CharacterId = combatant.CharacterId,
                        InitiativeRoll = combatant.InitiativeRoll,
                        TurnOrder = combatant.TurnOrder,
                        HitPointsCurrent = combatant.HitPointsCurrent,
                        HitPointsMax = combatant.HitPointsMax,
                        IsDefeated = combatant.IsDefeated,
                        IsStunned = combatant.IsStunned,
                        StandardActionsRemaining = combatant.StandardActionsRemaining,
                        MoveActionsRemaining = combatant.MoveActionsRemaining,
                        SwiftActionsRemaining = combatant.SwiftActionsRemaining,
                        FreeActionsRemaining = combatant.FreeActionsRemaining,
                        ActiveConditions = new List<string>(combatant.ActiveConditions)
                    });
                }
            }

            foreach (var action in combatState.PendingActions)
            {
                snapshot.PendingActions.Add(new PendingCombatActionState
                {
                    ActionId = action.ActionId,
                    CombatantId = action.CombatantId,
                    CombatantName = FindCombatantName(combatants, action.CombatantId),
                    TargetCombatantId = action.TargetCombatantId,
                    ActionType = action.ActionType,
                    ActionName = action.ActionName,
                    Description = action.ActionDescription,
                });
            }

            return snapshot;
        }

        private static string FindCombatantName(IReadOnlyList<Combatant> combatants, string combatantId)
        {
            if (combatants == null)
            {
                return string.Empty;
            }

            for (var i = 0; i < combatants.Count; i++)
            {
                if (combatants[i].CombatantId == combatantId)
                {
                    return combatants[i].ActorName;
                }
            }

            return string.Empty;
        }
    }

    [Serializable]
    public sealed class CombatantNetworkState
    {
        public string CombatantId = string.Empty;
        public string ActorName = string.Empty;
        public string CharacterId = string.Empty;
        public int InitiativeRoll;
        public int TurnOrder;
        public int HitPointsCurrent;
        public int HitPointsMax;
        public bool IsDefeated;
        public bool IsStunned;
        public int StandardActionsRemaining;
        public int MoveActionsRemaining;
        public int SwiftActionsRemaining;
        public int FreeActionsRemaining;
        public List<string> ActiveConditions = new List<string>();
    }

    [Serializable]
    public sealed class PendingCombatActionState
    {
        public string ActionId = string.Empty;
        public string CombatantId = string.Empty;
        public string CombatantName = string.Empty;
        public string TargetCombatantId = string.Empty;
        public TurnActionType ActionType = TurnActionType.StandardAction;
        public string ActionName = string.Empty;
        public string Description = string.Empty;
    }
}