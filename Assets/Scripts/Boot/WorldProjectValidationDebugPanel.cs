using System.IO;
using Elysium.Packaging;
using Elysium.Persistence;
using Elysium.World;
using UnityEngine;

namespace Elysium.Boot
{
    public sealed class WorldProjectValidationDebugPanel : MonoBehaviour
    {
        [SerializeField] private ElysiumBootstrap bootstrap;
        [SerializeField] private WorldProjectValidationRunner runner;
        [SerializeField] private WorldPackageRunner packageRunner;
        [SerializeField] private TriggerLuaSmokeTestRunner triggerSmokeRunner;
        [SerializeField] private LuaContextBindingsSmokeTestRunner luaBindingsSmokeRunner;
        [SerializeField] private CombatSmokeTestRunner combatSmokeRunner;
        [SerializeField] private CombatTurnTrackingSmokeTestRunner turnTrackingSmokeRunner;
        [SerializeField] private CombatNetworkingSmokeTestRunner networkingSmokeRunner;
        [SerializeField] private SessionSmokeTestRunner sessionSmokeRunner;
        [SerializeField] private CampaignPersistenceSmokeTestRunner persistenceSmokeRunner;
        [SerializeField] private WorldSimulationTickSmokeTestRunner worldSimulationTickSmokeRunner;
        [SerializeField] private string simulationWorldProjectId = "starter_forest_edge";
        [SerializeField] private bool showPanel = true;
        [SerializeField] private bool showWhenNotPlaying = false;
        [SerializeField] private Rect panelRect = new Rect(16f, 16f, 520f, 950f);
        [SerializeField] private bool showPackageControls = true;
        [SerializeField] private bool showLuaSmokeControls = true;
        [SerializeField] private bool showCombatControls = true;
        [SerializeField] private bool showTurnTrackingControls = true;
        [SerializeField] private bool showNetworkingControls = true;
        [SerializeField] private bool showSessionControls = true;
        [SerializeField] private bool showPersistenceControls = true;
        [SerializeField] private bool showWorldSimulationControls = true;

        private Vector2 detailsScroll;
        private string worldSimulationRefreshSummary = "Not refreshed";
        private int worldSimulationTickCount = -1;
        private string worldSimulationLastUtc = string.Empty;
        private string worldSimulationLastArea = string.Empty;
        private string worldSimulationLastEvent = string.Empty;
        private string worldSimulationLastSessionState = string.Empty;

        private void Reset()
        {
            bootstrap = GetComponent<ElysiumBootstrap>();
            runner = GetComponent<WorldProjectValidationRunner>();
            packageRunner = GetComponent<WorldPackageRunner>();
            triggerSmokeRunner = GetComponent<TriggerLuaSmokeTestRunner>();
            luaBindingsSmokeRunner = GetComponent<LuaContextBindingsSmokeTestRunner>();
            combatSmokeRunner = GetComponent<CombatSmokeTestRunner>();
            turnTrackingSmokeRunner = GetComponent<CombatTurnTrackingSmokeTestRunner>();
            networkingSmokeRunner = GetComponent<CombatNetworkingSmokeTestRunner>();
            sessionSmokeRunner = GetComponent<SessionSmokeTestRunner>();
            persistenceSmokeRunner = GetComponent<CampaignPersistenceSmokeTestRunner>();
            worldSimulationTickSmokeRunner = GetComponent<WorldSimulationTickSmokeTestRunner>();
        }

        private void OnGUI()
        {
            if (!showPanel)
            {
                return;
            }

            if (!Application.isPlaying && !showWhenNotPlaying)
            {
                return;
            }

            panelRect = GUI.Window(
                GetInstanceID(),
                panelRect,
                DrawPanel,
                "Elysium World Validation");
        }

