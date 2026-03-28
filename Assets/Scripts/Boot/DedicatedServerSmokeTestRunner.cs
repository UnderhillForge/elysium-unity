using System;
using System.Text;
using Elysium.Networking;
using UnityEngine;

namespace Elysium.Boot
{
    /// Exercises the dedicated-server headless boot path end-to-end without NGO or rendering.
    /// Covers:
    ///   1. HeadlessNetworkTransport starts and reports IsRunning.
    ///   2. HeadlessSessionBootstrap boots with the starter world project.
    ///   3. Session opens in Lobby state with the correct world project ID.
    ///   4. Shutdown clears IsRunning and IsReady.
    ///   5. A second boot attempt on an already-open SessionService fails gracefully.
    ///   6. ElysiumBootstrap.BootDedicatedServer() integration path works end-to-end.
    public sealed class DedicatedServerSmokeTestRunner : MonoBehaviour
    {
        private const string SourceProjectFolder = "starter_forest_edge";
        private const string ExpectedProjectId = "com.elysium.world.starter-forest-edge";
        private const string SmokeSessionId = "smoke_dedicated_server_session";

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        public void RunDedicatedServerSmokeTest()
        {
            try
            {
                LastSummary = RunDedicatedServerSmokeTestInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"Dedicated server smoke test failed: {ex}");
            }
        }

        private string RunDedicatedServerSmokeTestInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Dedicated Server Boot Smoke Test ===");

            // 1. Transport starts and reports IsRunning.
            var transport = new HeadlessNetworkTransport();
            if (transport.IsRunning)
                throw new InvalidOperationException("HeadlessNetworkTransport should not be running before StartServer().");

            if (!transport.StartServer(out var tErr))
                throw new InvalidOperationException($"HeadlessNetworkTransport.StartServer() failed: {tErr}");

            if (!transport.IsRunning)
                throw new InvalidOperationException("HeadlessNetworkTransport.IsRunning should be true after StartServer().");

            if (transport.Topology != SessionTopology.DedicatedServer)
                throw new InvalidOperationException($"Topology should be DedicatedServer, got {transport.Topology}.");

            transport.Stop();
            if (transport.IsRunning)
                throw new InvalidOperationException("HeadlessNetworkTransport.IsRunning should be false after Stop().");

            log.AppendLine("HeadlessNetworkTransport lifecycle — ok");

            // 2-3. HeadlessSessionBootstrap boots, opens session in Lobby.
            var sessionService = new SessionService();
            var freshTransport = new HeadlessNetworkTransport();
            var bootstrap = new HeadlessSessionBootstrap(freshTransport, sessionService);

            if (!bootstrap.Boot(SmokeSessionId, SourceProjectFolder, out var bootErr))
                throw new InvalidOperationException($"HeadlessSessionBootstrap.Boot() failed: {bootErr}");

            if (!bootstrap.IsReady)
                throw new InvalidOperationException("Bootstrap.IsReady should be true after successful Boot().");

            if (sessionService.State != SessionState.Lobby)
                throw new InvalidOperationException($"Session state should be Lobby, got {sessionService.State}.");

            if (sessionService.SessionId != SmokeSessionId)
                throw new InvalidOperationException($"Session ID mismatch: expected '{SmokeSessionId}', got '{sessionService.SessionId}'.");

            if (sessionService.WorldProjectId != ExpectedProjectId)
                throw new InvalidOperationException(
                    $"WorldProjectId mismatch: expected '{ExpectedProjectId}', got '{sessionService.WorldProjectId}'.");

            log.AppendLine($"Session open — state={sessionService.State}, world='{sessionService.WorldProjectId}' — ok");

            // 4. Shutdown clears state.
            bootstrap.Shutdown();
            if (bootstrap.IsReady)
                throw new InvalidOperationException("Bootstrap.IsReady should be false after Shutdown().");
            if (freshTransport.IsRunning)
                throw new InvalidOperationException("Transport.IsRunning should be false after Shutdown().");

            log.AppendLine("Shutdown cleared state — ok");

            // 5. Double-boot (reusing already-open SessionService) must fail gracefully.
            var anotherTransport = new HeadlessNetworkTransport();
            var anotherBootstrap = new HeadlessSessionBootstrap(anotherTransport, sessionService);
            if (anotherBootstrap.Boot("should_fail", SourceProjectFolder, out var doubleBootErr))
                throw new InvalidOperationException("Second boot on an already-open session should have failed.");
            if (string.IsNullOrEmpty(doubleBootErr))
                throw new InvalidOperationException("Double-boot failure did not produce an error message.");

            log.AppendLine($"Double-boot rejection: '{doubleBootErr}' — ok");

            // 6. ElysiumBootstrap.BootDedicatedServer() integration path.
            var bootstrapComponent = gameObject.AddComponent<ElysiumBootstrap>();
            var injectedTransport = new HeadlessNetworkTransport();
            var success = bootstrapComponent.BootDedicatedServer(
                sessionId: "smoke_integration_session",
                worldProjectFolder: SourceProjectFolder,
                transport: injectedTransport);

            if (!success)
                throw new InvalidOperationException("ElysiumBootstrap.BootDedicatedServer() returned false.");

            if (bootstrapComponent.HeadlessBootstrap == null)
                throw new InvalidOperationException("ElysiumBootstrap.HeadlessBootstrap is null after successful boot.");

            if (bootstrapComponent.HeadlessBootstrap.Session.State != SessionState.Lobby)
                throw new InvalidOperationException(
                    $"Integration session state should be Lobby, got {bootstrapComponent.HeadlessBootstrap.Session.State}.");

            log.AppendLine("ElysiumBootstrap.BootDedicatedServer() integration — ok");

            log.AppendLine("=== Dedicated Server Boot Smoke Test COMPLETE ===");
            return log.ToString();
        }
    }
}
