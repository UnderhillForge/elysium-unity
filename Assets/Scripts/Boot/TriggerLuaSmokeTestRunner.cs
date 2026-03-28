using System;
using System.IO;
using Elysium.World.Lua;
using UnityEngine;

namespace Elysium.Boot
{
    public sealed class TriggerLuaSmokeTestRunner : MonoBehaviour
    {
        [SerializeField] private string worldProjectFolder = "starter_forest_edge";
        [SerializeField] private string areaId = "area_forest_edge";
        [SerializeField] private string triggerId = "trigger_bandit_ambush";
        [SerializeField] private string actorName = "SmokeTester";
        [SerializeField] private bool runOnStart;

        [Header("Policy")]
        [SerializeField] private LuaSandboxPolicy policy = new LuaSandboxPolicy { AllowEncounterControl = true };

        public string LastSummary { get; private set; } = "Not run.";
        public bool LastSuccess { get; private set; }
        public string LastEncounterId { get; private set; } = string.Empty;

        private readonly LuaRuntimeService runtimeService = new LuaRuntimeService();

        private void Start()
        {
            if (runOnStart)
            {
                RunTriggerSmokeTest();
            }
        }

        [ContextMenu("Run Trigger Lua Smoke Test")]
        public void RunTriggerSmokeTest()
        {
            LastEncounterId = string.Empty;

            var triggerFile = Path.Combine(
                Application.streamingAssetsPath,
                "WorldProjects",
                worldProjectFolder,
                "Areas",
                areaId,
                "triggers.json");

            if (!File.Exists(triggerFile))
            {
                LastSuccess = false;
                LastSummary = $"Missing trigger file: {triggerFile}";
                Debug.LogError($"[Elysium] {LastSummary}");
                return;
            }

            var json = File.ReadAllText(triggerFile);
            var triggerCollection = JsonUtility.FromJson<TriggerCollection>(json);
            if (triggerCollection?.triggers == null)
            {
                LastSuccess = false;
                LastSummary = "Failed to parse triggers.json.";
                Debug.LogError($"[Elysium] {LastSummary}");
                return;
            }

            TriggerData selected = null;
            for (var i = 0; i < triggerCollection.triggers.Length; i++)
            {
                var candidate = triggerCollection.triggers[i];
                if (string.Equals(candidate.id, triggerId, StringComparison.OrdinalIgnoreCase))
                {
                    selected = candidate;
                    break;
                }
            }

            if (selected == null)
            {
                LastSuccess = false;
                LastSummary = $"Trigger not found: {triggerId}";
                Debug.LogError($"[Elysium] {LastSummary}");
                return;
            }

            var projectRoot = Path.Combine(Application.streamingAssetsPath, "WorldProjects", worldProjectFolder);
            var scriptReference = LuaMetadataParser.ParseScriptReference(projectRoot, selected.onEnterLua);
            var scriptPath = Path.Combine(projectRoot, selected.onEnterLua.Replace('/', Path.DirectorySeparatorChar));

            var context = new LuaHostContext
            {
                LogSink = message => Debug.Log($"[Elysium][Lua] {message}"),
                EncounterStarter = encounterId =>
                {
                    LastEncounterId = encounterId;
                    Debug.Log($"[Elysium] Encounter requested by Lua: {encounterId}");
                },
            };

            var actor = new LuaExecutionActor { Name = actorName };
            var execution = runtimeService.Execute(scriptPath, "on_enter", context, actor, scriptReference, policy);

            LastSuccess = execution.Success;
            LastSummary = execution.Success
                ? $"Trigger Lua succeeded for '{triggerId}'. Encounter: {LastEncounterId}"
                : $"Trigger Lua failed: {execution.Error}";

            if (!string.IsNullOrWhiteSpace(execution.Warning))
            {
                Debug.LogWarning($"[Elysium] {execution.Warning}");
            }

            if (execution.Success)
            {
                Debug.Log($"[Elysium] {LastSummary}");
            }
            else
            {
                Debug.LogError($"[Elysium] {LastSummary}");
            }
        }

        [Serializable]
        private sealed class TriggerCollection
        {
            public TriggerData[] triggers;
        }

        [Serializable]
        private sealed class TriggerData
        {
            public string id;
            public string onEnterLua;
        }
    }
}