        private void DrawPanel(int windowId)
        {
            GUILayout.BeginVertical();

            if (runner == null)
            {
                GUILayout.Label("No runner assigned.");
                GUILayout.EndVertical();
                GUI.DragWindow();
                return;
            }

            var previousColor = GUI.color;
            GUI.color = GetStatusColor();
            GUILayout.Label($"Status: {(runner.HasRun ? (runner.LastIsValid ? "Pass" : "Fail") : "Not Run")}");
            GUI.color = previousColor;

            GUILayout.Label($"Errors: {runner.LastErrorCount}");
            GUILayout.Label($"Warnings: {runner.LastWarningCount}");
            GUILayout.Label(runner.LastSummary);

            GUILayout.Space(8f);

            if (GUILayout.Button("Run Validation", GUILayout.Height(28f)))
            {
                runner.RunValidation();
            }

            if (showPackageControls && packageRunner != null)
            {
                GUILayout.Space(6f);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Export .ewm", GUILayout.Height(26f)))
                {
                    packageRunner.ExportEwm();
                }

                if (GUILayout.Button("Import .ewm", GUILayout.Height(26f)))
                {
                    packageRunner.ImportEwm();
                }

                if (GUILayout.Button("Round-Trip Test", GUILayout.Height(26f)))
                {
                    packageRunner.RunRoundTripSmokeTest();
                }

                GUILayout.EndHorizontal();

                GUILayout.Label(packageRunner.LastExportSummary);
                GUILayout.Label(packageRunner.LastImportSummary);

                GUILayout.Space(4f);
                GUILayout.Label("Import Trust Status");

                DrawTrustLine(
                    "Integrity",
                    packageRunner.HasImportRun,
                    packageRunner.LastIntegrityVerified,
                    packageRunner.LastIntegritySkipped,
                    packageRunner.LastIntegritySummary);

                DrawTrustLine(
                    "Dependencies",
                    packageRunner.HasImportRun,
                    packageRunner.LastDependencyCompatible,
                    packageRunner.LastDependencySkipped,
                    packageRunner.LastDependencySummary);

                GUILayout.Space(4f);
                DrawRoundTripLine(packageRunner);
            }

            if (showLuaSmokeControls && triggerSmokeRunner != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label("Lua Trigger Smoke Test");

                // Show MoonSharp availability
                var moonSharpAvailable = IsMoonSharpAvailable();
                var moonSharpColor = GUI.color;
                GUI.color = moonSharpAvailable ? Color.green : new Color(1f, 0.35f, 0.35f);
                GUILayout.Label($"MoonSharp: {(moonSharpAvailable ? "Available" : "Required - Missing")}");
                GUI.color = moonSharpColor;

                if (GUILayout.Button("Run Trigger Smoke", GUILayout.Height(26f)))
                {
                    triggerSmokeRunner.RunTriggerSmokeTest();
                }

                var triggerStatusColor = GUI.color;
                GUI.color = triggerSmokeRunner.LastSuccess ? Color.green : new Color(1f, 0.35f, 0.35f);
                GUILayout.Label($"Smoke Status: {(triggerSmokeRunner.LastSuccess ? "Passed" : "Not Passed")}");
                GUI.color = triggerStatusColor;
                GUILayout.Label(triggerSmokeRunner.LastSummary);

                if (luaBindingsSmokeRunner != null)
                {
                    GUILayout.Space(4f);
                    GUILayout.Label("Lua Combat/Session Bindings");

                    if (GUILayout.Button("Run Lua Bindings Smoke", GUILayout.Height(26f)))
                    {
                        luaBindingsSmokeRunner.RunSmokeTest();
                    }

                    var bindingsPrevious = GUI.color;
                    GUI.color = luaBindingsSmokeRunner.LastSuccess ? Color.green : new Color(1f, 0.35f, 0.35f);
                    GUILayout.Label($"Bindings Status: {(luaBindingsSmokeRunner.LastSuccess ? "Passed" : "Not Passed")}");
                    GUI.color = bindingsPrevious;
                    GUILayout.Label(luaBindingsSmokeRunner.LastSummary);
                }
            }

            if (showCombatControls && combatSmokeRunner != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label("PF1e Combat Smoke Test");

                if (GUILayout.Button("Run Combat Smoke", GUILayout.Height(26f)))
                {
                    combatSmokeRunner.RunCombatSmokeTest();
                }

                var combatPrevious = GUI.color;
                GUI.color = combatSmokeRunner.LastSuccess ? Color.green : new Color(1f, 0.35f, 0.35f);
                GUILayout.Label($"Combat Status: {(combatSmokeRunner.LastSuccess ? "Passed" : "Not Passed")}");
                GUI.color = combatPrevious;
                GUILayout.Label(combatSmokeRunner.LastSummary);
            }

            if (showTurnTrackingControls && turnTrackingSmokeRunner != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label("Turn-Based Combat Tracking");

                if (GUILayout.Button("Run Turn Tracking Smoke", GUILayout.Height(26f)))
                {
                    turnTrackingSmokeRunner.RunTurnTrackingSmokeTest();
                }

                var turnPrevious = GUI.color;
                GUI.color = turnTrackingSmokeRunner.LastSuccess ? Color.green : new Color(1f, 0.35f, 0.35f);
                GUILayout.Label($"Turn Tracking Status: {(turnTrackingSmokeRunner.LastSuccess ? "Passed" : "Not Passed")}");
                GUI.color = turnPrevious;
                GUILayout.Label(turnTrackingSmokeRunner.LastSummary);
            }

            if (showNetworkingControls && networkingSmokeRunner != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label("Combat Networking (Host Authority)");

                if (GUILayout.Button("Run Networking Smoke", GUILayout.Height(26f)))
                {
                    networkingSmokeRunner.RunNetworkingSmokeTest();
                }

                var networkingPrevious = GUI.color;
                GUI.color = networkingSmokeRunner.LastSuccess ? Color.green : new Color(1f, 0.35f, 0.35f);
                GUILayout.Label($"Networking Status: {(networkingSmokeRunner.LastSuccess ? "Passed" : "Not Passed")}");
                GUI.color = networkingPrevious;
                GUILayout.Label(networkingSmokeRunner.LastSummary);
            }

            if (showSessionControls && sessionSmokeRunner != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label("Session Lifecycle");

                if (GUILayout.Button("Run Session Smoke", GUILayout.Height(26f)))
                {
                    sessionSmokeRunner.RunSessionSmokeTest();
                }

                var sessionPrevious = GUI.color;
                GUI.color = sessionSmokeRunner.LastSuccess ? Color.green : new Color(1f, 0.35f, 0.35f);
                GUILayout.Label($"Session Status: {(sessionSmokeRunner.LastSuccess ? "Passed" : "Not Passed")}");
                GUI.color = sessionPrevious;
                GUILayout.Label(sessionSmokeRunner.LastSummary);
            }

            if (showPersistenceControls && persistenceSmokeRunner != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label("Campaign Persistence");

                if (GUILayout.Button("Run Persistence Smoke", GUILayout.Height(26f)))
                {
                    persistenceSmokeRunner.RunPersistenceSmokeTest();
                }

                var persistencePrevious = GUI.color;
                GUI.color = persistenceSmokeRunner.LastSuccess ? Color.green : new Color(1f, 0.35f, 0.35f);
                GUILayout.Label($"Persistence Status: {(persistenceSmokeRunner.LastSuccess ? "Passed" : "Not Passed")}");
                GUI.color = persistencePrevious;
                GUILayout.Label(persistenceSmokeRunner.LastSummary);
            }

            if (showWorldSimulationControls)
            {
                GUILayout.Space(6f);
                GUILayout.Label("World Simulation Tick");

                GUILayout.BeginHorizontal();
                if (worldSimulationTickSmokeRunner != null
                    && GUILayout.Button("Run Simulation Smoke", GUILayout.Height(26f)))
                {
                    worldSimulationTickSmokeRunner.RunWorldSimulationTickSmokeTest();
                    RefreshWorldSimulationSnapshot();
                }

                if (GUILayout.Button("Refresh Simulation Snapshot", GUILayout.Height(26f)))
                {
                    RefreshWorldSimulationSnapshot();
                }

                GUILayout.EndHorizontal();

                if (worldSimulationTickSmokeRunner != null)
                {
                    var simulationPrevious = GUI.color;
                    GUI.color = worldSimulationTickSmokeRunner.LastSuccess ? Color.green : new Color(1f, 0.35f, 0.35f);
                    GUILayout.Label($"Simulation Smoke: {(worldSimulationTickSmokeRunner.LastSuccess ? "Passed" : "Not Passed")}");
                    GUI.color = simulationPrevious;
                    GUILayout.Label(worldSimulationTickSmokeRunner.LastSummary);
                }

                GUILayout.Label($"Snapshot Refresh: {worldSimulationRefreshSummary}");
                GUILayout.Label($"Tick Count: {(worldSimulationTickCount >= 0 ? worldSimulationTickCount.ToString() : "Unavailable")}");
                GUILayout.Label($"Last Tick UTC: {(string.IsNullOrEmpty(worldSimulationLastUtc) ? "Unavailable" : worldSimulationLastUtc)}");
                GUILayout.Label($"Last Area: {(string.IsNullOrEmpty(worldSimulationLastArea) ? "Unavailable" : worldSimulationLastArea)}");
                GUILayout.Label($"Last Event: {(string.IsNullOrEmpty(worldSimulationLastEvent) ? "Unavailable" : worldSimulationLastEvent)}");
                GUILayout.Label($"Last Session State: {(string.IsNullOrEmpty(worldSimulationLastSessionState) ? "Unavailable" : worldSimulationLastSessionState)}");
            }

            GUILayout.Space(8f);
            GUILayout.Label("Details");

            detailsScroll = GUILayout.BeginScrollView(detailsScroll, GUILayout.Height(140f));

            if (runner.LastWarnings.Count == 0 && runner.LastErrors.Count == 0)
            {
                GUILayout.Label("No warnings or errors.");
            }
            else
            {
                for (var i = 0; i < runner.LastWarnings.Count; i++)
                {
                    GUILayout.Label($"[Warning {i + 1}] {runner.LastWarnings[i]}");
                }

                for (var i = 0; i < runner.LastErrors.Count; i++)
                {
                    GUILayout.Label($"[Error {i + 1}] {runner.LastErrors[i]}");
                }
            }

            GUILayout.EndScrollView();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private static bool IsMoonSharpAvailable()
        {
            var scriptType = System.Type.GetType("MoonSharp.Interpreter.Script, MoonSharp.Interpreter");
            return scriptType != null;
        }

        private Color GetStatusColor()
        {
            if (!runner.HasRun)
            {
                return Color.gray;
            }

            if (runner.LastIsValid)
            {
                return Color.green;
            }

            if (runner.LastErrorCount == 0 && runner.LastWarningCount > 0)
            {
                return new Color(1f, 0.85f, 0.2f);
            }

            return new Color(1f, 0.35f, 0.35f);
        }

        private void DrawTrustLine(
            string label,
            bool hasRun,
            bool passed,
            bool skipped,
            string message)
        {
            var previous = GUI.color;
            GUI.color = GetTrustColor(hasRun, passed, skipped);

            var status = !hasRun
                ? "Not Run"
                : skipped
                    ? "Skipped"
                    : passed
                        ? "Passed"
                        : "Failed";

            GUILayout.Label($"{label}: {status}");
            GUI.color = previous;

            GUILayout.Label(message);
        }

        private Color GetTrustColor(bool hasRun, bool passed, bool skipped)
        {
            if (!hasRun)
            {
                return Color.gray;
            }

            if (skipped)
            {
                return new Color(1f, 0.85f, 0.2f);
            }

            return passed ? Color.green : new Color(1f, 0.35f, 0.35f);
        }

        private void DrawRoundTripLine(WorldPackageRunner runnerValue)
        {
            var previous = GUI.color;
            GUI.color = runnerValue.HasRoundTripRun
                ? (runnerValue.LastRoundTripSuccess ? Color.green : new Color(1f, 0.35f, 0.35f))
                : Color.gray;

            var status = !runnerValue.HasRoundTripRun
                ? "Not Run"
                : runnerValue.LastRoundTripSuccess
                    ? "Passed"
                    : "Failed";

            GUILayout.Label($"Round-Trip Smoke Test: {status}");
            GUI.color = previous;
            GUILayout.Label(runnerValue.LastRoundTripSummary);
        }

        private void RefreshWorldSimulationSnapshot()
        {
            worldSimulationTickCount = -1;
            worldSimulationLastUtc = string.Empty;
            worldSimulationLastArea = string.Empty;
            worldSimulationLastEvent = string.Empty;
            worldSimulationLastSessionState = string.Empty;

            var campaignDatabasePath = ResolveCampaignDatabasePath();
            if (string.IsNullOrEmpty(campaignDatabasePath))
            {
                worldSimulationRefreshSummary = "Failed: could not resolve campaign.db path.";
                return;
            }

            var persistence = new CampaignPersistenceService(campaignDatabasePath);

            if (!persistence.TryGetWorldState(WorldSimulationTickService.TickCountKey, out var tickText, out var error))
            {
                worldSimulationRefreshSummary = $"Failed: {error}";
                return;
            }

            if (!string.IsNullOrWhiteSpace(tickText) && int.TryParse(tickText, out var parsedTick) && parsedTick >= 0)
            {
                worldSimulationTickCount = parsedTick;
            }

            if (!persistence.TryGetWorldState(WorldSimulationTickService.LastTickUtcKey, out worldSimulationLastUtc, out error)
                || !persistence.TryGetWorldState(WorldSimulationTickService.LastAreaIdKey, out worldSimulationLastArea, out error)
                || !persistence.TryGetWorldState(WorldSimulationTickService.LastEventKey, out worldSimulationLastEvent, out error)
                || !persistence.TryGetWorldState(WorldSimulationTickService.LastSessionStateKey, out worldSimulationLastSessionState, out error))
            {
                worldSimulationRefreshSummary = $"Failed: {error}";
                return;
            }

            worldSimulationRefreshSummary = "Updated";
        }

        private string ResolveCampaignDatabasePath()
        {
            var worldProjectId = simulationWorldProjectId;
            if (bootstrap != null && !string.IsNullOrWhiteSpace(bootstrap.DefaultWorldProjectId))
            {
                worldProjectId = bootstrap.DefaultWorldProjectId;
            }

            if (string.IsNullOrWhiteSpace(worldProjectId))
            {
                return string.Empty;
            }

            if (!WorldProjectLoader.TryLoadFromStreamingAssets(worldProjectId, out var project, out _))
            {
                return string.Empty;
            }

            return Path.Combine(project.RootPath, project.Definition.CampaignDatabasePath);
        }
    }
}