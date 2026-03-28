using System;
using System.Collections.Generic;
using UnityEngine;

namespace Elysium.Networking
{
    [Serializable]
    public sealed class ExplorationNetworkSnapshot
    {
        public string AreaId = string.Empty;
        public long PublishedAtUtc;
        public string StatusMessage = string.Empty;
        public List<ExplorationParticipantState> Participants = new List<ExplorationParticipantState>();
    }

    [Serializable]
    public sealed class ExplorationParticipantState
    {
        public string PlayerId = string.Empty;
        public string CharacterId = string.Empty;
        public string AreaId = string.Empty;
        public Vector3 Position = Vector3.zero;
        public float FacingYaw;
        public long UpdatedAtUtc;
    }
}