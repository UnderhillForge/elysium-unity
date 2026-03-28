using Elysium.Networking;
using UnityEngine;

namespace Elysium.Boot
{
    public sealed class ElysiumBootstrap : MonoBehaviour
    {
        [SerializeField] private string defaultWorldProjectPath = "WorldProjects";
        [SerializeField] private SessionTopology sessionTopology = SessionTopology.HostMode;
        [SerializeField] private string defaultSessionId = "session_001";
        [SerializeField] private string defaultWorldProjectId = "starter_forest_edge";

        public string DefaultWorldProjectPath => defaultWorldProjectPath;
        public SessionTopology SessionTopology => sessionTopology;
        public string DefaultSessionId => defaultSessionId;
        public string DefaultWorldProjectId => defaultWorldProjectId;

        /// Active headless bootstrap instance — non-null only when
        /// SessionTopology == DedicatedServer and BootDedicatedServer() succeeded.
        public HeadlessSessionBootstrap HeadlessBootstrap { get; private set; }

        private void Start()
        {
            if (sessionTopology == SessionTopology.DedicatedServer)
            {
                BootDedicatedServer();
            }
            // Host mode is wired separately via ElysiumSessionManager (NGO NetworkBehaviour).
        }

        /// Starts the headless dedicated-server boot sequence using HeadlessNetworkTransport.
        /// Call this from Start() automatically or drive it manually from tests/tools.
        public bool BootDedicatedServer(
            string sessionId = null,
            string worldProjectFolder = null,
            INetworkTransport transport = null)
        {
            var resolvedSessionId = sessionId ?? defaultSessionId;
            var resolvedFolder = worldProjectFolder ?? defaultWorldProjectId;
            var resolvedTransport = transport ?? new HeadlessNetworkTransport();

            var sessionService = new SessionService();
            var bootstrap = new HeadlessSessionBootstrap(resolvedTransport, sessionService);

            if (!bootstrap.Boot(resolvedSessionId, resolvedFolder, out var error))
            {
                Debug.LogError($"[ElysiumBootstrap] Dedicated server boot failed: {error}");
                return false;
            }

            HeadlessBootstrap = bootstrap;
            return true;
        }
    }
}