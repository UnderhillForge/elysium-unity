using System;
using System.Text;
using Elysium.Boot;
using UnityEditor;
using UnityEngine;

namespace Elysium.Editor
{
    public static class SmokeBatchRunner
    {
        public static void RunCoreSmokeSuite()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Elysium Core Smoke Suite (Batch) ===");

            var root = new GameObject("SmokeBatchRunner");
            try
            {
                Run<SessionSmokeTestRunner>(root, r => r.RunSessionSmokeTest(), "Session", log);
                Run<CombatSmokeTestRunner>(root, r => r.RunCombatSmokeTest(), "Combat", log);
                Run<LuaContextBindingsSmokeTestRunner>(root, r => r.RunSmokeTest(), "LuaContextBindings", log);
                Run<CampaignPersistenceSmokeTestRunner>(root, r => r.RunPersistenceSmokeTest(), "Persistence", log);
                Run<WorldPackagePortabilitySmokeTestRunner>(root, r => r.RunPortabilitySmokeTest(), "PackagingPortability", log);

                Debug.Log(log.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogError(log.ToString());
                Debug.LogError($"Smoke suite failed: {ex}");
                EditorApplication.Exit(1);
                return;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            EditorApplication.Exit(0);
        }

        private static void Run<T>(GameObject root, Action<T> execute, string name, StringBuilder log)
            where T : MonoBehaviour
        {
            var runner = root.AddComponent<T>();
            execute(runner);

            var successProp = typeof(T).GetProperty("LastSuccess");
            var summaryProp = typeof(T).GetProperty("LastSummary");

            var success = successProp != null && successProp.PropertyType == typeof(bool)
                ? (bool)successProp.GetValue(runner)
                : false;
            var summary = summaryProp?.GetValue(runner) as string ?? string.Empty;

            log.AppendLine($"[{name}] {(success ? "PASS" : "FAIL")}");
            if (!string.IsNullOrEmpty(summary))
            {
                log.AppendLine(summary);
            }

            if (!success)
            {
                throw new InvalidOperationException($"Smoke runner '{name}' failed.");
            }
        }
    }
}
