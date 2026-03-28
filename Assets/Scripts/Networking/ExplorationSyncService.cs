using System;
using System.Collections.Generic;
using UnityEngine;

namespace Elysium.Networking
{
    /// Host-authoritative exploration movement service.
    /// Players may move only while the session is in Lobby state. Combat locks movement.
    public sealed class ExplorationSyncService
    {
        private readonly Dictionary<string, ExplorationParticipantState> participants =
            new Dictionary<string, ExplorationParticipantState>(StringComparer.Ordinal);

        public event Action<ExplorationNetworkSnapshot> SnapshotPublished;

        public string ActiveAreaId { get; private set; } = string.Empty;
        public ExplorationNetworkSnapshot CurrentSnapshot { get; private set; } = new ExplorationNetworkSnapshot();

        public void HostArea(string areaId, string statusMessage = "Exploration synchronized.")
        {
            ActiveAreaId = areaId ?? string.Empty;
            PublishSnapshot(statusMessage);
        }

        public bool TryUpdateMovement(
            SessionService sessionService,
            string requesterId,
            string areaId,
            Vector3 position,
            float facingYaw,
            out string error)
        {
            if (sessionService == null)
            {
                error = "Session service is required.";
                return false;
            }

            if (sessionService.State != SessionState.Lobby)
            {
                error = $"Exploration movement is locked while session state is {sessionService.State}.";
                PublishSnapshot(error);
                return false;
            }

            if (string.IsNullOrWhiteSpace(ActiveAreaId))
            {
                error = "No active exploration area is hosted.";
                return false;
            }

            if (!string.Equals(ActiveAreaId, areaId, StringComparison.Ordinal))
            {
                error = $"Movement area '{areaId}' does not match active area '{ActiveAreaId}'.";
                PublishSnapshot(error);
                return false;
            }

            var player = sessionService.GetPlayer(requesterId);
            if (player == null)
            {
                error = $"Player '{requesterId}' is not registered in the session.";
                PublishSnapshot(error);
                return false;
            }

            if (!player.IsConnected)
            {
                error = $"Player '{requesterId}' is disconnected.";
                PublishSnapshot(error);
                return false;
            }

            if (string.IsNullOrWhiteSpace(player.AssignedCharacterId))
            {
                error = $"Player '{requesterId}' has no assigned character for exploration.";
                PublishSnapshot(error);
                return false;
            }

            participants[requesterId] = new ExplorationParticipantState
            {
                PlayerId = requesterId,
                CharacterId = player.AssignedCharacterId,
                AreaId = areaId,
                Position = position,
                FacingYaw = facingYaw,
                UpdatedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            error = string.Empty;
            PublishSnapshot($"Movement updated for {requesterId}.");
            return true;
        }

        public ExplorationParticipantState GetParticipant(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return null;
            }

            participants.TryGetValue(playerId, out var state);
            return state;
        }

        public void PublishSnapshot(string statusMessage)
        {
            var snapshot = new ExplorationNetworkSnapshot
            {
                AreaId = ActiveAreaId,
                PublishedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                StatusMessage = statusMessage ?? string.Empty,
            };

            foreach (var pair in participants)
            {
                snapshot.Participants.Add(pair.Value);
            }

            CurrentSnapshot = snapshot;
            SnapshotPublished?.Invoke(snapshot);
        }
    }
}