using Elysium.Networking;
using Elysium.Packaging;
using Elysium.World;
using UnityEngine;

namespace Elysium.Boot
{
    /// Orchestrates the dedicated-server boot sequence independently of NGO or any
    /// rendering subsystem. Designed to be exercised in headless (-nographics) batch
    /// mode and in smoke tests through HeadlessNetworkTransport.
    ///
    /// Boot sequence:
    ///   1. Start the transport (StartServer).
    ///   2. Load the configured world project via WorldProjectLoader.
    ///   3. Open the session on SessionService.
    ///   4. Activate the entry area via AreaLifecycleService (if provided).
    ///   5. Publish ready state.
    public sealed class HeadlessSessionBootstrap
    {
        private readonly INetworkTransport transport;
        private readonly SessionService sessionService;
        private readonly AreaLifecycleService areaLifecycle;

        public bool IsReady { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public SessionService Session => sessionService;
        public AreaLifecycleService AreaLifecycle => areaLifecycle;

        public HeadlessSessionBootstrap(
            INetworkTransport transport,
            SessionService sessionService,
            AreaLifecycleService areaLifecycle = null)
        {
            this.transport = transport ?? throw new System.ArgumentNullException(nameof(transport));
            this.sessionService = sessionService ?? throw new System.ArgumentNullException(nameof(sessionService));
            this.areaLifecycle = areaLifecycle;
        }

        /// Run the full boot sequence. Returns true on success.
        public bool Boot(string sessionId, string worldProjectFolder, out string error)
        {
            IsReady = false;
            LastError = string.Empty;

            // Step 1 — Start transport.
            if (!transport.StartServer(out error))
            {
                LastError = $"Transport start failed: {error}";
                return false;
            }

            // Step 2 — Load world project (read-only, no ownership enforcement on server side).
            if (!WorldProjectLoader.TryLoadFromStreamingAssets(worldProjectFolder, out var worldProject, out error))
            {
                transport.Stop();
                LastError = $"World project load failed: {error}";
                return false;
            }

            // Step 3 — Open session.
            if (!sessionService.TryOpenSession(sessionId, worldProject.Definition.ProjectId, out error))
            {
                transport.Stop();
                LastError = $"Session open failed: {error}";
                return false;
            }

            // Step 4 — Activate entry area (optional; skipped when no area lifecycle service is provided).
            if (areaLifecycle != null)
            {
                if (!areaLifecycle.TryActivateEntryArea(worldProject, out error))
                {
                    sessionService.CloseSession();
                    transport.Stop();
                    LastError = $"Entry area activation failed: {error}";
                    return false;
                }
            }

            IsReady = true;
            error = string.Empty;

            Debug.Log($"[HeadlessSessionBootstrap] Ready. Session={sessionId}, " +
                      $"World={worldProject.Definition.ProjectId}, " +
                      $"Area={areaLifecycle?.ActiveAreaId ?? "none"}, " +
                      $"Topology={transport.Topology}");
            return true;
        }

        public void Shutdown()
        {
            areaLifecycle?.DeactivateArea();
            transport.Stop();
            IsReady = false;
        }
    }
}
