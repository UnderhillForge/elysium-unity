using System;

namespace Elysium.Networking
{
    /// Represents a player connection registered in an Elysium session.
    [Serializable]
    public sealed class PlayerSessionRecord
    {
        /// Stable application-scoped player identifier (not tied to transport connection).
        public string PlayerId = string.Empty;

        /// Display name chosen by the player.
        public string DisplayName = string.Empty;

        /// NGO client ID assigned at network connection time. 0 = local/host.
        public ulong NetworkClientId;

        /// Session role assigned by the host.
        public PlayerRole Role = PlayerRole.Player;

        /// The combatant ID this player controls in the active encounter.
        /// Empty when the player has no active combatant.
        public string AssignedCombatantId = string.Empty;

        /// UTC epoch milliseconds when the player joined the session.
        public long JoinedAtUtc;

        /// Whether the player is currently connected to the session.
        public bool IsConnected = true;

        public bool IsGM => Role == PlayerRole.GameMaster;
        public bool HasCombatant => !string.IsNullOrEmpty(AssignedCombatantId);

        public override string ToString() =>
            $"[{Role}] {DisplayName} ({PlayerId}) client={NetworkClientId} combatant={AssignedCombatantId}";
    }

    public enum PlayerRole
    {
        /// Regular player controlling one or more party members.
        Player = 0,

        /// Game Master: initiates encounters, approves actions, controls NPCs.
        GameMaster = 1,

        /// Spectator: read-only view of combat, no action submission.
        Spectator = 2,
    }
}
