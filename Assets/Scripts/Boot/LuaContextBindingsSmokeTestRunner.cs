using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Elysium.Combat;
using Elysium.Networking;
using Elysium.World.Lua;
using UnityEngine;

namespace Elysium.Boot
{
    /// Verifies Lua can query live combat and session context through LuaHostContext.
    public sealed class LuaContextBindingsSmokeTestRunner : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = false;

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        private readonly LuaRuntimeService runtimeService = new LuaRuntimeService();

        private void Start()
        {
            if (runOnStart)
            {
                RunSmokeTest();
            }
        }

        public void RunSmokeTest()
        {
            try
            {
                LastSummary = RunSmokeTestInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"Lua context bindings smoke test failed: {ex}");
            }
        }

        private string RunSmokeTestInternal()
        {
            var log = new StringBuilder();
            var session = new SessionService();
            Require(session.TryOpenSession("lua_session_001", "starter_forest_edge", out var error), error);
            Require(session.TryRegisterPlayer(new PlayerSessionRecord
            {
                PlayerId = "gm_001",
                DisplayName = "Alice",
                NetworkClientId = 0,
                Role = PlayerRole.GameMaster,
            }, out error), error);
            Require(session.TryRegisterPlayer(new PlayerSessionRecord
            {
                PlayerId = "player_001",
                DisplayName = "Bob",
                NetworkClientId = 1,
                Role = PlayerRole.Player,
            }, out error), error);
            Require(session.TryAssignCombatant("gm_001", "player_001", "combatant_bob", out error), error);
            Require(session.TryStartCombat("gm_001", out error), error);

            var combat = CombatStateService.CreateForEncounter("enc_lua_001", CombatMode.GMGuided, "gm_001");
            combat.InitializeCombat(new List<Combatant>
            {
                new Combatant
                {
                    CombatantId = "combatant_bob",
                    ActorName = "Bob",
                    CharacterId = "player_001",
                    InitiativeRoll = 18,
                    HitPointsCurrent = 24,
                    HitPointsMax = 24,
                    ArmorClass = 16,
                    ArmorClassTouch = 13,
                    ArmorClassFlatFooted = 14,
                },
                new Combatant
                {
                    CombatantId = "enemy_001",
                    ActorName = "Bandit Captain",
                    CharacterId = "npc_bandit_01",
                    InitiativeRoll = 11,
                    HitPointsCurrent = 18,
                    HitPointsMax = 18,
                    ArmorClass = 15,
                    ArmorClassTouch = 12,
                    ArmorClassFlatFooted = 13,
                }
            });

            var pending = combat.AttemptAction(new TurnActionRequest
            {
                CombatantId = "combatant_bob",
                ActionName = "Attack",
                Description = "Bob attacks the bandit captain.",
                ActionType = TurnActionType.StandardAction,
                TargetCombatantId = "enemy_001"
            }, out error);
            Require(pending != null, error);

            var runtimeLog = new List<string>();
            var context = new LuaHostContext
            {
                LogSink = message => runtimeLog.Add(message),
                CombatStateProvider = () => new LuaCombatStateSnapshot
                {
                    EncounterId = combat.EncounterId,
                    CombatMode = (int)combat.CombatMode,
                    GMId = combat.GMId,
                    IsActive = combat.IsActive,
                    CurrentRound = combat.CurrentRound,
                    CurrentCombatantId = combat.CurrentCombatant?.CombatantId ?? string.Empty,
                    CurrentCombatantName = combat.CurrentCombatant?.ActorName ?? string.Empty,
                    PendingActionCount = combat.PendingActions.Count,
                },
                SessionStateProvider = () =>
                {
                    var connectedCount = 0;
                    foreach (var player in session.Players)
                    {
                        if (player.IsConnected)
                        {
                            connectedCount++;
                        }
                    }

                    return new LuaSessionStateSnapshot
                    {
                        SessionId = session.SessionId,
                        WorldProjectId = session.WorldProjectId,
                        State = (int)session.State,
                        GMPlayerId = session.GMPlayerId,
                        PlayerCount = session.Players.Count,
                        ConnectedCount = connectedCount,
                    };
                },
                CombatantOwnerProvider = combatantId =>
                {
                    var owner = session.GetCombatantOwner(combatantId);
                    return owner == null
                        ? new LuaPlayerBindingSnapshot()
                        : new LuaPlayerBindingSnapshot
                        {
                            PlayerId = owner.PlayerId,
                            DisplayName = owner.DisplayName,
                            RoleName = owner.Role.ToString(),
                            AssignedCombatantId = owner.AssignedCombatantId,
                            IsConnected = owner.IsConnected,
                            IsGM = owner.IsGM,
                        };
                },
                PlayerProvider = playerId =>
                {
                    var player = session.GetPlayer(playerId);
                    return player == null
                        ? new LuaPlayerBindingSnapshot()
                        : new LuaPlayerBindingSnapshot
                        {
                            PlayerId = player.PlayerId,
                            DisplayName = player.DisplayName,
                            RoleName = player.Role.ToString(),
                            AssignedCombatantId = player.AssignedCombatantId,
                            IsConnected = player.IsConnected,
                            IsGM = player.IsGM,
                        };
                }
            };

            var scriptPath = Path.Combine(Application.temporaryCachePath, "lua_context_bindings_smoke.lua");
            File.WriteAllText(scriptPath,
@"-- @id: smoke.lua.context.bindings
-- @attachment: World
-- @capabilities: rules.query.combat, session.read, debug.log

function on_world_loaded(context, actor)
    context:log('session=' .. context:get_session_id())
    context:log('world=' .. context:get_world_project_id())
    context:log('state=' .. tostring(context:get_session_state_index()))
    context:log('gm=' .. context:get_gm_player_id())
    context:log('players=' .. tostring(context:get_player_count()))
    context:log('connected=' .. tostring(context:get_connected_player_count()))
    context:log('active=' .. tostring(context:is_combat_active()))
    context:log('round=' .. tostring(context:get_current_round()))
    context:log('current=' .. context:get_current_combatant_name())
    context:log('pending=' .. tostring(context:get_pending_action_count()))
    context:log('owner=' .. context:get_combatant_owner_player_id('combatant_bob'))
    context:log('role=' .. context:get_player_role_name('gm_001'))
end
");

            var scriptReference = new LuaScriptReference
            {
                Id = "smoke.lua.context.bindings",
                RelativePath = scriptPath,
                AttachmentKind = LuaAttachmentKind.World,
            };
            scriptReference.Capabilities.Add("rules.query.combat");
            scriptReference.Capabilities.Add("session.read");
            scriptReference.Capabilities.Add("debug.log");

            var policy = new LuaSandboxPolicy
            {
                AllowWorldRead = true,
                AllowCombatRead = true,
                AllowSessionRead = true,
                AllowDebugLog = true,
            };

            var execution = runtimeService.Execute(
                scriptPath,
                "on_world_loaded",
                context,
                new LuaExecutionActor { Name = "SmokeTester" },
                scriptReference,
                policy);

            Require(execution.Success, execution.Error);
            Require(execution.UsedMoonSharp, "MoonSharp execution is required for Lua context bindings.");
            Require(Contains(runtimeLog, "session=lua_session_001"), "Lua did not receive session id.");
            Require(Contains(runtimeLog, "players=2"), "Lua did not receive player count.");
            Require(Contains(runtimeLog, "current=Bob"), "Lua did not receive current combatant.");
            Require(Contains(runtimeLog, "pending=1"), "Lua did not receive pending action count.");
            Require(Contains(runtimeLog, "owner=player_001"), "Lua did not resolve combatant owner.");
            Require(Contains(runtimeLog, "role=GameMaster"), "Lua did not resolve player role.");

            log.AppendLine("=== Lua Context Bindings Smoke Test ===");
            log.AppendLine("MoonSharp path: active");
            for (var i = 0; i < runtimeLog.Count; i++)
            {
                log.AppendLine($"  [Lua] {runtimeLog[i]}");
            }
            log.AppendLine("=== Smoke Test Complete ===");
            return log.ToString();
        }

        private static void Require(bool condition, string error)
        {
            if (!condition)
            {
                throw new InvalidOperationException(error);
            }
        }

        private static bool Contains(List<string> log, string expected)
        {
            for (var i = 0; i < log.Count; i++)
            {
                if (string.Equals(log[i], expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}