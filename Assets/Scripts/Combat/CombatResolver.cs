using System;
using System.Collections.Generic;
using System.Linq;
using Elysium.Characters;
using UnityEngine;

namespace Elysium.Combat
{
    /// Core PF1e combat mechanics resolver.
    /// Handles initiative, attack rolls, saves, damage, and action economy.
    public sealed class CombatResolver
    {
        private static System.Random s_random = new System.Random();
        
        /// Roll d20 for various checks (attack, save, skill).
        public static int RollD20()
        {
            return s_random.Next(1, 21);  // 1-20 inclusive
        }
        
        /// Roll an arbitrary die.
        public static int RollDie(int sides)
        {
            return s_random.Next(1, sides + 1);
        }
        
        /// Roll initiative for a character.
        public static InitiativeRoll RollInitiative(string actorId, string actorName, int dexterityModifier = 0)
        {
            return new InitiativeRoll
            {
                ActorId = actorId,
                ActorName = actorName,
                DieResult = RollD20(),
                InitiativeBonus = dexterityModifier
            };
        }
        
        /// Sort initiative results by total, descending (highest first = acts first).
        public static List<InitiativeRoll> SortInitiative(List<InitiativeRoll> rolls)
        {
            var sorted = rolls.OrderByDescending(r => r.TotalInitiative).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                sorted[i].InitiativeOrder = i;
            }
            return sorted;
        }
        
        /// Perform a melee or ranged attack roll.
        public static AttackRoll PerformAttack(
            string attackerId,
            string defenderId,
            CharacterRecord attacker,
            CharacterRecord defender,
            int weaponDamageBonus = 0,
            DamageType damageType = DamageType.Bludgeoning,
            bool isTouchAttack = false,
            bool isFlatFooted = false)
        {
            var attackBonus = attacker.BaseAttackBonus + attacker.GetAbilityModifier(Characters.PF1eAbility.Strength);
            
            var targetAC = isTouchAttack 
                ? defender.ArmorClassTouch 
                : (isFlatFooted ? defender.ArmorClassFlatFooted : defender.ArmorClass);
            
            var roll = new AttackRoll
            {
                AttackerId = attackerId,
                DefenderId = defenderId,
                AttackDieResult = RollD20(),
                AttackBonus = attackBonus,
                DefenderAC = targetAC,
                DamageBaseResult = RollD20(),  // Placeholder: should be weapon-specific die
                DamageBonus = attacker.GetAbilityModifier(Characters.PF1eAbility.Strength) + weaponDamageBonus,
                CriticalThreatRange = attacker.CriticalThreatRange,
                CriticalMultiplier = attacker.CriticalMultiplier,
                IsTouchAttack = isTouchAttack,
                IsFlatFooted = isFlatFooted,
                DamageType = damageType
            };
            
            // If critical threat, would need to roll to confirm (not automated here)
            return roll;
        }
        
        /// Perform a saving throw.
        public static SavingThrow PerformSavingThrow(
            string actorId,
            string actorName,
            CharacterRecord character,
            SavingThrowType saveType,
            int difficultyClass = 10,
            int circumstanceBonus = 0)
        {
            var saveBonus = saveType switch
            {
                SavingThrowType.Fortitude => character.SaveFortitude + character.GetAbilityModifier(Characters.PF1eAbility.Constitution),
                SavingThrowType.Reflex => character.SaveReflex + character.GetAbilityModifier(Characters.PF1eAbility.Dexterity),
                SavingThrowType.Will => character.SaveWill + character.GetAbilityModifier(Characters.PF1eAbility.Wisdom),
                _ => 0
            };
            
            return new SavingThrow
            {
                ActorId = actorId,
                ActorName = actorName,
                SaveType = saveType,
                DieResult = RollD20(),
                SaveBonus = saveBonus + circumstanceBonus,
                DifficultyClass = difficultyClass
            };
        }
        
        /// Perform a skill check.
        public static SkillCheck PerformSkillCheck(
            string actorId,
            string actorName,
            CharacterRecord character,
            string skillName,
            int difficultyClass = 10,
            int circumstanceBonus = 0)
        {
            var skillRank = character.SkillRanks.FirstOrDefault(s => s.SkillName == skillName);
            var skillBonus = skillRank?.GetBonus(GetGoverningAbility(skillName), character) ?? 0;
            
            return new SkillCheck
            {
                ActorId = actorId,
                ActorName = actorName,
                SkillName = skillName,
                DieResult = RollD20(),
                SkillBonus = skillBonus + circumstanceBonus,
                DifficultyClass = difficultyClass
            };
        }
        
        /// Get the governing ability for a skill (simplified version).
        private static Characters.PF1eAbility GetGoverningAbility(string skillName)
        {
            return skillName switch
            {
                "Acrobatics" => Characters.PF1eAbility.Dexterity,
                "Appraise" => Characters.PF1eAbility.Intelligence,
                "Bluff" => Characters.PF1eAbility.Charisma,
                "Climb" => Characters.PF1eAbility.Strength,
                "Craft" => Characters.PF1eAbility.Intelligence,
                "Diplomacy" => Characters.PF1eAbility.Charisma,
                "Disable Device" => Characters.PF1eAbility.Dexterity,
                "Disguise" => Characters.PF1eAbility.Charisma,
                "Escape Artist" => Characters.PF1eAbility.Dexterity,
                "Fly" => Characters.PF1eAbility.Dexterity,
                "Handle Animal" => Characters.PF1eAbility.Charisma,
                "Heal" => Characters.PF1eAbility.Wisdom,
                "Intimidate" => Characters.PF1eAbility.Charisma,
                "Knowledge" => Characters.PF1eAbility.Intelligence,
                "Linguistics" => Characters.PF1eAbility.Intelligence,
                "Perception" => Characters.PF1eAbility.Wisdom,
                "Perform" => Characters.PF1eAbility.Charisma,
                "Profession" => Characters.PF1eAbility.Wisdom,
                "Ride" => Characters.PF1eAbility.Dexterity,
                "Sense Motive" => Characters.PF1eAbility.Wisdom,
                "Sleight of Hand" => Characters.PF1eAbility.Dexterity,
                "Spellcraft" => Characters.PF1eAbility.Intelligence,
                "Stealth" => Characters.PF1eAbility.Dexterity,
                "Survival" => Characters.PF1eAbility.Wisdom,
                "Swim" => Characters.PF1eAbility.Strength,
                "Use Magic Device" => Characters.PF1eAbility.Charisma,
                _ => Characters.PF1eAbility.Intelligence
            };
        }
        
        /// Apply damage to a character.
        public static void ApplyDamage(CharacterRecord character, int damageAmount)
        {
            character.HitPointsCurrent = Mathf.Max(character.HitPointsCurrent - damageAmount, -10);
        }
        
        /// Heal a character.
        public static void HealDamage(CharacterRecord character, int healAmount)
        {
            character.HitPointsCurrent = Mathf.Min(character.HitPointsCurrent + healAmount, character.HitPointsMax);
        }
        
        /// Check if character is alive.
        public static bool IsAlive(CharacterRecord character)
        {
            return character.HitPointsCurrent > 0;
        }
        
        /// Check if character is conscious (for stabilization vs death).
        public static bool IsConscious(CharacterRecord character)
        {
            return character.HitPointsCurrent > -1;
        }
    }
}
