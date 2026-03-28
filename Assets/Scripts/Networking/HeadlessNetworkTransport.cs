namespace Elysium.Networking
{
    /// INetworkTransport stub for the dedicated-server boot path.
    /// Used in headless (no rendering) contexts and in smoke tests where NGO
    /// NetworkManager is unavailable or undesirable. StartServer() always succeeds
    /// so session lifecycle and world-load logic can be exercised independently.
    public sealed class HeadlessNetworkTransport : INetworkTransport
    {
        public SessionTopology Topology => SessionTopology.DedicatedServer;

        public bool IsRunning { get; private set; }

        public bool StartServer(out string error)
        {
            IsRunning = true;
            error = string.Empty;
            return true;
        }

        public void Stop()
        {
            IsRunning = false;
        }
    }
}
