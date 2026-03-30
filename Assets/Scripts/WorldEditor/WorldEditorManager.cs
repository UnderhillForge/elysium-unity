using System;
using System.Collections.Generic;
using System.IO;
using Elysium.Boot;
using Elysium.Networking;
using Elysium.Packaging;
using Elysium.World;
using Elysium.World.Lua;
using Unity.Netcode;
using UnityEngine;

namespace Elysium.WorldEditor
{
    /// Runtime GM world editor manager for terrain sculpt/paint/water/place operations.
    /// Authority lives on the server. Clients can request edits through ServerRpcs.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class WorldEditorManager : NetworkBehaviour
    {
        private const ulong HostClientId = 0;
        private const string LastMutationKey = "world.editor.lastMutation";

        public static WorldEditorManager Instance { get; private set; }

        [Header("Editor Context")]
        [SerializeField] private ElysiumBootstrap bootstrap;
        [SerializeField] private ElysiumSessionManager sessionManager;
        [SerializeField] private string fallbackWorldProjectFolder = "starter_forest_edge";
        [SerializeField] private string fallbackAreaId = "area_forest_edge_01";

        [Header("Authoring Targets")]
        // Terrain material guidance (low-poly cel look):
        // - Use a toon Shader Graph terrain material with stepped diffuse bands.
        // - Keep smoothness near 0 and metallic near 0 for flat stylized response.
        // - Pair with outline post-process or geometry outlines on placeables.
        [SerializeField] private Terrain targetTerrain;
        [SerializeField] private Transform placeablesRoot;
        [SerializeField] private List<PlaceableDefinition> placeableCatalog = new List<PlaceableDefinition>();
        [SerializeField] private GameObject defaultWaterPrefab;

        [Header("God Mode")]
        [SerializeField] private KeyCode toggleGodModeKey = KeyCode.F1;
        [SerializeField] private bool godModeEnabled;
        [SerializeField] private WorldEditorTool activeTool = WorldEditorTool.Sculpt;
        [SerializeField] private SculptOperation activeSculptOperation = SculptOperation.Raise;

        [Header("Brush Defaults")]
        [SerializeField, Min(0.5f)] private float sculptRadiusMeters = 4f;
        [SerializeField, Range(0.01f, 2f)] private float sculptStrength = 0.3f;
        [SerializeField, Min(0.01f)] private float sculptTickIntervalSeconds = 0.05f;
        [SerializeField, Min(0.5f)] private float paintRadiusMeters = 5f;
        [SerializeField, Range(0f, 1f)] private float paintOpacity = 0.35f;
        [SerializeField] private int paintLayerIndex;

        [Header("Undo")]
        [SerializeField, Range(5, 10)] private int maxUndoSteps = 8;
        [SerializeField] private KeyCode undoKey = KeyCode.Z;

        [Header("AnyRPG Prefab Integration")]
        [SerializeField] private bool allowResourcesPrefabLookup = true;
        [SerializeField] private List<string> resourcesPrefabRoots = new List<string>
        {
            "AnyRPG",
            "RPGPP_LT",
            "Polytope Studio",
        };

        [Header("Persistence")]
        [SerializeField] private bool autoLoadOnServerSpawn = true;
        [SerializeField] private bool autoSaveAfterMutation = true;
        [SerializeField, Min(0.2f)] private float saveDebounceSeconds = 0.75f;

        private readonly Dictionary<string, GameObject> placeableById = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<ulong, string> placedObjectIdByNetworkObject = new Dictionary<ulong, string>();
        private readonly List<NetworkObject> spawnedPlaceables = new List<NetworkObject>();

        private WorldMutationService worldMutationService;
        private WorldEditorPersistenceAdapter persistenceAdapter;
        private float nextAllowedPersistAt;
        private float sculptAccumulatedDelta;
        private float lastSculptTickAt;
        private bool pendingSave;
        private bool suppressDirtyTracking;
        private bool useInjectedServices;

        private readonly List<WorldEditorAreaSnapshot> undoStack = new List<WorldEditorAreaSnapshot>();

        public bool GodModeEnabled => godModeEnabled;
        public WorldEditorTool ActiveTool => activeTool;
        public SculptOperation ActiveSculptOperation => activeSculptOperation;
        public float SculptRadiusMeters => sculptRadiusMeters;
        public float SculptStrength => sculptStrength;
        public float PaintRadiusMeters => paintRadiusMeters;
        public float PaintOpacity => paintOpacity;
        public int PaintLayerIndex => paintLayerIndex;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<ElysiumBootstrap>();
            }

