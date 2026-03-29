using System;
using System.Collections.Generic;
using System.IO;
using Elysium.Characters;
using Elysium.Networking;
using Elysium.Packaging;
using Elysium.Prototype.Networking;
using Elysium.World;
using UnityEngine;

namespace Elysium.Boot
{
    /// Thin playable bootstrap for the sandbox prototype lane.
    ///
    /// Responsibilities:
    /// - Load the proto world project and activate its entry area.
    /// - Open a host-local Elysium session through the protocol adapter.
    /// - Register a local controlling player and assign a starter character.
    /// - Spawn a donor character prefab at the authored entry spawn.
    /// - Route local movement input through Elysium exploration sync before moving.
    [DisallowMultipleComponent]
    public sealed class ProtoPlayableBootstrap : MonoBehaviour
    {
        private const ulong HostClientId = 0;

        [Header("Prototype World")]
        [SerializeField] private string worldProjectFolder = "proto_village_square";
        [SerializeField] private string sessionId = "proto_playable_001";

        [Header("Local Host Player")]
        [SerializeField] private string localPlayerId = "gm_001";
        [SerializeField] private string localDisplayName = "Local Prototype Host";
        [SerializeField] private bool localPlayerActsAsGm = true;

        [Header("Donor Content")]
        [SerializeField] private GameObject donorCharacterPrefab;
        [SerializeField] private string donorPrefabReferencePath = string.Empty;

        [Header("Runtime Controls")]
        [SerializeField] private float moveSpeed = 4.5f;
        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 3.25f, -5.5f);
        [SerializeField] private bool bootOnStart = true;

        private WorldProject activeProject;
        private AreaLifecycleService areaLifecycle;
        private CharacterGalleryService galleryService;
        private ProtoNetworkProtocolAdapter protocolAdapter;
        private GameObject activeCharacterInstance;
        private Camera sceneCamera;
        private string assignedCharacterId = string.Empty;

        public SessionService Session => protocolAdapter?.Session;
        public ExplorationSyncService Exploration => protocolAdapter?.Exploration;
        public WorldProject ActiveProject => activeProject;
        public AreaLifecycleService AreaLifecycle => areaLifecycle;
        public GameObject ActiveCharacterInstance => activeCharacterInstance;
        public string AssignedCharacterId => assignedCharacterId;
        public string LastMovementStatus { get; private set; } = "No movement yet.";
        public string DonorPrefabName => donorCharacterPrefab != null ? donorCharacterPrefab.name : "(none)";
        public string DonorPrefabAssetPath => string.IsNullOrWhiteSpace(donorPrefabReferencePath) ? "(unknown)" : donorPrefabReferencePath;
        public string SpawnStatus { get; private set; } = "Not spawned.";

        private void Start()
        {
            if (bootOnStart)
            {
                Boot();
            }
        }

        private void Update()
        {
            if (protocolAdapter == null || activeCharacterInstance == null)
            {
                return;
            }

            var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var direction = new Vector3(input.x, 0f, input.y);
            if (direction.sqrMagnitude > 1f)
            {
                direction.Normalize();
            }

            var nextPosition = activeCharacterInstance.transform.position + direction * (moveSpeed * Time.deltaTime);
            var facingYaw = Quaternion.LookRotation(direction, Vector3.up).eulerAngles.y;
            if (protocolAdapter.TrySubmitMovement(new ProtoMovementRequest
                {
                    SenderClientId = HostClientId,
                    RequesterPlayerId = localPlayerId,
                    AreaId = areaLifecycle.ActiveAreaId,
                    Position = nextPosition,
                    FacingYaw = facingYaw,
                }, out var error))
            {
                activeCharacterInstance.transform.position = nextPosition;
                activeCharacterInstance.transform.rotation = Quaternion.Euler(0f, facingYaw, 0f);
                LastMovementStatus = $"Accepted: {nextPosition.x:F1}, {nextPosition.z:F1}";
            }
            else
            {
                Debug.LogWarning($"[ProtoPlayableBootstrap] Movement rejected: {error}");
                LastMovementStatus = $"Rejected: {error}";
            }
        }

        private void LateUpdate()
        {
            if (sceneCamera == null || activeCharacterInstance == null)
            {
                return;
            }

            sceneCamera.transform.position = activeCharacterInstance.transform.position + cameraOffset;
            sceneCamera.transform.LookAt(activeCharacterInstance.transform.position + Vector3.up * 1.5f);
        }

