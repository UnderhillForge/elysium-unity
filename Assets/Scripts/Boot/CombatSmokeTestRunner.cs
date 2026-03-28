using System;
using System.Collections.Generic;
using System.Text;
using Elysium.Characters;
using Elysium.Combat;
using Elysium.World.Lua;
using UnityEngine;

namespace Elysium.Boot
{
    /// Smoke test demonstrating end-to-end PF1e combat resolution.
    /// Runs through initiative, attack rolls, damage, and Lua integration.
    public sealed class CombatSmokeTestRunner : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = false;
        
        public bool LastSuccess { get; private set; } = false;
        public string LastSummary { get; private set; } = "Not run";
        
        private void Start()
        {
            if (runOnStart)
            {
                RunCombatSmokeTest();
            }
        }
        
        public void RunCombatSmokeTest()
        {
            try
            {
                LastSummary = RunCombatSmokeTestInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"Combat smoke test failed: {ex}");
            }
        }
        
        private string RunCombatSmokeTestInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("=== PF1e Combat Smoke Test ===");
            
            // Create two combatants
            var partyLeader = CreateCharacter("leader_001", "Aragorn", 5, 10, 14, 8, 12, 13);
            var bandit = CreateCharacter("enemy_001", "Bandit", 3, 12, 13, 9, 10, 10);
            
            log.AppendLine($"Party Leader: {partyLeader.DisplayName} (HP: {partyLeader.HitPointsCurrent}/{partyLeader.HitPointsMax}, AC: {partyLeader.ArmorClass})");
            log.AppendLine($"Enemy: {bandit.DisplayName} (HP: {bandit.HitPointsCurrent}/{bandit.HitPointsMax}, AC: {bandit.ArmorClass})");
            log.AppendLine();
            
            // Roll initiative
            log.AppendLine("--- Initiative Phase ---");
            var initiativeRolls = new List<InitiativeRoll>
            {
                CombatResolver.RollInitiative("party_001", partyLeader.DisplayName, partyLeader.GetAbilityModifier(PF1eAbility.Dexterity)),
                CombatResolver.RollInitiative("enemy_001", bandit.DisplayName, bandit.GetAbilityModifier(PF1eAbility.Dexterity))
            };
            
            var sortedInitiative = CombatResolver.SortInitiative(initiativeRolls);
            foreach (var roll in sortedInitiative)
            {
                log.AppendLine($"  {roll}");
            }
            log.AppendLine($"Turn order: {sortedInitiative[0].ActorName} acts first");
            log.AppendLine();
            
            // Combat round: party leader attacks
            log.AppendLine("--- Combat Round 1 ---");
            log.AppendLine($"{partyLeader.DisplayName}'s turn");
            
            var attack1 = CombatResolver.PerformAttack("party_001", "enemy_001", partyLeader, bandit, weaponDamageBonus: 2);
            log.AppendLine($"  Attack: {attack1}");
            
            if (attack1.IsHit)
            {
                var finalDamage = attack1.IsCriticalThreat ? attack1.CriticalDamage : attack1.TotalDamage;
                CombatResolver.ApplyDamage(bandit, finalDamage);
                log.AppendLine($"  Damage: {finalDamage}");
                log.AppendLine($"  Bandit HP: {bandit.HitPointsCurrent}/{bandit.HitPointsMax}");
            }
            else
            {
                log.AppendLine("  Miss!");
            }
            log.AppendLine();
            
            // Enemy turn: bandit attacks
            log.AppendLine($"{bandit.DisplayName}'s turn");
            var attack2 = CombatResolver.PerformAttack("enemy_001", "party_001", bandit, partyLeader, weaponDamageBonus: 1);
            log.AppendLine($"  Attack: {attack2}");
            
            if (attack2.IsHit)
            {
                var finalDamage = attack2.IsCriticalThreat ? attack2.CriticalDamage : attack2.TotalDamage;
                CombatResolver.ApplyDamage(partyLeader, finalDamage);
                log.AppendLine($"  Damage: {finalDamage}");
                log.AppendLine($"  {partyLeader.DisplayName} HP: {partyLeader.HitPointsCurrent}/{partyLeader.HitPointsMax}");
            }
            else
            {
                log.AppendLine("  Miss!");
            }
            log.AppendLine();
            
            // Saving throw test
            log.AppendLine("--- Saving Throw Test ---");
            var save = CombatResolver.PerformSavingThrow("enemy_001", bandit.DisplayName, bandit, SavingThrowType.Will, difficultyClass: 14);
            log.AppendLine($"  {save}");
            log.AppendLine();
            
            // Skill check test
            log.AppendLine("--- Skill Check Test ---");
            var skill = CombatResolver.PerformSkillCheck("party_001", partyLeader.DisplayName, partyLeader, "Perception", difficultyClass: 15);
            log.AppendLine($"  {skill}");
            log.AppendLine();
            
            // Combat states
            log.AppendLine("--- Status Check ---");
            log.AppendLine($"  {partyLeader.DisplayName} alive: {CombatResolver.IsAlive(partyLeader)}");
            log.AppendLine($"  {bandit.DisplayName} alive: {CombatResolver.IsAlive(bandit)}");
            log.AppendLine();
            
            // Lua integration simulation
            log.AppendLine("--- Lua Combat Integration ---");
            var luaContext = new LuaHostContext()
            {
                LogSink = (msg) => log.AppendLine($"  [Lua] {msg}"),
                AttackRoller = (atk, def, touch) => CombatResolver.PerformAttack(atk.Id, def.Id, atk, def, 2, DamageType.Slashing, touch),
                SaveThrowRoller = (act, type, dc) => CombatResolver.PerformSavingThrow(act.Id, act.DisplayName, act, type, dc),
                SkillChecker = (act, skill, dc) => CombatResolver.PerformSkillCheck(act.Id, act.DisplayName, act, skill, dc)
            };
            
            // Simulate Lua script calling combat functions
            var luaAttack = luaContext.roll_attack(partyLeader, bandit, false);
            log.AppendLine($"  Lua attack: {luaAttack}");
            
            var luaSave = luaContext.roll_save(bandit, (int)SavingThrowType.Reflex, 12);
            log.AppendLine($"  Lua save: {luaSave}");
            
            log.AppendLine();
            log.AppendLine("=== Smoke Test Complete ===");
            
            return log.ToString();
        }
        
        private CharacterRecord CreateCharacter(
            string id,
            string name,
            int level,
            int str,
            int dex,
            int con,
            int intel,
            int wis,
            int cha = 10)
        {
            var baseHp = 8 + (level - 1) * 6;  // d6 HD, simplified
            var dexMod = (dex - 10) / 2;
            
            return new CharacterRecord
            {
                Id = id,
                DisplayName = name,
                Level = level,
                AbilityStrength = str,
                AbilityDexterity = dex,
                AbilityConstitution = con,
                AbilityIntelligence = intel,
                AbilityWisdom = wis,
                AbilityCharisma = cha,
                HitPointsMax = baseHp,
                HitPointsCurrent = baseHp,
                ArmorClass = 10 + dexMod,           // Light leather armor default
                ArmorClassTouch = 10 + dexMod,
                ArmorClassFlatFooted = 10,
                BaseAttackBonus = level,
                SaveFortitude = (con - 10) / 2,
                SaveReflex = dexMod,
                SaveWill = (wis - 10) / 2
            };
        }
    }
}