            if (sessionManager == null)
            {
                sessionManager = FindAnyObjectByType<ElysiumSessionManager>();
            }

            if (targetTerrain == null)
            {
                targetTerrain = Terrain.activeTerrain;
            }

            RebuildPlaceableCatalog();
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (targetTerrain == null)
            {
                targetTerrain = Terrain.activeTerrain;
            }

            RebuildPlaceableCatalog();

            if (IsServer)
            {
                if (TryInitializePersistence(out var initError))
                {
                    if (autoLoadOnServerSpawn)
                    {
                        TryLoadWorldState(out var loadError);
                        if (!string.IsNullOrEmpty(loadError))
                        {
                            Debug.LogWarning($"[WorldEditor] Initial load skipped: {loadError}");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[WorldEditor] Persistence unavailable: {initError}");
                }
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleGodModeKey) && CanUseLocalGodModeInput())
            {
                godModeEnabled = !godModeEnabled;
                Debug.Log($"[WorldEditor] God Mode {(godModeEnabled ? "enabled" : "disabled")}");
            }

            if (!godModeEnabled || !CanUseLocalGodModeInput())
            {
                return;
            }

            if (Input.GetKeyDown(undoKey)
                && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || IsServer))
            {
                RequestUndo();
            }

            // Sculpt updates are throttled to reduce expensive terrain patches every frame.
            if (activeTool == WorldEditorTool.Sculpt && Input.GetMouseButton(0) && TryRaycastTerrain(out var worldPoint))
            {
                sculptAccumulatedDelta += Time.deltaTime;
                if (Time.unscaledTime - lastSculptTickAt < Mathf.Max(0.01f, sculptTickIntervalSeconds))
                {
                    return;
                }

                var operation = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                    ? SculptOperation.Lower
                    : activeSculptOperation;

                var request = new TerrainBrushRequest
                {
                    WorldCenter = worldPoint,
                    RadiusMeters = sculptRadiusMeters,
                    Strength = sculptStrength,
                    DeltaTime = Mathf.Clamp(sculptAccumulatedDelta, 0.005f, 0.2f),
                    Operation = operation,
                };

                RequestSculpt(request);
                sculptAccumulatedDelta = 0f;
                lastSculptTickAt = Time.unscaledTime;
            }
            else if (!Input.GetMouseButton(0))
            {
                sculptAccumulatedDelta = 0f;
            }

