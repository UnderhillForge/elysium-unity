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

        // Replicated session info — broadcast to all clients.
        private readonly NetworkVariable<FixedString512Bytes> sessionInfoJson =
            new NetworkVariable<FixedString512Bytes>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public event Action<SessionInfo> SessionInfoUpdated;
        public event Action<CombatNetworkSnapshot> CombatSnapshotUpdated;

        // Host-side authoritative session access.
        public SessionService Session => sessionService;

        // Latest session info visible on all peers.
        private SessionInfo cachedSessionInfo;
        public SessionInfo CurrentSessionInfo => cachedSessionInfo;

        // Latest combat snapshot — server: from service; clients: from replicated variable.
        private CombatNetworkSnapshot cachedCombatSnapshot;
        public CombatNetworkSnapshot CurrentCombatSnapshot => cachedCombatSnapshot;

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
            var record = new PlayerSessionRecord
            {
                PlayerId = playerId,
                DisplayName = displayName,
                NetworkClientId = rpcParams.Receive.SenderClientId,
                Role = PlayerRole.Player,
            };

            if (!sessionService.TryRegisterPlayer(record, out var error))
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
            // Verify the network client matches the requester.
            var player = sessionService.GetPlayerByClientId(rpcParams.Receive.SenderClientId);
            if (player == null || !string.Equals(player.PlayerId, requesterId, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[Session] Client {rpcParams.Receive.SenderClientId} tried to act as '{requesterId}'.");
                return;
            }

            var request = new TurnActionRequest
            {
                CombatantId = combatantId,
                ActionName = actionName,
                Description = description,
                ActionType = (TurnActionType)actionTypeIndex,
                TargetCombatantId = targetCombatantId
            };

            if (!combatNetworkService.TrySubmitAction(requesterId, request, out _, out var error))
            {
                Debug.LogWarning($"[Session] Action rejected for {requesterId}: {error}");
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
            var player = sessionService.GetPlayerByClientId(rpcParams.Receive.SenderClientId);
            if (player == null || !string.Equals(player.PlayerId, gmPlayerId, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[Session] Client {rpcParams.Receive.SenderClientId} tried to resolve as GM.");
                return;
            }

            if (!combatNetworkService.TryResolvePendingAction(gmPlayerId, actionId, approved, resolutionText, out var error))
            {
                Debug.LogWarning($"[Session] Action resolution failed for {gmPlayerId}: {error}");
            }
        }

        /// Current combatant ends their turn.
        [ServerRpc(RequireOwnership = false)]
        public void EndTurnServerRpc(string requesterId, ServerRpcParams rpcParams = default)
        {
            var player = sessionService.GetPlayerByClientId(rpcParams.Receive.SenderClientId);
            if (player == null || !string.Equals(player.PlayerId, requesterId, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[Session] Client {rpcParams.Receive.SenderClientId} tried to end turn as '{requesterId}'.");
                return;
            }

            if (!combatNetworkService.TryEndTurn(requesterId, out var error))
            {
                Debug.LogWarning($"[Session] End turn rejected for {requesterId}: {error}");
            }
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
