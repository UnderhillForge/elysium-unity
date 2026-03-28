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
    }
}