using System;
using Elysium.Networking;
using UnityEngine;

namespace Elysium.Prototype.Networking
{
    /// Host-side adapter that accepts external/prototype protocol messages
    /// and routes all state mutation through Elysium authority services.
    ///
    /// Contract:
    /// - Caller never mutates SessionService or ExplorationSyncService directly.
    /// - Every request is identity-checked against sender client ID.
    /// - Role/ownership rules are delegated to SessionService.
    public sealed class ProtoNetworkProtocolAdapter
    {
        private readonly SessionService sessionService;
        private readonly ExplorationSyncService explorationService;

        public ProtoNetworkProtocolAdapter(
            SessionService sessionService,
            ExplorationSyncService explorationService)
        {
            this.sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            this.explorationService = explorationService ?? throw new ArgumentNullException(nameof(explorationService));
        }

        public SessionService Session => sessionService;
        public ExplorationSyncService Exploration => explorationService;

        public bool TryOpenSessionAsHost(string sessionId, string worldProjectId, out string error)
        {
            return sessionService.TryOpenSession(sessionId, worldProjectId, out error);
        }

        public bool TryRegisterPlayer(ProtoJoinRequest request, out string error)
        {
            error = string.Empty;
            if (request == null)
            {
                error = "Join request is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.PlayerId))
            {
                error = "PlayerId is required.";
                return false;
            }

            // Prevent client-ID impersonation across different player IDs.
            var existingByClient = sessionService.GetPlayerByClientId(request.SenderClientId);
            if (existingByClient != null && !string.Equals(existingByClient.PlayerId, request.PlayerId, StringComparison.Ordinal))
            {
                error = $"Client {request.SenderClientId} is already bound to player '{existingByClient.PlayerId}'.";
                return false;
            }

            var record = new PlayerSessionRecord
            {
                PlayerId = request.PlayerId,
                DisplayName = request.DisplayName ?? request.PlayerId,
                NetworkClientId = request.SenderClientId,
                Role = ResolveJoinRole(request),
            };

            return sessionService.TryRegisterPlayer(record, out error);
        }

        public bool TryAssignCharacter(ProtoCharacterAssignRequest request, out string error)
        {
            error = string.Empty;
            if (request == null)
            {
                error = "Character-assign request is null.";
                return false;
            }

            if (!TryValidateSenderIdentity(request.SenderClientId, request.RequesterPlayerId, out error))
            {
                return false;
            }

            return sessionService.TryAssignCharacter(
                request.RequesterPlayerId,
                request.TargetPlayerId,
                request.CharacterId,
                out error);
        }

        public bool TryHostArea(string areaId, out string error)
        {
            if (string.IsNullOrWhiteSpace(areaId))
            {
                error = "areaId is required.";
                return false;
            }

            explorationService.HostArea(areaId, "Exploration area hosted via protocol adapter.");
            error = string.Empty;
            return true;
        }

        public bool TrySubmitMovement(ProtoMovementRequest request, out string error)
        {
            error = string.Empty;
            if (request == null)
            {
                error = "Movement request is null.";
                return false;
            }

            if (!TryValidateSenderIdentity(request.SenderClientId, request.RequesterPlayerId, out error))
            {
                return false;
            }

            return explorationService.TryUpdateMovement(
                sessionService,
                request.RequesterPlayerId,
                request.AreaId,
                request.Position,
                request.FacingYaw,
                out error);
        }

        private PlayerRole ResolveJoinRole(ProtoJoinRequest request)
        {
            // Bootstrap GM is allowed only for local host client while no GM exists yet.
            if (request.RequestedRole == PlayerRole.GameMaster
                && !sessionService.HasGM
                && request.SenderClientId == 0)
            {
                return PlayerRole.GameMaster;
            }

            return PlayerRole.Player;
        }

        private bool TryValidateSenderIdentity(ulong senderClientId, string requesterPlayerId, out string error)
        {
            error = string.Empty;
            var player = sessionService.GetPlayerByClientId(senderClientId);
            if (player == null)
            {
                error = $"Client {senderClientId} is not registered in the session.";
                return false;
            }

            if (!string.Equals(player.PlayerId, requesterPlayerId, StringComparison.Ordinal))
            {
                error = $"Client {senderClientId} cannot act as '{requesterPlayerId}'.";
                return false;
            }

            return true;
        }
    }

    [Serializable]
    public sealed class ProtoJoinRequest
    {
        public ulong SenderClientId;
        public string PlayerId = string.Empty;
        public string DisplayName = string.Empty;
        public PlayerRole RequestedRole = PlayerRole.Player;
    }

    [Serializable]
    public sealed class ProtoCharacterAssignRequest
    {
        public ulong SenderClientId;
        public string RequesterPlayerId = string.Empty;
        public string TargetPlayerId = string.Empty;
        public string CharacterId = string.Empty;
    }

    [Serializable]
    public sealed class ProtoMovementRequest
    {
        public ulong SenderClientId;
        public string RequesterPlayerId = string.Empty;
        public string AreaId = string.Empty;
        public Vector3 Position = Vector3.zero;
        public float FacingYaw;
    }
}
