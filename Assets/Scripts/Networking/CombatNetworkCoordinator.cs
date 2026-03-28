using Elysium.Combat;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Elysium.Networking
{
    /// NGO bridge that republishes authoritative combat snapshots from the host.
    [DisallowMultipleComponent]
    public sealed class CombatNetworkCoordinator : NetworkBehaviour
    {
        private readonly NetworkVariable<FixedString4096Bytes> snapshotJson =
            new NetworkVariable<FixedString4096Bytes>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly CombatNetworkService hostNetworkService = new CombatNetworkService();
        private CombatNetworkSnapshot replicatedSnapshot;

        public CombatNetworkSnapshot CurrentSnapshot => IsServer
            ? hostNetworkService.CurrentSnapshot
            : replicatedSnapshot;

        public event System.Action<CombatNetworkSnapshot> SnapshotUpdated;

        private void Awake()
        {
            hostNetworkService.SnapshotPublished += HandleHostSnapshotPublished;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            snapshotJson.OnValueChanged += HandleSnapshotJsonChanged;

            if (!snapshotJson.Value.Equals(default(FixedString4096Bytes)))
            {
                replicatedSnapshot = DeserializeSnapshot(snapshotJson.Value.ToString());
            }
        }

        public override void OnNetworkDespawn()
        {
            snapshotJson.OnValueChanged -= HandleSnapshotJsonChanged;
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            hostNetworkService.SnapshotPublished -= HandleHostSnapshotPublished;
            base.OnDestroy();
        }

        public bool StartHostedEncounter(CombatStateService combatState, out string error)
        {
            if (!IsServer)
            {
                error = "Only the server/host can start a networked combat encounter.";
                return false;
            }

            hostNetworkService.HostEncounter(combatState, "Networked combat encounter hosted.");
            error = string.Empty;
            return true;
        }

        public void SubmitActionRequest(
            string requesterId,
            TurnActionRequest request)
        {
            if (IsServer)
            {
                if (!hostNetworkService.TrySubmitAction(requesterId, request, out _, out var error))
                {
                    hostNetworkService.PublishStatus(error);
                }

                return;
            }

            SubmitActionRequestServerRpc(
                requesterId,
                request.CombatantId,
                request.ActionName,
                request.Description,
                (int)request.ActionType,
                request.TargetCombatantId);
        }

        public void ResolvePendingAction(
            string requesterId,
            string actionId,
            bool approved,
            string resolutionText)
        {
            if (IsServer)
            {
                if (!hostNetworkService.TryResolvePendingAction(requesterId, actionId, approved, resolutionText, out var error))
                {
                    hostNetworkService.PublishStatus(error);
                }

                return;
            }

            ResolvePendingActionServerRpc(requesterId, actionId, approved, resolutionText);
        }

        public void EndTurn(string requesterId)
        {
            if (IsServer)
            {
                if (!hostNetworkService.TryEndTurn(requesterId, out var error))
                {
                    hostNetworkService.PublishStatus(error);
                }

                return;
            }

            EndTurnServerRpc(requesterId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitActionRequestServerRpc(
            string requesterId,
            string combatantId,
            string actionName,
            string description,
            int actionType,
            string targetCombatantId,
            ServerRpcParams rpcParams = default)
        {
            var request = new TurnActionRequest
            {
                CombatantId = combatantId,
                ActionName = actionName,
                Description = description,
                ActionType = (TurnActionType)actionType,
                TargetCombatantId = targetCombatantId
            };

            if (!hostNetworkService.TrySubmitAction(requesterId, request, out _, out var error))
            {
                hostNetworkService.PublishStatus(error);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void ResolvePendingActionServerRpc(
            string requesterId,
            string actionId,
            bool approved,
            string resolutionText,
            ServerRpcParams rpcParams = default)
        {
            if (!hostNetworkService.TryResolvePendingAction(requesterId, actionId, approved, resolutionText, out var error))
            {
                hostNetworkService.PublishStatus(error);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void EndTurnServerRpc(string requesterId, ServerRpcParams rpcParams = default)
        {
            if (!hostNetworkService.TryEndTurn(requesterId, out var error))
            {
                hostNetworkService.PublishStatus(error);
            }
        }

        private void HandleHostSnapshotPublished(CombatNetworkSnapshot snapshot)
        {
            if (!IsServer)
            {
                return;
            }

            replicatedSnapshot = snapshot;
            snapshotJson.Value = SerializeSnapshot(snapshot);
            SnapshotUpdated?.Invoke(snapshot);
        }

        private void HandleSnapshotJsonChanged(FixedString4096Bytes previousValue, FixedString4096Bytes newValue)
        {
            if (IsServer)
            {
                return;
            }

            replicatedSnapshot = DeserializeSnapshot(newValue.ToString());
            SnapshotUpdated?.Invoke(replicatedSnapshot);
        }

        private static FixedString4096Bytes SerializeSnapshot(CombatNetworkSnapshot snapshot)
        {
            var json = JsonUtility.ToJson(snapshot);
            if (json.Length > 4000)
            {
                var overflowSnapshot = new CombatNetworkSnapshot
                {
                    EncounterId = snapshot.EncounterId,
                    CombatMode = snapshot.CombatMode,
                    GMId = snapshot.GMId,
                    IsActive = snapshot.IsActive,
                    CurrentRound = snapshot.CurrentRound,
                    CurrentCombatantId = snapshot.CurrentCombatantId,
                    CurrentCombatantName = snapshot.CurrentCombatantName,
                    PublishedAtUtc = snapshot.PublishedAtUtc,
                    StatusMessage = "Snapshot truncated for NGO transport."
                };

                json = JsonUtility.ToJson(overflowSnapshot);
            }

            return new FixedString4096Bytes(json);
        }

        private static CombatNetworkSnapshot DeserializeSnapshot(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new CombatNetworkSnapshot();
            }

            var snapshot = JsonUtility.FromJson<CombatNetworkSnapshot>(json);
            return snapshot ?? new CombatNetworkSnapshot();
        }
    }
}