            if (pendingSave && autoSaveAfterMutation && Time.unscaledTime >= nextAllowedPersistAt)
            {
                TrySaveWorldState(out _);
            }
        }

        public void SetTool(WorldEditorTool tool)
        {
            activeTool = tool;
        }

        public void SetSculptOperation(SculptOperation operation)
        {
            activeSculptOperation = operation;
        }

        public void SetSculptRadius(float radius)
        {
            sculptRadiusMeters = Mathf.Max(0.5f, radius);
        }

        public void SetSculptStrength(float strength)
        {
            sculptStrength = Mathf.Clamp(strength, 0.01f, 2f);
        }

        public void SetPaintLayer(int layerIndex)
        {
            paintLayerIndex = Mathf.Max(0, layerIndex);
        }

        public void SetPaintOpacity(float opacity)
        {
            paintOpacity = Mathf.Clamp01(opacity);
        }

        public bool RequestSculpt(in TerrainBrushRequest request)
        {
            if (IsServer)
            {
                return TryApplySculptServer(request, out _);
            }

            SubmitSculptServerRpc(request.WorldCenter, request.RadiusMeters, request.Strength, request.DeltaTime, (int)request.Operation);
            return true;
        }

        public bool RequestPaint(in TexturePaintRequest request)
        {
            if (IsServer)
            {
                return TryApplyPaintServer(request, out _);
            }

            SubmitPaintServerRpc(request.WorldCenter, request.RadiusMeters, request.LayerIndex, request.Opacity);
            return true;
        }

        public bool RequestActivePaintAt(Vector3 worldCenter)
        {
            var request = new TexturePaintRequest
            {
                WorldCenter = worldCenter,
                RadiusMeters = paintRadiusMeters,
                LayerIndex = paintLayerIndex,
                Opacity = paintOpacity,
            };

            return RequestPaint(request);
        }

        public bool RequestPlace(in PlaceableSpawnRequest request)
        {
            if (IsServer)
            {
                return TrySpawnPlaceableServer(request, out _);
            }

            SubmitPlaceableSpawnServerRpc(
                request.PlaceableId,
                request.Position,
                request.Rotation,
                request.Scale);
            return true;
        }

        public bool RequestWater(in WaterPlacementRequest request)
        {
            if (IsServer)
            {
                return TryApplyWaterServer(request, out _);
            }

            SubmitWaterPlacementServerRpc(
                request.WorldCenter,
                request.RadiusMeters,
                request.WaterSurfaceY,
                request.CarveDepth);
            return true;
        }

        public bool RequestUndo()
        {
            if (IsServer)
            {
                return TryUndoServer(out _);
            }

            SubmitUndoServerRpc();
            return true;
        }

        public bool TryGetBrushWorldPoint(out Vector3 worldPoint)
        {
            return TryRaycastTerrain(out worldPoint);
        }

        /// Allows boot code to inject existing world mutation/persistence services.
        public void InjectPersistenceServices(WorldMutationService mutationService, WorldEditorPersistenceAdapter adapter = null)
        {
            worldMutationService = mutationService;
            persistenceAdapter = adapter ?? (mutationService == null ? null : new WorldEditorPersistenceAdapter(mutationService));
            useInjectedServices = worldMutationService != null && persistenceAdapter != null;
        }

        public bool TrySaveWorldState(out string error)
        {
            if (!IsServer)
            {
                error = "Only server can save world editor state.";
                return false;
            }

            if (!TryEnsurePersistenceReady(out error))
            {
                return false;
            }

            var snapshots = BuildPlacedObjectSnapshots();
            if (!persistenceAdapter.TrySave(ResolveAuthorityPlayerId(), ResolveAreaId(), targetTerrain, snapshots, out error))
            {
                return false;
            }

            pendingSave = false;
            return worldMutationService.TryWriteState(
                ResolveAuthorityPlayerId(),
                LastMutationKey,
                DateTime.UtcNow.ToString("O"),
                new LuaSandboxPolicy { AllowWorldRead = true, AllowWorldWrite = true },
                out error);
        }

        public bool TryLoadWorldState(out string error)
        {
            if (!IsServer)
            {
                error = "Only server can load world editor state.";
                return false;
            }

            if (!TryEnsurePersistenceReady(out error))
            {
                return false;
            }

            if (!persistenceAdapter.TryLoad(ResolveAuthorityPlayerId(), ResolveAreaId(), targetTerrain, out var snapshot, out error))
            {
                return false;
            }

            RehydratePlaceables(snapshot.placedObjects);
            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitSculptServerRpc(
            Vector3 worldCenter,
            float radiusMeters,
            float strength,
            float deltaTime,
            int sculptOperation,
            ServerRpcParams rpcParams = default)
        {
            if (!TryAuthorizeGmClient(rpcParams.Receive.SenderClientId, out _))
            {
                return;
            }

            var request = new TerrainBrushRequest
            {
                WorldCenter = worldCenter,
                RadiusMeters = radiusMeters,
                Strength = strength,
                DeltaTime = deltaTime,
                Operation = (SculptOperation)Mathf.Clamp(sculptOperation, 0, 3),
            };

            TryApplySculptServer(request, out _);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitPaintServerRpc(
            Vector3 worldCenter,
            float radiusMeters,
            int layerIndex,
            float opacity,
            ServerRpcParams rpcParams = default)
        {
            if (!TryAuthorizeGmClient(rpcParams.Receive.SenderClientId, out _))
            {
                return;
            }

            var request = new TexturePaintRequest
            {
                WorldCenter = worldCenter,
                RadiusMeters = radiusMeters,
                LayerIndex = layerIndex,
                Opacity = opacity,
            };

            TryApplyPaintServer(request, out _);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitPlaceableSpawnServerRpc(
            string placeableId,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            ServerRpcParams rpcParams = default)
        {
            if (!TryAuthorizeGmClient(rpcParams.Receive.SenderClientId, out _))
            {
                return;
            }

            var request = new PlaceableSpawnRequest
            {
                PlaceableId = placeableId,
                Position = position,
                Rotation = rotation,
                Scale = scale,
            };

            TrySpawnPlaceableServer(request, out _);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitWaterPlacementServerRpc(
            Vector3 worldCenter,
            float radiusMeters,
            float waterSurfaceY,
            float carveDepth,
            ServerRpcParams rpcParams = default)
        {
            if (!TryAuthorizeGmClient(rpcParams.Receive.SenderClientId, out _))
            {
                return;
            }

            var request = new WaterPlacementRequest
            {
                WorldCenter = worldCenter,
                RadiusMeters = radiusMeters,
                WaterSurfaceY = waterSurfaceY,
                CarveDepth = carveDepth,
            };

            TryApplyWaterServer(request, out _);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitUndoServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!TryAuthorizeGmClient(rpcParams.Receive.SenderClientId, out _))
            {
                return;
            }

            TryUndoServer(out _);
        }

        [ClientRpc]
        private void ApplyHeightPatchClientRpc(int rectX, int rectY, int rectW, int rectH, string compressedHeightsBase64)
        {
            if (IsServer || targetTerrain == null || targetTerrain.terrainData == null)
            {
                return;
            }

            var heights = DecodeHeightsPatch(rectW, rectH, compressedHeightsBase64);
            targetTerrain.terrainData.SetHeightsDelayLOD(rectX, rectY, heights);
            targetTerrain.terrainData.SyncHeightmap();
            targetTerrain.Flush();
        }

        [ClientRpc]
        private void ApplyAlphaPatchClientRpc(int rectX, int rectY, int rectW, int rectH, int layers, string compressedAlphaBase64)
        {
            if (IsServer || targetTerrain == null || targetTerrain.terrainData == null)
            {
                return;
            }

            var maps = DecodeAlphaPatch(rectW, rectH, layers, compressedAlphaBase64);
            targetTerrain.terrainData.SetAlphamaps(rectX, rectY, maps);
        }

        private bool TryApplySculptServer(in TerrainBrushRequest request, out string error)
        {
            error = string.Empty;
            if (!TryEnsureServerEditReady(out error))
            {
                return false;
            }

            CaptureUndoSnapshot();

            if (!WorldEditorTerrainBrushes.TryApplySculpt(targetTerrain, request, out var changedRect))
            {
                error = "Sculpt request did not intersect terrain.";
                return false;
            }

            targetTerrain.terrainData.SyncHeightmap();
            targetTerrain.Flush();
            var patch = targetTerrain.terrainData.GetHeights(changedRect.x, changedRect.y, changedRect.width, changedRect.height);
            ApplyHeightPatchClientRpc(changedRect.x, changedRect.y, changedRect.width, changedRect.height, EncodeHeightsPatch(patch));
            MarkMutationDirty();
            return true;
        }

        private bool TryApplyPaintServer(in TexturePaintRequest request, out string error)
        {
            error = string.Empty;
            if (!TryEnsureServerEditReady(out error))
            {
                return false;
            }

            CaptureUndoSnapshot();

            if (!WorldEditorTerrainBrushes.TryApplyTexturePaint(targetTerrain, request, out var changedRect))
            {
                error = "Paint request did not intersect terrain or layer is invalid.";
                return false;
            }

            var patch = targetTerrain.terrainData.GetAlphamaps(changedRect.x, changedRect.y, changedRect.width, changedRect.height);
            ApplyAlphaPatchClientRpc(
                changedRect.x,
                changedRect.y,
                changedRect.width,
                changedRect.height,
                targetTerrain.terrainData.alphamapLayers,
                EncodeAlphaPatch(patch));

            MarkMutationDirty();
            return true;
        }

        private bool TryApplyWaterServer(in WaterPlacementRequest request, out string error)
        {
            error = string.Empty;
            if (!TryEnsureServerEditReady(out error))
            {
                return false;
            }

            var flattenRequest = new TerrainBrushRequest
            {
                WorldCenter = new Vector3(request.WorldCenter.x, request.WaterSurfaceY - request.CarveDepth, request.WorldCenter.z),
                RadiusMeters = request.RadiusMeters,
                Strength = 0.85f,
                DeltaTime = 1f,
                Operation = SculptOperation.Flatten,
            };

            if (!TryApplySculptServer(flattenRequest, out error))
            {
                return false;
            }

            if (defaultWaterPrefab != null)
            {
                var spawnRequest = new PlaceableSpawnRequest
                {
                    PlaceableId = ResolvePlaceableIdForPrefab(defaultWaterPrefab),
                    Position = new Vector3(request.WorldCenter.x, request.WaterSurfaceY, request.WorldCenter.z),
                    Rotation = Quaternion.identity,
                    Scale = Vector3.one * Mathf.Max(1f, request.RadiusMeters * 0.2f),
                };

                TrySpawnPlaceableServer(spawnRequest, out _);
            }

            return true;
        }

        private bool TrySpawnPlaceableServer(in PlaceableSpawnRequest request, out string error)
        {
            error = string.Empty;
            if (!TryEnsureServerEditReady(out error))
            {
                return false;
            }

            if (!TryResolvePlaceablePrefab(request.PlaceableId, out var prefab) || prefab == null)
            {
                error = $"Unknown placeable id '{request.PlaceableId}'.";
                return false;
            }

            if (!suppressDirtyTracking)
            {
                CaptureUndoSnapshot();
            }

            var instance = Instantiate(prefab, request.Position, request.Rotation, placeablesRoot);
            instance.transform.localScale = request.Scale == Vector3.zero ? Vector3.one : request.Scale;

            var networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                error = $"Prefab '{prefab.name}' is missing NetworkObject. Add it and register in DefaultNetworkPrefabs.";
                Destroy(instance);
                return false;
            }

            networkObject.Spawn(destroyWithScene: true);
            spawnedPlaceables.Add(networkObject);
            placedObjectIdByNetworkObject[networkObject.NetworkObjectId] = request.PlaceableId;

            MarkMutationDirty();
            return true;
        }

        private bool TryAuthorizeGmClient(ulong senderClientId, out string gmPlayerId)
        {
            gmPlayerId = string.Empty;
            if (!IsServer)
            {
                return false;
            }

            if (senderClientId == HostClientId)
            {
                gmPlayerId = ResolveAuthorityPlayerId();
                return true;
            }

            if (sessionManager?.Session == null)
            {
                return false;
            }

            var player = sessionManager.Session.GetPlayerByClientId(senderClientId);
            if (player == null || !player.IsGM)
            {
                return false;
            }

            gmPlayerId = player.PlayerId;
            return true;
        }

        private bool CanUseLocalGodModeInput()
        {
            if (!IsSpawned)
            {
                return false;
            }

            if (IsServer)
            {
                return true;
            }

            if (sessionManager?.Session == null)
            {
                return false;
            }

            var localClientId = NetworkManager.Singleton == null ? ulong.MaxValue : NetworkManager.Singleton.LocalClientId;
            var player = sessionManager.Session.GetPlayerByClientId(localClientId);
            return player != null && player.IsGM;
        }

        private bool TryInitializePersistence(out string error)
        {
            if (useInjectedServices && worldMutationService != null && persistenceAdapter != null)
            {
                error = string.Empty;
                return true;
            }

            error = string.Empty;
            var projectFolder = ResolveWorldProjectFolder();
            if (!WorldProjectLoader.TryLoadFromStreamingAssets(projectFolder, out var project, out error))
            {
                return false;
            }

            var campaignDatabasePath = Path.Combine(project.RootPath, project.Definition.CampaignDatabasePath);
            worldMutationService = new WorldMutationService(projectFolder, campaignDatabasePath);
            persistenceAdapter = new WorldEditorPersistenceAdapter(worldMutationService);
            return true;
        }

        private bool TryEnsurePersistenceReady(out string error)
        {
            if (worldMutationService != null && persistenceAdapter != null)
            {
                error = string.Empty;
                return true;
            }

            return TryInitializePersistence(out error);
        }

        private bool TryEnsureServerEditReady(out string error)
        {
            if (!IsServer)
            {
                error = "Only server can apply world edits.";
                return false;
            }

            if (targetTerrain == null || targetTerrain.terrainData == null)
            {
                error = "Target terrain is not configured.";
                return false;
            }

            return TryEnsurePersistenceReady(out error);
        }

        private string ResolveWorldProjectFolder()
        {
            if (bootstrap != null && !string.IsNullOrWhiteSpace(bootstrap.DefaultWorldProjectId))
            {
                return bootstrap.DefaultWorldProjectId;
            }

            if (sessionManager?.CurrentSessionInfo != null && !string.IsNullOrWhiteSpace(sessionManager.CurrentSessionInfo.WorldProjectId))
            {
                return sessionManager.CurrentSessionInfo.WorldProjectId;
            }

            return fallbackWorldProjectFolder;
        }

        private string ResolveAreaId()
        {
            var exploration = sessionManager?.CurrentExplorationSnapshot;
            if (exploration != null && !string.IsNullOrWhiteSpace(exploration.AreaId))
            {
                return exploration.AreaId;
            }

            return fallbackAreaId;
        }

        private string ResolveAuthorityPlayerId()
        {
            if (sessionManager?.Session != null && !string.IsNullOrWhiteSpace(sessionManager.Session.GMPlayerId))
            {
                return sessionManager.Session.GMPlayerId;
            }

            return "gm_001";
        }

        private void MarkMutationDirty()
        {
            if (suppressDirtyTracking)
            {
                return;
            }

            if (!autoSaveAfterMutation)
            {
                return;
            }

            pendingSave = true;
            if (Time.unscaledTime < nextAllowedPersistAt)
            {
                return;
            }

            nextAllowedPersistAt = Time.unscaledTime + saveDebounceSeconds;
        }

        private List<PlacedObjectSnapshot> BuildPlacedObjectSnapshots()
        {
            var snapshots = new List<PlacedObjectSnapshot>(spawnedPlaceables.Count);
            for (var index = 0; index < spawnedPlaceables.Count; index++)
            {
                var networkObject = spawnedPlaceables[index];
                if (networkObject == null)
                {
                    continue;
                }

                if (!placedObjectIdByNetworkObject.TryGetValue(networkObject.NetworkObjectId, out var placeableId))
                {
                    continue;
                }

                var transformRef = networkObject.transform;
                snapshots.Add(new PlacedObjectSnapshot
                {
                    placeableId = placeableId,
                    position = SerializableVector3.FromVector3(transformRef.position),
                    rotation = SerializableQuaternion.FromQuaternion(transformRef.rotation),
                    scale = SerializableVector3.FromVector3(transformRef.localScale),
                });
            }

            return snapshots;
        }

        private void RehydratePlaceables(IReadOnlyCollection<PlacedObjectSnapshot> placedObjects)
        {
            suppressDirtyTracking = true;
            try
            {
                for (var i = spawnedPlaceables.Count - 1; i >= 0; i--)
                {
                    var existing = spawnedPlaceables[i];
                    if (existing == null)
                    {
                        continue;
                    }

                    if (existing.IsSpawned)
                    {
                        existing.Despawn(destroy: true);
                    }
                    else
                    {
                        Destroy(existing.gameObject);
                    }
                }

                spawnedPlaceables.Clear();
                placedObjectIdByNetworkObject.Clear();

                if (placedObjects == null)
                {
                    return;
                }

                foreach (var snapshot in placedObjects)
                {
                    if (snapshot == null)
                    {
                        continue;
                    }

                    var request = new PlaceableSpawnRequest
                    {
                        PlaceableId = snapshot.placeableId,
                        Position = snapshot.position.ToVector3(),
                        Rotation = snapshot.rotation.ToQuaternion(),
                        Scale = snapshot.scale.ToVector3(),
                    };

                    TrySpawnPlaceableServer(request, out _);
                }
            }
            finally
            {
                suppressDirtyTracking = false;
            }
        }

        private void RebuildPlaceableCatalog()
        {
            placeableById.Clear();
            for (var index = 0; index < placeableCatalog.Count; index++)
            {
                var entry = placeableCatalog[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || entry.Prefab == null)
                {
                    continue;
                }

                placeableById[entry.Id] = entry.Prefab;
            }

            if (defaultWaterPrefab != null)
            {
                var waterId = ResolvePlaceableIdForPrefab(defaultWaterPrefab);
                if (!string.IsNullOrWhiteSpace(waterId))
                {
                    placeableById[waterId] = defaultWaterPrefab;
                }
            }
        }

        private string ResolvePlaceableIdForPrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                return string.Empty;
            }

            for (var index = 0; index < placeableCatalog.Count; index++)
            {
                var entry = placeableCatalog[index];
                if (entry != null && entry.Prefab == prefab && !string.IsNullOrWhiteSpace(entry.Id))
                {
                    return entry.Id;
                }
            }

            return $"auto.{prefab.name}";
        }

        private bool TryResolvePlaceablePrefab(string placeableId, out GameObject prefab)
        {
            prefab = null;
            if (!string.IsNullOrWhiteSpace(placeableId)
                && placeableById.TryGetValue(placeableId, out prefab)
                && prefab != null)
            {
                return true;
            }

            if (!allowResourcesPrefabLookup || string.IsNullOrWhiteSpace(placeableId))
            {
                return false;
            }

            var effectiveId = placeableId;
            const string anyRpgPrefix = "anyrpg:";
            if (effectiveId.StartsWith(anyRpgPrefix, StringComparison.OrdinalIgnoreCase))
            {
                effectiveId = effectiveId.Substring(anyRpgPrefix.Length);
            }

            prefab = Resources.Load<GameObject>(effectiveId);
            if (prefab != null)
            {
                placeableById[placeableId] = prefab;
                return true;
            }

            for (var i = 0; i < resourcesPrefabRoots.Count; i++)
            {
                var root = resourcesPrefabRoots[i];
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                var path = string.Concat(root.TrimEnd('/'), "/", effectiveId);
                prefab = Resources.Load<GameObject>(path);
                if (prefab != null)
                {
                    placeableById[placeableId] = prefab;
                    return true;
                }
            }

            return false;
        }

        private void CaptureUndoSnapshot()
        {
            if (!IsServer || persistenceAdapter == null || targetTerrain == null)
            {
                return;
            }

            if (!persistenceAdapter.TryCaptureSnapshot(ResolveAreaId(), targetTerrain, BuildPlacedObjectSnapshots(), out var snapshot, out _))
            {
                return;
            }

            if (undoStack.Count >= Mathf.Max(5, maxUndoSteps))
            {
                undoStack.RemoveAt(0);
            }

            undoStack.Add(snapshot);
        }

        private bool TryUndoServer(out string error)
        {
            error = string.Empty;
            if (!TryEnsureServerEditReady(out error))
            {
                return false;
            }

            if (undoStack.Count == 0)
            {
                error = "Undo stack is empty.";
                return false;
            }

            var index = undoStack.Count - 1;
            var snapshot = undoStack[index];
            undoStack.RemoveAt(index);

            if (!persistenceAdapter.TryApplySnapshot(targetTerrain, snapshot, out error))
            {
                return false;
            }

            targetTerrain.terrainData.SyncHeightmap();
            targetTerrain.Flush();

            suppressDirtyTracking = true;
            RehydratePlaceables(snapshot.placedObjects);
            suppressDirtyTracking = false;

            var allHeights = targetTerrain.terrainData.GetHeights(
                0,
                0,
                targetTerrain.terrainData.heightmapResolution,
                targetTerrain.terrainData.heightmapResolution);
            ApplyHeightPatchClientRpc(
                0,
                0,
                targetTerrain.terrainData.heightmapResolution,
                targetTerrain.terrainData.heightmapResolution,
                EncodeHeightsPatch(allHeights));

            var allAlphas = targetTerrain.terrainData.GetAlphamaps(
                0,
                0,
                targetTerrain.terrainData.alphamapWidth,
                targetTerrain.terrainData.alphamapHeight);
            ApplyAlphaPatchClientRpc(
                0,
                0,
                targetTerrain.terrainData.alphamapWidth,
                targetTerrain.terrainData.alphamapHeight,
                targetTerrain.terrainData.alphamapLayers,
                EncodeAlphaPatch(allAlphas));

            MarkMutationDirty();
            return true;
        }

        private static string EncodeHeightsPatch(float[,] heights)
        {
            var width = heights.GetLength(1);
            var height = heights.GetLength(0);
            using var raw = new MemoryStream();
            using (var writer = new BinaryWriter(raw))
            {
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        writer.Write(heights[y, x]);
                    }
                }
            }

            return Convert.ToBase64String(raw.ToArray());
        }

        private static float[,] DecodeHeightsPatch(int width, int height, string base64)
        {
            var heights = new float[height, width];
            var bytes = Convert.FromBase64String(base64);
            using var raw = new MemoryStream(bytes);
            using var reader = new BinaryReader(raw);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    heights[y, x] = reader.ReadSingle();
                }
            }

            return heights;
        }

        private static string EncodeAlphaPatch(float[,,] maps)
        {
            var height = maps.GetLength(0);
            var width = maps.GetLength(1);
            var layers = maps.GetLength(2);
            using var raw = new MemoryStream();
            using (var writer = new BinaryWriter(raw))
            {
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        for (var layer = 0; layer < layers; layer++)
                        {
                            writer.Write(maps[y, x, layer]);
                        }
                    }
                }
            }

            return Convert.ToBase64String(raw.ToArray());
        }

        private static float[,,] DecodeAlphaPatch(int width, int height, int layers, string base64)
        {
            var maps = new float[height, width, layers];
            var bytes = Convert.FromBase64String(base64);
            using var raw = new MemoryStream(bytes);
            using var reader = new BinaryReader(raw);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    for (var layer = 0; layer < layers; layer++)
                    {
                        maps[y, x, layer] = reader.ReadSingle();
                    }
                }
            }

            return maps;
        }

        private bool TryRaycastTerrain(out Vector3 worldPoint)
        {
            worldPoint = Vector3.zero;
            var cam = Camera.main;
            if (cam == null)
            {
                return false;
            }

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 5000f))
            {
                return false;
            }

            worldPoint = hit.point;
            return true;
        }
    }
}
