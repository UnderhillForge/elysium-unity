using System;
using Elysium.Characters;
using Elysium.Combat;
using Elysium.Networking;

namespace Elysium.World.Lua
{
    public sealed class LuaHostContext
    {
        public Action<string> LogSink;
        public Action<string> EncounterStarter;
        
        // Combat actions for Lua scripts
        public Func<CharacterRecord, CharacterRecord, bool, AttackRoll> AttackRoller;  // (attacker, defender, isTouch)
        public Func<CharacterRecord, SavingThrowType, int, SavingThrow> SaveThrowRoller; // (actor, type, dc)
        public Func<CharacterRecord, string, int, SkillCheck> SkillChecker;  // (actor, skillName, dc)
        public Func<LuaCombatStateSnapshot> CombatStateProvider;
        public Func<LuaSessionStateSnapshot> SessionStateProvider;
        public Func<string, LuaPlayerBindingSnapshot> CombatantOwnerProvider;
        public Func<string, LuaPlayerBindingSnapshot> PlayerProvider;

        public void log(string message)
        {
            LogSink?.Invoke(message);
        }

        public void start_encounter(string encounterId)
        {
            EncounterStarter?.Invoke(encounterId);
        }
        
        /// Roll attack from attacker to defender.
        /// touch: if true, use touch AC; if false, use normal AC.
        public AttackRoll roll_attack(CharacterRecord attacker, CharacterRecord defender, bool touch = false)
        {
            if (AttackRoller == null)
            {
                log($"ERROR: Attack roller not available in Lua context");
                return null;
            }
            return AttackRoller.Invoke(attacker, defender, touch);
        }
        
        /// Roll saving throw.
        public SavingThrow roll_save(CharacterRecord actor, int saveTypeIndex, int dc)
        {
            if (SaveThrowRoller == null)
            {
                log($"ERROR: Save throw roller not available in Lua context");
                return null;
            }
            var saveType = (SavingThrowType)saveTypeIndex;  // 0=Fortitude, 1=Reflex, 2=Will
            return SaveThrowRoller.Invoke(actor, saveType, dc);
        }
        
        /// Roll skill check.
        public SkillCheck roll_skill(CharacterRecord actor, string skillName, int dc)
        {
            if (SkillChecker == null)
            {
                log($"ERROR: Skill checker not available in Lua context");
                return null;
            }
            return SkillChecker.Invoke(actor, skillName, dc);
        }
        
        /// Get ability modifier for a character.
        public int get_ability_mod(CharacterRecord actor, int abilityIndex)
        {
            var ability = (PF1eAbility)abilityIndex;  // 0=STR, 1=DEX, 2=CON, etc.
            return actor.GetAbilityModifier(ability);
        }
        
        /// Check if character is alive.
        public bool is_alive(CharacterRecord actor)
        {
            return CombatResolver.IsAlive(actor);
        }
        
        /// Apply damage to character.
        public void apply_damage(CharacterRecord actor, int amount)
        {
            CombatResolver.ApplyDamage(actor, amount);
        }
        
        /// Heal character.
        public void heal_damage(CharacterRecord actor, int amount)
        {
            CombatResolver.HealDamage(actor, amount);
        }

        /// Get the current combat state snapshot for Lua queries.
        public LuaCombatStateSnapshot get_combat_state()
        {
            return CombatStateProvider?.Invoke() ?? new LuaCombatStateSnapshot();
        }

        public bool is_combat_active()
        {
            return get_combat_state().IsActive;
        }

        public int get_current_round()
        {
            return get_combat_state().CurrentRound;
        }

        public string get_current_combatant_id()
        {
            return get_combat_state().CurrentCombatantId;
        }

        public string get_current_combatant_name()
        {
            return get_combat_state().CurrentCombatantName;
        }

        public int get_pending_action_count()
        {
            return get_combat_state().PendingActionCount;
        }

        public bool is_gm_guided_combat()
        {
            return get_combat_state().CombatMode == (int)CombatMode.GMGuided;
        }

        /// Get the current session state snapshot for Lua queries.
        public LuaSessionStateSnapshot get_session_state()
        {
            return SessionStateProvider?.Invoke() ?? new LuaSessionStateSnapshot();
        }

        public string get_session_id()
        {
            return get_session_state().SessionId;
        }

        public string get_world_project_id()
        {
            return get_session_state().WorldProjectId;
        }

        public string get_gm_player_id()
        {
            return get_session_state().GMPlayerId;
        }

        public int get_player_count()
        {
            return get_session_state().PlayerCount;
        }

        public int get_connected_player_count()
        {
            return get_session_state().ConnectedCount;
        }

        public int get_session_state_index()
        {
            return (int)get_session_state().State;
        }

        public LuaPlayerBindingSnapshot get_combatant_owner(string combatantId)
        {
            return CombatantOwnerProvider?.Invoke(combatantId) ?? new LuaPlayerBindingSnapshot();
        }

        public LuaPlayerBindingSnapshot get_player_binding(string playerId)
        {
            return PlayerProvider?.Invoke(playerId) ?? new LuaPlayerBindingSnapshot();
        }

        public string get_combatant_owner_player_id(string combatantId)
        {
            return get_combatant_owner(combatantId).PlayerId;
        }

        public string get_player_role_name(string playerId)
        {
            return get_player_binding(playerId).RoleName;
        }
    }

    [Serializable]
    public sealed class LuaCombatStateSnapshot
    {
        public string EncounterId = string.Empty;
        public int CombatMode = (int)Elysium.Combat.CombatMode.GMGuided;
        public string GMId = string.Empty;
        public bool IsActive;
        public int CurrentRound;
        public string CurrentCombatantId = string.Empty;
        public string CurrentCombatantName = string.Empty;
        public int PendingActionCount;
    }

    [Serializable]
    public sealed class LuaSessionStateSnapshot
    {
        public string SessionId = string.Empty;
        public string WorldProjectId = string.Empty;
        public int State = (int)SessionState.Idle;
        public string GMPlayerId = string.Empty;
        public int PlayerCount;
        public int ConnectedCount;
    }

    [Serializable]
    public sealed class LuaPlayerBindingSnapshot
    {
        public string PlayerId = string.Empty;
        public string DisplayName = string.Empty;
        public string RoleName = string.Empty;
        public string AssignedCombatantId = string.Empty;
        public bool IsConnected;
        public bool IsGM;
    }
}