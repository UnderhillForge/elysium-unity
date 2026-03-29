using System;
using System.Collections.Generic;
using Elysium.Combat;
using Elysium.Persistence;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Elysium.Networking
{
    /// NGO-integrated session manager. Runs on the host. Clients receive state via
    /// replicated NetworkVariables and call server RPCs to register or submit actions.
    [DisallowMultipleComponent]
    public sealed class ElysiumSessionManager : NetworkBehaviour
    {
        // Authoritative state — host only.
        private readonly SessionService sessionService = new SessionService();
        private CombatNetworkService combatNetworkService;
        private ExplorationSyncService explorationSyncService;

        // Replicated session info — broadcast to all clients.
        private readonly NetworkVariable<FixedString512Bytes> sessionInfoJson =
            new NetworkVariable<FixedString512Bytes>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public event Action<SessionInfo> SessionInfoUpdated;
        public event Action<CombatNetworkSnapshot> CombatSnapshotUpdated;
        public event Action<ExplorationNetworkSnapshot> ExplorationSnapshotUpdated;

        // Host-side authoritative session access.
        public SessionService Session => sessionService;

        // Latest session info visible on all peers.
        private SessionInfo cachedSessionInfo;
        public SessionInfo CurrentSessionInfo => cachedSessionInfo;

        // Latest combat snapshot — server: from service; clients: from replicated variable.
        private CombatNetworkSnapshot cachedCombatSnapshot;
        public CombatNetworkSnapshot CurrentCombatSnapshot => cachedCombatSnapshot;

        private ExplorationNetworkSnapshot cachedExplorationSnapshot;
        public ExplorationNetworkSnapshot CurrentExplorationSnapshot => cachedExplorationSnapshot;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            sessionInfoJson.OnValueChanged += HandleSessionInfoChanged;

            if (!sessionInfoJson.Value.Equals(default(FixedString512Bytes)))
            {
                cachedSessionInfo = ParseSessionInfo(sessionInfoJson.Value.ToString());
                SessionInfoUpdated?.Invoke(cachedSessionInfo);
            }

            if (IsServer)
            {
                combatNetworkService = new CombatNetworkService();
                combatNetworkService.SnapshotPublished += HandleCombatSnapshot;
                explorationSyncService = new ExplorationSyncService();
                explorationSyncService.SnapshotPublished += HandleExplorationSnapshot;
                NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            }
        }

        public override void OnNetworkDespawn()
        {
            sessionInfoJson.OnValueChanged -= HandleSessionInfoChanged;

            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            if (explorationSyncService != null)
            {
                explorationSyncService.SnapshotPublished -= HandleExplorationSnapshot;
            }

            base.OnNetworkDespawn();
        }

        // ── Host-Side Session API ─────────────────────────────────────────────

        /// Open a new session (call from host before accepting players).
        public bool OpenSession(string sessionId, string worldProjectId, out string error)
        {
            if (!IsServer)
            {
                error = "Only the host can open a session.";
                return false;
            }

            if (!sessionService.TryOpenSession(sessionId, worldProjectId, out error))
            {
                return false;
            }

            PublishSessionInfo();
            explorationSyncService?.HostArea(worldProjectId, "Exploration session opened.");
            return true;
        }

        /// Assign GM designation to a player (host only).
        public bool AssignGM(string hostPlayerId, string targetPlayerId, out string error)
        {
            if (!IsServer)
            {
                error = "Only the host can assign the GM role.";
                return false;
            }

            return sessionService.TrySetRole(hostPlayerId, targetPlayerId, PlayerRole.GameMaster, out error);
        }

        /// Assign a combatant to a player (GM only).
        public bool AssignCombatant(string gmPlayerId, string targetPlayerId, string combatantId, out string error)
        {
            if (!IsServer)
            {
                error = "Only the host can assign combatants.";
                return false;
            }

            return sessionService.TryAssignCombatant(gmPlayerId, targetPlayerId, combatantId, out error);
        }

        /// Assign a character selection to a player (GM only).
        public bool AssignCharacter(string gmPlayerId, string targetPlayerId, string characterId, out string error)
        {
            if (!IsServer)
            {
                error = "Only the host can assign characters.";
                return false;
            }

            return sessionService.TryAssignCharacter(gmPlayerId, targetPlayerId, characterId, out error);
        }

        /// Submit exploration movement for a player (host-authoritative).
        public bool SubmitExplorationMovement(
            string requesterId,
            string areaId,
            Vector3 position,
            float facingYaw,
            out string error)
        {
            if (!IsServer)
            {
                error = "Only the host can accept exploration movement updates.";
                return false;
            }

            if (explorationSyncService == null)
            {
                explorationSyncService = new ExplorationSyncService();
                explorationSyncService.SnapshotPublished += HandleExplorationSnapshot;
            }

            if (string.IsNullOrWhiteSpace(explorationSyncService.ActiveAreaId))
            {
                explorationSyncService.HostArea(areaId, "Exploration area hosted.");
            }

            return explorationSyncService.TryUpdateMovement(sessionService, requesterId, areaId, position, facingYaw, out error);
        }

        /// Start a hosted encounter (GM only, session must be in Lobby state).
        public bool StartEncounter(string gmPlayerId, CombatStateService combatState, out string error)
        {
            if (!IsServer)
            {
                error = "Only the host can start an encounter.";
                return false;
            }

            if (!sessionService.TryStartCombat(gmPlayerId, out error))
            {
                return false;
            }

            combatNetworkService.HostEncounter(combatState, "Encounter started.");
            cachedCombatSnapshot = combatNetworkService.CurrentSnapshot;
            PublishSessionInfo();
            return true;
        }

        /// End the active encounter and return to Lobby.
        public void EndEncounter()
        {
            if (!IsServer)
            {
                return;
            }

            sessionService.EndCombat();
            PublishSessionInfo();
        }

        /// Save the current host session and encounter state into campaign.db.
        public bool TrySaveCampaignState(
            string campaignDatabasePath,
            string encounterInstanceId,
            string areaId,
            out string error)
        {
            if (!IsServer)
            {
                error = "Only the host can save campaign state.";
                return false;
            }

            var persistence = new CampaignPersistenceService(campaignDatabasePath);
            return persistence.TrySaveCampaignState(
                sessionService,
                encounterInstanceId,
                combatNetworkService?.HostedCombatState?.EncounterId ?? string.Empty,
                areaId,
                combatNetworkService?.HostedCombatState,
                out error);
        }

        /// Restore session and encounter state from campaign.db.
        public bool TryLoadCampaignState(
            string campaignDatabasePath,
            string encounterInstanceId,
            out string error)
        {
            if (!IsServer)
            {
                error = "Only the host can load campaign state.";
                return false;
            }

            var persistence = new CampaignPersistenceService(campaignDatabasePath);
            if (!persistence.TryLoadCampaignState(encounterInstanceId, out var loadedSession, out var loadedCombat, out error))
            {
                return false;
            }

            sessionService.RestoreFromSnapshot(loadedSession.CreateSnapshot());

            if (combatNetworkService == null)
            {
                combatNetworkService = new CombatNetworkService();
                combatNetworkService.SnapshotPublished += HandleCombatSnapshot;
            }

            if (loadedCombat != null)
            {
                combatNetworkService.HostEncounter(loadedCombat, "Campaign state restored from database.");
                cachedCombatSnapshot = combatNetworkService.CurrentSnapshot;
            }

            PublishSessionInfo();
            return true;
        }

        // ── Client-Facing Server RPCs ─────────────────────────────────────────

        /// Players call this to join the session.
        [ServerRpc(RequireOwnership = false)]
        public void RegisterPlayerServerRpc(
            string playerId,
            string displayName,
            ServerRpcParams rpcParams = default)
        {
            if (!TryRegisterPlayerFromClient(
                    sessionService,
                    rpcParams.Receive.SenderClientId,
                    playerId,
                    displayName,
                    out var record,
                    out var error))
            {
                Debug.LogWarning($"[Session] Could not register player {displayName}: {error}");
                return;
            }

            Debug.Log($"[Session] Player joined: {record}");
            PublishSessionInfo();
        }

        /// Players submit action requests through here.
        [ServerRpc(RequireOwnership = false)]
        public void SubmitActionServerRpc(
            string requesterId,
            string combatantId,
            string actionName,
            string description,
            int actionTypeIndex,
            string targetCombatantId,
            ServerRpcParams rpcParams = default)
        {
            var request = new TurnActionRequest
            {
                CombatantId = combatantId,
                ActionName = actionName,
                Description = description,
                ActionType = (TurnActionType)actionTypeIndex,
                TargetCombatantId = targetCombatantId
            };

            if (!TrySubmitActionFromClient(
                    sessionService,
                    combatNetworkService,
                    rpcParams.Receive.SenderClientId,
                    requesterId,
                    request,
                    out _,
                    out var error))
            {
                Debug.LogWarning($"[Session] Action rejected for {requesterId}: {error}");
            }
        }

        /// Players submit exploration movement through here.
        [ServerRpc(RequireOwnership = false)]
        public void SubmitExplorationMovementServerRpc(
            string requesterId,
            string areaId,
            Vector3 position,
            float facingYaw,
            ServerRpcParams rpcParams = default)
        {
            if (explorationSyncService == null)
            {
                explorationSyncService = new ExplorationSyncService();
                explorationSyncService.SnapshotPublished += HandleExplorationSnapshot;
            }

            if (string.IsNullOrWhiteSpace(explorationSyncService.ActiveAreaId))
            {
                explorationSyncService.HostArea(areaId, "Exploration area hosted from movement RPC.");
            }

            if (!TrySubmitExplorationMovementFromClient(
                    sessionService,
                    explorationSyncService,
                    rpcParams.Receive.SenderClientId,
                    requesterId,
                    areaId,
                    position,
                    facingYaw,
                    out var error))
            {
                Debug.LogWarning($"[Session] Exploration movement rejected for {requesterId}: {error}");
            }
        }

        /// GM resolves a pending action.
        [ServerRpc(RequireOwnership = false)]
        public void ResolveActionServerRpc(
            string gmPlayerId,
            string actionId,
            bool approved,
            string resolutionText,
            ServerRpcParams rpcParams = default)
        {
            if (!TryResolveActionFromClient(
                    sessionService,
                    combatNetworkService,
                    rpcParams.Receive.SenderClientId,
                    gmPlayerId,
                    actionId,
                    approved,
                    resolutionText,
                    out var error))
            {
                Debug.LogWarning($"[Session] Action resolution failed for {gmPlayerId}: {error}");
            }
        }

        /// Current combatant ends their turn.
        [ServerRpc(RequireOwnership = false)]
        public void EndTurnServerRpc(string requesterId, ServerRpcParams rpcParams = default)
        {
            if (!TryEndTurnFromClient(
                    sessionService,
                    combatNetworkService,
                    rpcParams.Receive.SenderClientId,
                    requesterId,
                    out var error))
            {
                Debug.LogWarning($"[Session] End turn rejected for {requesterId}: {error}");
            }
        }

        internal static bool TryRegisterPlayerFromClient(
            SessionService sessionService,
            ulong senderClientId,
            string playerId,
            string displayName,
            out PlayerSessionRecord record,
            out string error)
        {
            record = null;
            error = string.Empty;

            if (sessionService == null)
            {
                error = "Session service is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(playerId))
            {
                error = "PlayerId is required.";
                return false;
            }

            var existingByClient = sessionService.GetPlayerByClientId(senderClientId);
            if (existingByClient != null && !string.Equals(existingByClient.PlayerId, playerId, StringComparison.Ordinal))
            {
                error = $"Client {senderClientId} is already bound to player '{existingByClient.PlayerId}'.";
                return false;
            }

            record = new PlayerSessionRecord
            {
                PlayerId = playerId,
                DisplayName = displayName,
                NetworkClientId = senderClientId,
                Role = PlayerRole.Player,
            };

            return sessionService.TryRegisterPlayer(record, out error);
        }

        internal static bool TrySubmitExplorationMovementFromClient(
            SessionService sessionService,
            ExplorationSyncService explorationSyncService,
            ulong senderClientId,
            string requesterId,
            string areaId,
            Vector3 position,
            float facingYaw,
            out string error)
        {
            if (!TryAuthorizeClientPlayer(sessionService, senderClientId, requesterId, out _, out error))
            {
                return false;
            }

            if (explorationSyncService == null)
            {
                error = "Exploration sync service is required.";
                return false;
            }

            return explorationSyncService.TryUpdateMovement(sessionService, requesterId, areaId, position, facingYaw, out error);
        }

        internal static bool TrySubmitActionFromClient(
            SessionService sessionService,
            CombatNetworkService combatNetworkService,
            ulong senderClientId,
            string requesterId,
            TurnActionRequest request,
            out ActionResolution resolution,
            out string error)
        {
            resolution = null;
            if (!TryAuthorizeClientPlayer(sessionService, senderClientId, requesterId, out var player, out error))
            {
                return false;
            }

            if (combatNetworkService == null)
            {
                error = "Combat network service is required.";
                return false;
            }

            if (!TryResolveEffectiveCombatRequesterId(player, out var effectiveRequesterId, out error))
            {
                return false;
            }

            return combatNetworkService.TrySubmitAction(effectiveRequesterId, request, out resolution, out error);
        }

        internal static bool TryResolveActionFromClient(
            SessionService sessionService,
            CombatNetworkService combatNetworkService,
            ulong senderClientId,
            string requesterId,
            string actionId,
            bool approved,
            string resolutionText,
            out string error)
        {
            if (!TryAuthorizeClientPlayer(sessionService, senderClientId, requesterId, out var player, out error))
            {
                return false;
            }

            if (combatNetworkService == null)
            {
                error = "Combat network service is required.";
                return false;
            }

            if (!TryResolveEffectiveCombatRequesterId(player, out var effectiveRequesterId, out error))
            {
                return false;
            }

            return combatNetworkService.TryResolvePendingAction(effectiveRequesterId, actionId, approved, resolutionText, out error);
        }

        internal static bool TryEndTurnFromClient(
            SessionService sessionService,
            CombatNetworkService combatNetworkService,
            ulong senderClientId,
            string requesterId,
            out string error)
        {
            if (!TryAuthorizeClientPlayer(sessionService, senderClientId, requesterId, out var player, out error))
            {
                return false;
            }

            if (combatNetworkService == null)
            {
                error = "Combat network service is required.";
                return false;
            }

            if (!TryResolveEffectiveCombatRequesterId(player, out var effectiveRequesterId, out error))
            {
                return false;
            }

            return combatNetworkService.TryEndTurn(effectiveRequesterId, out error);
        }

        internal static bool TryResolveEffectiveCombatRequesterId(
            PlayerSessionRecord player,
            out string effectiveRequesterId,
            out string error)
        {
            effectiveRequesterId = string.Empty;
            error = string.Empty;

            if (player == null)
            {
                error = "Player record is required.";
                return false;
            }

            if (player.IsGM)
            {
                effectiveRequesterId = player.PlayerId;
                return true;
            }

            if (string.IsNullOrWhiteSpace(player.AssignedCombatantId))
            {
                error = $"Player '{player.PlayerId}' has no assigned combatant.";
                return false;
            }

            effectiveRequesterId = player.AssignedCombatantId;
            return true;
        }

        internal static bool TryAuthorizeClientPlayer(
            SessionService sessionService,
            ulong senderClientId,
            string requesterId,
            out PlayerSessionRecord player,
            out string error)
        {
            player = null;
            error = string.Empty;

            if (sessionService == null)
            {
                error = "Session service is required.";
                return false;
            }

            player = sessionService.GetPlayerByClientId(senderClientId);
            if (player == null)
            {
                error = $"Client {senderClientId} is not registered in the session.";
                return false;
            }

            if (!string.Equals(player.PlayerId, requesterId, StringComparison.Ordinal))
            {
                error = $"Client {senderClientId} cannot act as '{requesterId}'.";
                return false;
            }

            return true;
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private void HandleClientConnected(ulong clientId)
        {
            Debug.Log($"[Session] NGO client connected: {clientId}");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            var player = sessionService.GetPlayerByClientId(clientId);
            if (player != null)
            {
                sessionService.TryDisconnectPlayer(player.PlayerId, out _);
                Debug.Log($"[Session] Player disconnected: {player.DisplayName} (client {clientId})");
                PublishSessionInfo();
            }
        }

        private void HandleCombatSnapshot(CombatNetworkSnapshot snapshot)
        {
            cachedCombatSnapshot = snapshot;
            CombatSnapshotUpdated?.Invoke(snapshot);
        }

        private void HandleExplorationSnapshot(ExplorationNetworkSnapshot snapshot)
        {
            cachedExplorationSnapshot = snapshot;
            ExplorationSnapshotUpdated?.Invoke(snapshot);
        }

        private void HandleSessionInfoChanged(
            FixedString512Bytes previousValue,
            FixedString512Bytes newValue)
        {
            if (IsServer)
            {
                return;
            }

            cachedSessionInfo = ParseSessionInfo(newValue.ToString());
            SessionInfoUpdated?.Invoke(cachedSessionInfo);
        }

        private void PublishSessionInfo()
        {
            var info = new SessionInfo
            {
                SessionId = sessionService.SessionId,
                WorldProjectId = sessionService.WorldProjectId,
                State = sessionService.State,
                GMPlayerId = sessionService.GMPlayerId,
                PlayerCount = 0,
                ConnectedCount = 0,
            };

            foreach (var p in sessionService.Players)
            {
                info.PlayerCount++;
                if (p.IsConnected)
                {
                    info.ConnectedCount++;
                }
            }

            cachedSessionInfo = info;
            SessionInfoUpdated?.Invoke(info);

            var json = JsonUtility.ToJson(info);
            if (json.Length <= 512)
            {
                sessionInfoJson.Value = new FixedString512Bytes(json);
            }
        }

        private static SessionInfo ParseSessionInfo(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new SessionInfo();
            }

            return JsonUtility.FromJson<SessionInfo>(json) ?? new SessionInfo();
        }
    }

    /// Compact, serialisable projection of session state replicated to clients.
    [Serializable]
    public sealed class SessionInfo
    {
        public string SessionId = string.Empty;
        public string WorldProjectId = string.Empty;
        public SessionState State = SessionState.Idle;
        public string GMPlayerId = string.Empty;
        public int PlayerCount;
        public int ConnectedCount;
    }
}
