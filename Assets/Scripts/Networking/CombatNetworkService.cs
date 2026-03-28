using System;
using Elysium.Combat;

namespace Elysium.Networking
{
    /// Host-authoritative combat service used by NGO and local smoke tests.
    public sealed class CombatNetworkService
    {
        private CombatStateService hostedCombatState;

        public event Action<CombatNetworkSnapshot> SnapshotPublished;

        public CombatNetworkSnapshot CurrentSnapshot { get; private set; }
        public CombatStateService HostedCombatState => hostedCombatState;

        public bool HasHostedEncounter => hostedCombatState != null;

        public void HostEncounter(CombatStateService combatState, string statusMessage = "Combat synchronized.")
        {
            hostedCombatState = combatState ?? throw new ArgumentNullException(nameof(combatState));
            PublishSnapshot(statusMessage);
        }

        public void PublishStatus(string statusMessage)
        {
            PublishSnapshot(statusMessage);
        }

        public bool TrySubmitAction(
            string requesterId,
            TurnActionRequest request,
            out ActionResolution resolution,
            out string error)
        {
            resolution = null;
            error = string.Empty;

            if (!EnsureHostedEncounter(out error))
            {
                return false;
            }

            if (!IsActionRequesterAuthorized(requesterId, request, out error))
            {
                PublishSnapshot($"Rejected action request from {requesterId}: {error}");
                return false;
            }

            resolution = hostedCombatState.AttemptAction(request, out error);
            if (resolution == null)
            {
                PublishSnapshot($"Rejected action request from {requesterId}: {error}");
                return false;
            }

            var statusMessage = resolution.RequiresGMApproval
                ? $"Pending GM approval: {request.ActionName} ({request.CombatantId})"
                : $"Resolved action: {request.ActionName} ({request.CombatantId})";

            PublishSnapshot(statusMessage);
            return true;
        }

        public bool TryResolvePendingAction(
            string requesterId,
            string actionId,
            bool approved,
            string gmResolution,
            out string error)
        {
            error = string.Empty;

            if (!EnsureHostedEncounter(out error))
            {
                return false;
            }

            if (hostedCombatState.RequiresGMApproval && !string.Equals(requesterId, hostedCombatState.GMId, StringComparison.Ordinal))
            {
                error = $"Only GM '{hostedCombatState.GMId}' can resolve pending actions.";
                PublishSnapshot($"Rejected approval from {requesterId}: {error}");
                return false;
            }

            if (!hostedCombatState.ResolveActionApproval(actionId, approved, gmResolution))
            {
                error = $"Pending action '{actionId}' was not found.";
                PublishSnapshot($"Rejected approval from {requesterId}: {error}");
                return false;
            }

            var statusMessage = approved
                ? $"GM approved action {actionId}."
                : $"GM denied action {actionId}.";

            PublishSnapshot(statusMessage);
            return true;
        }

        public bool TryEndTurn(string requesterId, out string error)
        {
            error = string.Empty;

            if (!EnsureHostedEncounter(out error))
            {
                return false;
            }

            var currentCombatant = hostedCombatState.CurrentCombatant;
            if (currentCombatant == null)
            {
                error = "No active combatant to end turn for.";
                PublishSnapshot(error);
                return false;
            }

            var isCurrentActor = string.Equals(requesterId, currentCombatant.CombatantId, StringComparison.Ordinal);
            var isGm = string.Equals(requesterId, hostedCombatState.GMId, StringComparison.Ordinal);
            if (!isCurrentActor && !isGm)
            {
                error = $"Requester '{requesterId}' cannot end turn for '{currentCombatant.CombatantId}'.";
                PublishSnapshot($"Rejected end-turn request from {requesterId}: {error}");
                return false;
            }

            hostedCombatState.EndCurrentTurn();
            PublishSnapshot($"Turn advanced by {requesterId}.");
            return true;
        }

        public bool TrySkipTurn(string requesterId, out string error)
        {
            error = string.Empty;

            if (!EnsureHostedEncounter(out error))
            {
                return false;
            }

            if (!string.Equals(requesterId, hostedCombatState.GMId, StringComparison.Ordinal))
            {
                error = $"Only GM '{hostedCombatState.GMId}' can skip turns.";
                PublishSnapshot($"Rejected skip-turn request from {requesterId}: {error}");
                return false;
            }

            hostedCombatState.SkipCurrentTurn();
            PublishSnapshot($"Turn skipped by GM {requesterId}.");
            return true;
        }

        private bool EnsureHostedEncounter(out string error)
        {
            if (hostedCombatState == null)
            {
                error = "No hosted combat encounter is active.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool IsActionRequesterAuthorized(
            string requesterId,
            TurnActionRequest request,
            out string error)
        {
            if (request == null)
            {
                error = "Action request cannot be null.";
                return false;
            }

            var isGm = string.Equals(requesterId, hostedCombatState.GMId, StringComparison.Ordinal);
            if (!isGm && !string.Equals(requesterId, request.CombatantId, StringComparison.Ordinal))
            {
                error = $"Requester '{requesterId}' cannot submit actions for '{request.CombatantId}'.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void PublishSnapshot(string statusMessage)
        {
            if (hostedCombatState == null)
            {
                return;
            }

            CurrentSnapshot = CombatNetworkSnapshot.FromCombatState(hostedCombatState, statusMessage);
            SnapshotPublished?.Invoke(CurrentSnapshot);
        }
    }
}