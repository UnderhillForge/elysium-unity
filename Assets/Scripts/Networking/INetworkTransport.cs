namespace Elysium.Networking
{
    /// Minimal abstraction over the network transport so boot and session logic
    /// can operate without direct NGO coupling. Implementations: NgoNetworkTransport
    /// (host mode) and HeadlessNetworkTransport (dedicated-server smoke/stub).
    public interface INetworkTransport
    {
        /// The runtime topology this transport operates under.
        SessionTopology Topology { get; }

        /// True after StartServer() or StartHost() has been called and not yet stopped.
        bool IsRunning { get; }

        /// Start listening as a server (dedicated or host).
        /// Returns false with a reason if startup failed.
        bool StartServer(out string error);

        /// Gracefully stop the transport.
        void Stop();
    }
}
