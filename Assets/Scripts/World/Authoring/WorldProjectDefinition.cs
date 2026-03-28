using System;
using UnityEngine;

namespace Elysium.World.Authoring
{
    [Serializable]
    public sealed class WorldProjectDefinition
    {
        [SerializeField] private string projectId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private string author = string.Empty;
        [SerializeField] private string ownerPlayerId = string.Empty;
        [SerializeField] private string[] collaborators = new string[0];
        [SerializeField] private string ruleset = "Pathfinder1e";
        [SerializeField] private string entryAreaId = string.Empty;
        [SerializeField] private string worldDatabasePath = "Databases/world.db";
        [SerializeField] private string campaignDatabasePath = "Databases/campaign.db";
        [SerializeField] private string createdUtc = string.Empty;
        [SerializeField] private string updatedUtc = string.Empty;
        [SerializeField] private string defaultPackageMode = "Template";
        [SerializeField] private string notes = string.Empty;

        public string ProjectId => projectId;
        public string DisplayName => displayName;
        public string Author => author;
        public string OwnerPlayerId => ownerPlayerId;
        public string[] Collaborators => collaborators;
        public string Ruleset => ruleset;
        public string EntryAreaId => entryAreaId;
        public string WorldDatabasePath => worldDatabasePath;
        public string CampaignDatabasePath => campaignDatabasePath;
        public string CreatedUtc => createdUtc;
        public string UpdatedUtc => updatedUtc;
        public string DefaultPackageMode => defaultPackageMode;
        public string Notes => notes;
    }
}