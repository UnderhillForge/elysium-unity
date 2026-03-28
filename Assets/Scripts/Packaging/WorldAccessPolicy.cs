using System;
using Elysium.World.Authoring;

namespace Elysium.Packaging
{
    /// Enforces ownership and collaborator access rules for world project editing.
    /// Rule: the owner (ownerPlayerId) and any listed collaborator may edit a project.
    /// Projects with no ownerPlayerId set are treated as open-authoring (legacy/template)
    /// and allow any non-empty requestingPlayerId to proceed.
    public static class WorldAccessPolicy
    {
        /// Returns true if the definition has a non-empty ownerPlayerId.
        public static bool HasOwner(WorldProjectDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return !string.IsNullOrWhiteSpace(definition.OwnerPlayerId);
        }

        /// Returns true if requestingPlayerId is allowed to edit the given project.
        /// An empty requestingPlayerId is always denied.
        public static bool CanEdit(WorldProjectDefinition definition, string requestingPlayerId)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            if (string.IsNullOrWhiteSpace(requestingPlayerId))
                return false;

            // No owner set — open-authoring mode (legacy / template projects).
            if (!HasOwner(definition))
                return true;

            if (string.Equals(definition.OwnerPlayerId, requestingPlayerId, StringComparison.Ordinal))
                return true;

            var collaborators = definition.Collaborators;
            if (collaborators != null)
            {
                foreach (var collaborator in collaborators)
                {
                    if (string.Equals(collaborator, requestingPlayerId, StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }

        /// Produces a descriptive denial message suitable for logging or error propagation.
        public static string DenialReason(WorldProjectDefinition definition, string requestingPlayerId)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            if (string.IsNullOrWhiteSpace(requestingPlayerId))
                return $"Access denied to project '{definition.ProjectId}': requestingPlayerId is empty.";

            return $"Access denied to project '{definition.ProjectId}': " +
                   $"player '{requestingPlayerId}' is not the owner or a listed collaborator.";
        }
    }
}
