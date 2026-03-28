using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Elysium.Networking
{
    /// NGO bridge for host-authoritative exploration position replication.
    [DisallowMultipleComponent]
    public sealed class ExplorationNetworkCoordinator : NetworkBehaviour
    {
        private readonly NetworkVariable<FixedString4096Bytes> snapshotJson =
            new NetworkVariable<FixedString4096Bytes>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly ExplorationSyncService hostSyncService = new ExplorationSyncService();
        private ExplorationNetworkSnapshot replicatedSnapshot = new ExplorationNetworkSnapshot();

        public ExplorationNetworkSnapshot CurrentSnapshot => IsServer
            ? hostSyncService.CurrentSnapshot
            : replicatedSnapshot;

        public event System.Action<ExplorationNetworkSnapshot> SnapshotUpdated;

        private void Awake()
        {
            hostSyncService.SnapshotPublished += HandleHostSnapshotPublished;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            snapshotJson.OnValueChanged += HandleSnapshotJsonChanged;
        }

        public override void OnNetworkDespawn()
        {
            snapshotJson.OnValueChanged -= HandleSnapshotJsonChanged;
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            hostSyncService.SnapshotPublished -= HandleHostSnapshotPublished;
            base.OnDestroy();
        }

        public void HostArea(string areaId)
        {
            hostSyncService.HostArea(areaId);
        }

        public bool TrySubmitMovement(
            SessionService sessionService,
            string requesterId,
            string areaId,
            Vector3 position,
            float facingYaw,
            out string error)
        {
            if (!IsServer)
            {
                error = "Only the server/host can apply authoritative exploration movement in this coordinator.";
                return false;
            }

            return hostSyncService.TryUpdateMovement(sessionService, requesterId, areaId, position, facingYaw, out error);
        }

        private void HandleHostSnapshotPublished(ExplorationNetworkSnapshot snapshot)
        {
            if (!IsServer)
            {
                return;
            }

            replicatedSnapshot = snapshot;
            snapshotJson.Value = new FixedString4096Bytes(JsonUtility.ToJson(snapshot));
            SnapshotUpdated?.Invoke(snapshot);
        }

        private void HandleSnapshotJsonChanged(FixedString4096Bytes previousValue, FixedString4096Bytes newValue)
        {
            if (IsServer)
            {
                return;
            }

            replicatedSnapshot = string.IsNullOrEmpty(newValue.ToString())
                ? new ExplorationNetworkSnapshot()
                : JsonUtility.FromJson<ExplorationNetworkSnapshot>(newValue.ToString()) ?? new ExplorationNetworkSnapshot();
            SnapshotUpdated?.Invoke(replicatedSnapshot);
        }
    }
}