        public bool Boot()
        {
            sceneCamera = Camera.main;
            areaLifecycle = new AreaLifecycleService();
            galleryService = new CharacterGalleryService();
            protocolAdapter = new ProtoNetworkProtocolAdapter(new Networking.SessionService(), new Networking.ExplorationSyncService());

            if (!WorldProjectLoader.TryLoadFromStreamingAssets(worldProjectFolder, out activeProject, out var loadError))
            {
                Debug.LogError($"[ProtoPlayableBootstrap] Failed loading world project: {loadError}");
                return false;
            }

            if (!areaLifecycle.TryActivateEntryArea(activeProject, out var areaError))
            {
                Debug.LogError($"[ProtoPlayableBootstrap] Failed activating entry area: {areaError}");
                return false;
            }

            if (!protocolAdapter.TryOpenSessionAsHost(sessionId, activeProject.Definition.ProjectId, out var sessionError))
            {
                Debug.LogError($"[ProtoPlayableBootstrap] Failed opening session: {sessionError}");
                return false;
            }

            if (!protocolAdapter.TryRegisterPlayer(new ProtoJoinRequest
                {
                    SenderClientId = HostClientId,
                    PlayerId = localPlayerId,
                    DisplayName = localDisplayName,
                    RequestedRole = localPlayerActsAsGm ? Networking.PlayerRole.GameMaster : Networking.PlayerRole.Player,
                }, out var registerError))
            {
                Debug.LogError($"[ProtoPlayableBootstrap] Failed registering local player: {registerError}");
                return false;
            }

            if (!galleryService.TryLoadFromStreamingAssets(worldProjectFolder, out var characters, out var galleryError))
            {
                Debug.LogError($"[ProtoPlayableBootstrap] Failed loading gallery: {galleryError}");
                return false;
            }

            var selectedCharacterId = SelectStarterCharacterId(characters);
            if (string.IsNullOrWhiteSpace(selectedCharacterId))
            {
                Debug.LogError("[ProtoPlayableBootstrap] No starter character available in gallery.");
                return false;
            }

            if (!protocolAdapter.TryAssignCharacter(new ProtoCharacterAssignRequest
                {
                    SenderClientId = HostClientId,
                    RequesterPlayerId = localPlayerId,
                    TargetPlayerId = localPlayerId,
                    CharacterId = selectedCharacterId,
                }, out var assignError))
            {
                Debug.LogError($"[ProtoPlayableBootstrap] Failed assigning local character: {assignError}");
                return false;
            }

            assignedCharacterId = selectedCharacterId;

            if (!protocolAdapter.TryHostArea(areaLifecycle.ActiveAreaId, out var hostAreaError))
            {
                Debug.LogError($"[ProtoPlayableBootstrap] Failed hosting exploration area: {hostAreaError}");
                return false;
            }

            var spawnPosition = TryResolveEntrySpawnPosition(activeProject.RootPath, areaLifecycle.ActiveAreaDefinition.EntrySpawnId, out var resolvedSpawn)
                ? resolvedSpawn
                : Vector3.zero;

            SpawnOrReplaceCharacter(spawnPosition);
            LastMovementStatus = "Ready. Use WASD/arrow keys to move.";
            Debug.Log($"[ProtoPlayableBootstrap] Boot complete. world='{activeProject.Definition.DisplayName}' area='{areaLifecycle.ActiveAreaId}' player='{localPlayerId}' character='{selectedCharacterId}'.");
            return true;
        }

        private string SelectStarterCharacterId(IReadOnlyList<CharacterRecord> characters)
        {
            if (characters == null || characters.Count == 0)
            {
                return string.Empty;
            }

            for (var i = 0; i < characters.Count; i++)
            {
                if (characters[i] != null && string.Equals(characters[i].Id, "pc_proto_fighter", StringComparison.Ordinal))
                {
                    return characters[i].Id;
                }
            }

            return characters[0]?.Id ?? string.Empty;
        }

        private void SpawnOrReplaceCharacter(Vector3 spawnPosition)
        {
            if (activeCharacterInstance != null)
            {
                DestroyImmediate(activeCharacterInstance);
                activeCharacterInstance = null;
            }

            if (donorCharacterPrefab == null)
            {
                Debug.LogWarning("[ProtoPlayableBootstrap] Donor character prefab is not assigned. No runtime avatar spawned.");
                SpawnStatus = "Spawn failed: donor prefab not assigned.";
                return;
            }

            activeCharacterInstance = Instantiate(donorCharacterPrefab, spawnPosition, Quaternion.identity);
            activeCharacterInstance.name = "ProtoPlayableCharacter";
            SpawnStatus = $"Spawned '{donorCharacterPrefab.name}' at ({spawnPosition.x:F1}, {spawnPosition.y:F1}, {spawnPosition.z:F1}).";
        }

        private static bool TryResolveEntrySpawnPosition(string worldRootPath, string entrySpawnId, out Vector3 position)
        {
            position = Vector3.zero;
            if (string.IsNullOrWhiteSpace(worldRootPath) || string.IsNullOrWhiteSpace(entrySpawnId))
            {
                return false;
            }

            var placementsPath = Path.Combine(worldRootPath, "Areas", "area_proto_village", "placements.json");
            if (!File.Exists(placementsPath))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(placementsPath);
                var file = JsonUtility.FromJson<PlacementFile>(json);
                if (file?.spawns == null)
                {
                    return false;
                }

                for (var i = 0; i < file.spawns.Count; i++)
                {
                    var spawn = file.spawns[i];
                    if (spawn != null && string.Equals(spawn.id, entrySpawnId, StringComparison.Ordinal))
                    {
                        position = new Vector3(spawn.position.x, spawn.position.y, spawn.position.z);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProtoPlayableBootstrap] Failed reading placements.json: {ex.Message}");
            }

            return false;
        }

        [Serializable]
        private sealed class PlacementFile
        {
            public List<SpawnRecord> spawns = new List<SpawnRecord>();
        }

        [Serializable]
        private sealed class SpawnRecord
        {
            public string id = string.Empty;
            public SerializableVector3 position;
        }

        [Serializable]
        private struct SerializableVector3
        {
            public float x;
            public float y;
            public float z;
        }
    }
}