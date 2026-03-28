using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Elysium.World;
using Elysium.World.Lua;
using UnityEngine;

namespace Elysium.Boot
{
    /// Verifies capability-checked world mutation persistence and Lua world-write bindings.
    public sealed class WorldMutationSmokeTestRunner : MonoBehaviour
    {
        private const string WorldProjectFolder = "starter_forest_edge";
        private const string OwnerPlayerId = "gm.elysium.default";
        private const string StrangerPlayerId = "gm.stranger.smoke";

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        public void RunWorldMutationSmokeTest()
        {
            try
            {
                LastSummary = RunWorldMutationSmokeTestInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"World mutation smoke test failed: {ex}");
            }
        }

        private string RunWorldMutationSmokeTestInternal()
        {
            var log = new StringBuilder();
            var runtimeLog = new List<string>();
            var campaignDatabasePath = Path.Combine(
                Application.streamingAssetsPath,
                "WorldProjects",
                WorldProjectFolder,
                "Databases",
                "campaign.db");

            var service = new WorldMutationService(WorldProjectFolder, campaignDatabasePath);
            var writePolicy = new LuaSandboxPolicy
            {
                AllowWorldRead = true,
                AllowWorldWrite = true,
                AllowDebugLog = true,
            };

            var deniedPolicy = new LuaSandboxPolicy
            {
                AllowWorldRead = true,
                AllowWorldWrite = false,
                AllowDebugLog = true,
            };

            var readDeniedPolicy = new LuaSandboxPolicy
            {
                AllowWorldRead = false,
                AllowWorldWrite = true,
                AllowDebugLog = true,
            };

            var key = $"smoke.world.flag.{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

            Require(service.TryWriteState(OwnerPlayerId, key, "enabled", writePolicy, out var error), error);
            Require(service.TryReadState(key, writePolicy, out var loadedValue, out error), error);
            if (!string.Equals(loadedValue, "enabled", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"World state mismatch: expected 'enabled', got '{loadedValue}'.");
            }

            log.AppendLine("=== World Mutation Smoke Test ===");
            log.AppendLine($"Authorized mutation persisted: {key}={loadedValue} — ok");

            var unauthorizedRejected = !service.TryWriteState(StrangerPlayerId, key + ".unauthorized", "x", writePolicy, out var unauthorizedError);
            if (!unauthorizedRejected)
            {
                throw new InvalidOperationException("Non-owner world mutation should be rejected.");
            }
            log.AppendLine($"Non-owner mutation rejected: {unauthorizedError} — ok");

            var policyRejected = !service.TryWriteState(OwnerPlayerId, key + ".denied", "x", deniedPolicy, out var deniedError);
            if (!policyRejected)
            {
                throw new InvalidOperationException("Policy-denied world mutation should be rejected.");
            }
            log.AppendLine($"Policy-denied mutation rejected: {deniedError} — ok");

            var readDenied = !service.TryReadState(key, readDeniedPolicy, out _, out var readDeniedError);
            if (!readDenied)
            {
                throw new InvalidOperationException("Policy-denied world read should be rejected.");
            }
            log.AppendLine($"Policy-denied read rejected: {readDeniedError} — ok");

            var unauthorizedRead = !service.TryReadState(StrangerPlayerId, key, writePolicy, enforceOwnership: true, out _, out var unauthorizedReadError);
            if (!unauthorizedRead)
            {
                throw new InvalidOperationException("Ownership-enforced world read should reject non-owner requester.");
            }
            log.AppendLine($"Ownership-enforced read rejected: {unauthorizedReadError} — ok");

            var invalidKeyRejected = !service.TryWriteState(OwnerPlayerId, "invalid key with spaces", "x", writePolicy, out var invalidKeyError);
            if (!invalidKeyRejected)
            {
                throw new InvalidOperationException("Invalid world key should be rejected.");
            }
            log.AppendLine($"Invalid key rejected: {invalidKeyError} — ok");

            var context = new LuaHostContext
            {
                LogSink = message => runtimeLog.Add(message),
                WorldStateReader = stateKey =>
                {
                    return service.TryReadState(OwnerPlayerId, stateKey, writePolicy, enforceOwnership: true, out var value, out var readError)
                        ? value
                        : $"read_error:{readError}";
                },
                WorldStateWriter = (stateKey, value) =>
                {
                    return service.TryWriteState(OwnerPlayerId, stateKey, value, writePolicy, out var writeError)
                        ? string.Empty
                        : writeError;
                }
            };

            var scriptRoot = Path.Combine(Application.temporaryCachePath, "world_mutation_smoke");
            Directory.CreateDirectory(scriptRoot);
            var scriptPath = Path.Combine(scriptRoot, "world_mutation.lua");
            var luaKey = key + ".lua";
            File.WriteAllText(
                scriptPath,
                "function mutate_world(context, actor)\n"
                + "    local ok = context:set_world_state('" + luaKey + "', 'lua_value')\n"
                + "    context:log('write_ok=' .. tostring(ok))\n"
                + "    context:log('world_value=' .. context:get_world_state('" + luaKey + "'))\n"
                + "end\n");

            var scriptReference = new LuaScriptReference
            {
                Id = "smoke.world.mutation",
                RelativePath = scriptPath,
                AttachmentKind = LuaAttachmentKind.World,
            };
            scriptReference.Capabilities.Add("world.write");
            scriptReference.Capabilities.Add("debug.log");

            var runtime = new LuaRuntimeService();
            var execution = runtime.Execute(
                scriptPath,
                "mutate_world",
                context,
                new LuaExecutionActor { Name = "SmokeMutator" },
                scriptReference,
                writePolicy);

            Require(execution.Success, execution.Error);
            Require(Contains(runtimeLog, "write_ok=true"), "Lua write did not succeed.");
            Require(Contains(runtimeLog, "world_value=lua_value"), "Lua readback did not match persisted value.");
            log.AppendLine("Lua world mutation binding round-trip — ok");

            var deniedExecution = runtime.Execute(
                scriptPath,
                "mutate_world",
                context,
                new LuaExecutionActor { Name = "SmokeMutatorDenied" },
                scriptReference,
                deniedPolicy);

            if (deniedExecution.Success)
            {
                throw new InvalidOperationException("Lua mutation should fail when world.write is denied by policy.");
            }
            log.AppendLine($"Lua policy denial observed: {deniedExecution.Error} — ok");
            log.AppendLine("=== World Mutation Smoke Test COMPLETE ===");
            return log.ToString();
        }

        private static void Require(bool condition, string error)
        {
            if (!condition)
            {
                throw new InvalidOperationException(error);
            }
        }

        private static bool Contains(List<string> entries, string expected)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i], expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}