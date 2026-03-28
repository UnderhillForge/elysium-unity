using System.Collections;
using System.IO;
using Elysium.Networking;
using Elysium.Packaging;
using Elysium.World;
using UnityEngine;

namespace Elysium.Boot
{
    /// Automatically restores and persists host campaign state around session lifecycle events.
    public sealed class HostCampaignPersistenceController : MonoBehaviour
    {
        [SerializeField] private ElysiumBootstrap bootstrap;
        [SerializeField] private ElysiumSessionManager sessionManager;
        [SerializeField] private bool autoLoadOnHostStart = true;
        [SerializeField] private bool autoSaveOnSessionChange = true;
        [SerializeField] private bool autoSaveOnCombatChange = true;
        [SerializeField] private bool autoSaveOnApplicationPause = true;
        [SerializeField] private bool autoSaveOnApplicationQuit = true;
        [SerializeField] private bool runWorldSimulationTickOnAutoSave = true;
        [SerializeField] private bool runWorldSimulationTickOnInterval = true;
        [SerializeField, Min(1f)] private float worldSimulationTickIntervalSeconds = 45f;
        [SerializeField, Min(0f)] private float worldSimulationTickIntervalJitterSeconds = 0f;
        [SerializeField, Min(1)] private int maxWorldSimulationTicksPerUpdate = 1;
        [SerializeField, Min(0f)] private float nonIntervalWorldSimulationMinSpacingSeconds = 0.25f;
        [SerializeField] private string encounterInstanceId = "active_host_encounter";
        [SerializeField] private string fallbackAreaId = "area_forest_edge_01";
        [SerializeField] private bool verboseLogging = true;

        private bool hasSubscribed;
        private bool isRestoring;
        private string cachedCampaignDatabasePath = string.Empty;
        private WorldSimulationTickService worldSimulationTickService;
        private float nextWorldSimulationTickAt = float.PositiveInfinity;
        private float lastWorldSimulationTickAt = float.NegativeInfinity;
        private int lastWorldSimulationTickFrame = -1;

        private void Reset()
        {
            bootstrap = GetComponent<ElysiumBootstrap>();
            sessionManager = GetComponent<ElysiumSessionManager>();
        }

        private IEnumerator Start()
        {
            if (sessionManager == null)
            {
                yield break;
            }

            while (!sessionManager.IsSpawned)
            {
                yield return null;
            }

            if (!sessionManager.IsServer)
            {
                yield break;
            }

            cachedCampaignDatabasePath = ResolveCampaignDatabasePath();
            EnsureWorldSimulationService();
            SubscribeIfNeeded();

            if (autoLoadOnHostStart)
            {
                TryRestoreOnStartup();
            }

            ScheduleNextWorldSimulationTick();
        }

        private void Update()
        {
            if (!runWorldSimulationTickOnInterval
                || isRestoring
                || sessionManager == null
                || !sessionManager.IsSpawned
                || !sessionManager.IsServer)
            {
                return;
            }

            var dueIntervalTicks = ConsumeDueIntervalTickBudget();
            if (dueIntervalTicks <= 0)
            {
                return;
            }

            for (var index = 0; index < dueIntervalTicks; index++)
            {
                TryAdvanceWorldSimulationTick("interval");
            }

            if (verboseLogging && IsStillBehindSimulationSchedule())
            {
                Debug.LogWarning(
                    $"[Elysium] World simulation interval budget capped at {Mathf.Max(1, maxWorldSimulationTicksPerUpdate)} ticks per frame.");
            }
        }

        private void OnDisable()
        {
            UnsubscribeIfNeeded();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && autoSaveOnApplicationPause)
            {
                TrySave("application-pause");
            }
        }

        private void OnApplicationQuit()
        {
            if (autoSaveOnApplicationQuit)
            {
                TrySave("application-quit");
            }
        }

        private void SubscribeIfNeeded()
        {
            if (hasSubscribed || sessionManager == null)
            {
                return;
            }

            sessionManager.SessionInfoUpdated += HandleSessionInfoUpdated;
            sessionManager.CombatSnapshotUpdated += HandleCombatSnapshotUpdated;
            hasSubscribed = true;
        }

        private void UnsubscribeIfNeeded()
        {
            if (!hasSubscribed || sessionManager == null)
            {
                return;
            }

            sessionManager.SessionInfoUpdated -= HandleSessionInfoUpdated;
            sessionManager.CombatSnapshotUpdated -= HandleCombatSnapshotUpdated;
            hasSubscribed = false;
        }

        private void HandleSessionInfoUpdated(SessionInfo sessionInfo)
        {
            if (autoSaveOnSessionChange)
            {
                TrySave("session-update");
            }
        }

        private void HandleCombatSnapshotUpdated(CombatNetworkSnapshot snapshot)
        {
            if (autoSaveOnCombatChange)
            {
                TrySave("combat-update");
            }
        }

        private void TryRestoreOnStartup()
        {
            if (string.IsNullOrEmpty(cachedCampaignDatabasePath) || sessionManager == null)
            {
                return;
            }

            isRestoring = true;
            var restored = sessionManager.TryLoadCampaignState(cachedCampaignDatabasePath, encounterInstanceId, out var error);
            isRestoring = false;

            if (restored)
            {
                if (verboseLogging)
                {
                    Debug.Log($"[Elysium] Restored host campaign state from '{cachedCampaignDatabasePath}'.");
                }
            }
            else if (verboseLogging && !string.IsNullOrEmpty(error) && !error.Contains("No persisted session snapshot found"))
            {
                Debug.LogWarning($"[Elysium] Host campaign restore skipped: {error}");
            }
        }

