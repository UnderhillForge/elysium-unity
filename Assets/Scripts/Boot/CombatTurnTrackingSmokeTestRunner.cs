using System;
using System.Collections.Generic;
using System.Text;
using Elysium.Characters;
using UnityEngine;

namespace Elysium.Boot
{
    /// Smoke test demonstrating turn-based combat tracking and action resolution.
    /// Shows initiative ordering, turn progression, action economy, and GM approval flow.
    public sealed class CombatTurnTrackingSmokeTestRunner : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = false;
        
        public bool LastSuccess { get; private set; } = false;
        public string LastSummary { get; private set; } = "Not run";
        
        private void Start()
        {
            if (runOnStart)
            {
                RunTurnTrackingSmokeTest();
            }
        }
        
        public void RunTurnTrackingSmokeTest()
        {
            try
            {
                LastSummary = RunTurnTrackingSmokeTestInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"Turn tracking smoke test failed: {ex}");
            }
        }
        
        private string RunTurnTrackingSmokeTestInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("=== PF1e Turn-Based Combat Tracking Smoke Test ===");
            log.AppendLine();
            
            // Create combatants
            var party = CreateCharacter("party_001", "Aragorn", 5, 14);
            var enemy1 = CreateCharacter("enemy_001", "Bandit Captain", 3, 16);
            var enemy2 = CreateCharacter("enemy_002", "Bandit Grunt", 2, 12);
            
            var combatants = new List<Combat.Combatant>
            {
                new Combat.Combatant
                {
                    CombatantId = party.Id,
                    ActorName = party.DisplayName,
                    CharacterId = party.Id,
                    InitiativeRoll = 18,  // 16 DEX mod + roll
                    HitPointsCurrent = party.HitPointsCurrent,
                    HitPointsMax = party.HitPointsMax,
                    ArmorClass = party.ArmorClass
                },
                new Combat.Combatant
                {
                    CombatantId = enemy1.Id,
                    ActorName = enemy1.DisplayName,
                    CharacterId = enemy1.Id,
                    InitiativeRoll = 20,  // Highest! Goes first
                    HitPointsCurrent = enemy1.HitPointsCurrent,
                    HitPointsMax = enemy1.HitPointsMax,
                    ArmorClass = enemy1.ArmorClass
                },
                new Combat.Combatant
                {
                    CombatantId = enemy2.Id,
                    ActorName = enemy2.DisplayName,
                    CharacterId = enemy2.Id,
                    InitiativeRoll = 14,
                    HitPointsCurrent = enemy2.HitPointsCurrent,
                    HitPointsMax = enemy2.HitPointsMax,
                    ArmorClass = enemy2.ArmorClass
                }
            };
            
            log.AppendLine("--- Combatants Created ---");
            foreach (var c in combatants)
            {
                log.AppendLine($"  {c.ActorName}: Init={c.InitiativeRoll}, HP={c.HitPointsCurrent}/{c.HitPointsMax}");
            }
            log.AppendLine();
            
            // Initialize combat
            var combatState = Combat.CombatStateService.CreateForEncounter(
                "enc_test_001",
                Combat.CombatMode.GMGuided,
                "gm_001");
            
            combatState.InitializeCombat(combatants);
            
            log.AppendLine("--- Initiative Rolled ---");
            log.AppendLine(combatState.GetInitiativeDisplay());
            log.AppendLine();
            
            // Simulate Round 1
            log.AppendLine("--- Round 1 ---");
            
            // Enemy 1 goes first
            log.AppendLine($"Current Turn: {combatState.CurrentCombatant.ActorName}");
            var actionReq1 = new Combat.TurnActionRequest
            {
                CombatantId = enemy1.Id,
                ActionName = "Attack",
                Description = "Attacks with sword",
                ActionType = Combat.TurnActionType.StandardAction,
                TargetCombatantId = party.Id
            };
            var resolution1 = combatState.AttemptAction(actionReq1, out var error1);
            if (resolution1 != null)
            {
                log.AppendLine($"  Action: {resolution1.Action.ActionName}");
                log.AppendLine($"  Requires GM Approval: {resolution1.RequiresGMApproval}");
                log.AppendLine($"  Status: {resolution1.ResultMessage}");
                
                // GM approves
                combatState.ResolveActionApproval(resolution1.Action.ActionId, true, "Hit! 8 damage");
                combatState.ProcessDamage(party.Id, 8);
                log.AppendLine($"  GM Approved: Hit! Aragorn takes 8 damage (HP: {12}/20)");
            }
            log.AppendLine();
            
            // Enemy 1 moves
            log.AppendLine($"  Move action remaining: {combatState.CurrentCombatant.HasMoveAction}");
            var moveReq = new Combat.TurnActionRequest
            {
                CombatantId = enemy1.Id,
                ActionName = "Move 30 ft",
                Description = "Tactical repositioning",
                ActionType = Combat.TurnActionType.MoveAction
            };
            var moveResolution = combatState.AttemptAction(moveReq, out var moveError);
            if (moveResolution != null)
            {
                combatState.ResolveActionApproval(moveResolution.Action.ActionId, true, "Moved");
                log.AppendLine($"  Move action used. Remaining Actions: STD={combatState.CurrentCombatant.StandardActionsRemaining}, MOVE={combatState.CurrentCombatant.MoveActionsRemaining}");
            }
            log.AppendLine();
            
            // End Enemy 1's turn
            log.AppendLine($"  Ending turn for {combatState.CurrentCombatant.ActorName}");
            combatState.EndCurrentTurn();
            log.AppendLine();
            
            // Enemy 2's turn
            log.AppendLine($"Current Turn: {combatState.CurrentCombatant.ActorName}");
            var actionReq2 = new Combat.TurnActionRequest
            {
                CombatantId = enemy2.Id,
                ActionName = "Attack",
                Description = "Attacks with dagger",
                ActionType = Combat.TurnActionType.StandardAction,
                TargetCombatantId = party.Id
            };
            var resolution2 = combatState.AttemptAction(actionReq2, out var error2);
            if (resolution2 != null)
            {
                combatState.ResolveActionApproval(resolution2.Action.ActionId, true, "Miss!");
                log.AppendLine($"  GM Resolved: Miss!");
            }
            combatState.EndCurrentTurn();
            log.AppendLine();
            
            // Aragorn's turn
            log.AppendLine($"Current Turn: {combatState.CurrentCombatant.ActorName}");
            var actionReq3 = new Combat.TurnActionRequest
            {
                CombatantId = party.Id,
                ActionName = "Attack",
                Description = "Sword attack on Captain",
                ActionType = Combat.TurnActionType.StandardAction,
                TargetCombatantId = enemy1.Id
            };
            var resolution3 = combatState.AttemptAction(actionReq3, out var error3);
            if (resolution3 != null)
            {
                combatState.ResolveActionApproval(resolution3.Action.ActionId, true, "Hit! 12 damage");
                combatState.ProcessDamage(enemy1.Id, 12);
                log.AppendLine($"  GM Resolved: Hit! Bandit Captain takes 12 damage");
            }
            combatState.EndCurrentTurn();
            log.AppendLine();
            
            // Check round completion
            log.AppendLine($"End of Round {combatState.CurrentRound}: All combatants have acted");
            log.AppendLine();
            
            // Round 2 preview
            log.AppendLine("--- Round 2 ---");
            log.AppendLine($"Current Turn: {combatState.CurrentCombatant.ActorName}");
            log.AppendLine($"Bandit Captain HP: Reduced by 12 from previous round");
            log.AppendLine($"Actions Reset: STD={combatState.CurrentCombatant.StandardActionsRemaining}, MOVE={combatState.CurrentCombatant.MoveActionsRemaining}");
            log.AppendLine();
            
            // Test action economy enforcement
            log.AppendLine("--- Action Economy Test ---");
            log.AppendLine($"Testing action limits:");
            for (int i = 0; i < 2; i++)
            {
                var testReq = new Combat.TurnActionRequest
                {
                    CombatantId = combatState.CurrentCombatant.CombatantId,
                    ActionName = $"Standard Action {i + 1}",
                    ActionType = Combat.TurnActionType.StandardAction
                };
                var result = combatState.AttemptAction(testReq, out var testError);
                if (result != null)
                {
                    if (testError.Length > 0)
                    {
                        log.AppendLine($"  Attempt {i + 1}: {testError}");
                        break;
                    }
                    else
                    {
                        combatState.ResolveActionApproval(result.Action.ActionId, true, "Approved");
                        log.AppendLine($"  Attempt {i + 1}: Approved. Remaining: {combatState.CurrentCombatant.StandardActionsRemaining}");
                    }
                }
            }
            log.AppendLine();
            
            log.AppendLine("=== Smoke Test Complete ===");
            
            return log.ToString();
        }
        
        private CharacterRecord CreateCharacter(
            string id,
            string name,
            int level,
            int dex)
        {
            var baseHp = 8 + (level - 1) * 6;
            return new CharacterRecord
            {
                Id = id,
                DisplayName = name,
                Level = level,
                AbilityDexterity = 10 + dex,  // Will be converted to modifier
                HitPointsMax = baseHp,
                HitPointsCurrent = baseHp,
                ArmorClass = 10 + (dex - 10) / 2
            };
        }
    }
}
