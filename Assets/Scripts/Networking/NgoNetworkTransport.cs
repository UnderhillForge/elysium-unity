using Unity.Netcode;
using UnityEngine;

namespace Elysium.Networking
{
    /// INetworkTransport implementation for host mode using Unity Netcode for GameObjects.
    /// Delegates to NetworkManager.Singleton so the smoke suite and production path share
    /// the same transport contract.
    public sealed class NgoNetworkTransport : INetworkTransport
    {
        public SessionTopology Topology => SessionTopology.HostMode;

        public bool IsRunning =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        public bool StartServer(out string error)
        {
            if (NetworkManager.Singleton == null)
            {
                error = "NetworkManager.Singleton is null — ensure a NetworkManager is present in the scene.";
                return false;
            }

            if (NetworkManager.Singleton.IsListening)
            {
                error = "NetworkManager is already listening.";
                return false;
            }

            var started = NetworkManager.Singleton.StartHost();
            if (!started)
            {
                error = "NetworkManager.StartHost() returned false.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Stop()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }
    }
}