        private void TrySave(string reason)
        {
            if (isRestoring || sessionManager == null || !sessionManager.IsSpawned || !sessionManager.IsServer)
            {
                return;
            }

            if (string.IsNullOrEmpty(cachedCampaignDatabasePath))
            {
                cachedCampaignDatabasePath = ResolveCampaignDatabasePath();
                EnsureWorldSimulationService();
            }

            if (string.IsNullOrEmpty(cachedCampaignDatabasePath))
            {
                return;
            }

            TryAdvanceWorldSimulationTick(reason);

            if (!sessionManager.TrySaveCampaignState(cachedCampaignDatabasePath, encounterInstanceId, fallbackAreaId, out var error))
            {
                if (verboseLogging)
                {
                    Debug.LogWarning($"[Elysium] Auto-save failed ({reason}): {error}");
                }

                return;
            }

            if (verboseLogging)
            {
                Debug.Log($"[Elysium] Auto-saved campaign state ({reason}).");
            }
        }

        private string ResolveCampaignDatabasePath()
        {
            if (bootstrap == null)
            {
                return string.Empty;
            }

            if (WorldProjectLoader.TryLoadFromStreamingAssets(bootstrap.DefaultWorldProjectId, out var project, out var error))
            {
                return Path.Combine(project.RootPath, project.Definition.CampaignDatabasePath);
            }

            if (verboseLogging)
            {
                Debug.LogWarning($"[Elysium] Could not resolve campaign database path: {error}");
            }

                return string.Empty;
        }

        private void EnsureWorldSimulationService()
        {
            if ((!runWorldSimulationTickOnAutoSave && !runWorldSimulationTickOnInterval)
                || worldSimulationTickService != null
                || string.IsNullOrEmpty(cachedCampaignDatabasePath))
            {
                return;
            }

            worldSimulationTickService = new WorldSimulationTickService(cachedCampaignDatabasePath);
        }

        private void TryAdvanceWorldSimulationTick(string reason)
        {
            var shouldRun = reason == "interval" ? runWorldSimulationTickOnInterval : runWorldSimulationTickOnAutoSave;
            if (!shouldRun || sessionManager == null)
            {
                return;
            }

            if (!CanRunWorldSimulationTick(reason))
            {
                return;
            }

            EnsureWorldSimulationService();
            if (worldSimulationTickService == null)
            {
                return;
            }

            var activeAreaId = fallbackAreaId;
            var exploration = sessionManager.CurrentExplorationSnapshot;
            if (exploration != null && !string.IsNullOrWhiteSpace(exploration.AreaId))
            {
                activeAreaId = exploration.AreaId;
            }

            if (!worldSimulationTickService.TryAdvanceTick(sessionManager.Session.State, activeAreaId, out var tickSnapshot, out var error))
            {
                if (verboseLogging)
                {
                    Debug.LogWarning($"[Elysium] World simulation tick failed ({reason}): {error}");
                }

                return;
            }

            if (verboseLogging && tickSnapshot.Advanced)
            {
                Debug.Log(
                    $"[Elysium] World simulation tick #{tickSnapshot.TickCount} ({tickSnapshot.LastEvent}) in area '{tickSnapshot.LastAreaId}' ({reason}).");
            }

            if (tickSnapshot.Advanced)
            {
                RecordAdvancedWorldSimulationTick();
            }
        }

        private int ConsumeDueIntervalTickBudget()
        {
            if (float.IsPositiveInfinity(nextWorldSimulationTickAt))
            {
                ScheduleNextWorldSimulationTick();
            }

            var dueTicks = 0;
            var maxTicks = Mathf.Max(1, maxWorldSimulationTicksPerUpdate);
            var now = Time.unscaledTime;
            while (now >= nextWorldSimulationTickAt && dueTicks < maxTicks)
            {
                dueTicks++;
                ScheduleNextWorldSimulationTick(nextWorldSimulationTickAt);
            }

            return dueTicks;
        }

        private bool IsStillBehindSimulationSchedule()
        {
            return !float.IsPositiveInfinity(nextWorldSimulationTickAt) && Time.unscaledTime >= nextWorldSimulationTickAt;
        }

        private bool CanRunWorldSimulationTick(string reason)
        {
            if (Time.frameCount == lastWorldSimulationTickFrame)
            {
                return false;
            }

            if (!string.Equals(reason, "interval")
                && Time.unscaledTime - lastWorldSimulationTickAt < Mathf.Max(0f, nonIntervalWorldSimulationMinSpacingSeconds))
            {
                return false;
            }

            return true;
        }

        private void RecordAdvancedWorldSimulationTick()
        {
            lastWorldSimulationTickAt = Time.unscaledTime;
            lastWorldSimulationTickFrame = Time.frameCount;
        }

        private void ScheduleNextWorldSimulationTick()
        {
            ScheduleNextWorldSimulationTick(Time.unscaledTime);
        }

        private void ScheduleNextWorldSimulationTick(float startTime)
        {
            var interval = Mathf.Max(1f, worldSimulationTickIntervalSeconds);
            nextWorldSimulationTickAt = startTime + interval + SampleIntervalJitter();
        }

        private float SampleIntervalJitter()
        {
            var jitter = Mathf.Max(0f, worldSimulationTickIntervalJitterSeconds);
            if (jitter <= 0f)
            {
                return 0f;
            }

            return Random.Range(0f, jitter);
        }
    }
}