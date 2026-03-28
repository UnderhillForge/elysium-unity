using System;
using System.Collections.Generic;
using System.Linq;

namespace Elysium.Networking
{
    /// Transport-agnostic session state and player registry.
    /// Owned entirely by the host. Clients receive snapshots via the NGO manager.
    public sealed class SessionService
    {
        private readonly Dictionary<string, PlayerSessionRecord> players =
            new Dictionary<string, PlayerSessionRecord>(StringComparer.Ordinal);

        public string SessionId { get; private set; } = string.Empty;
        public string WorldProjectId { get; private set; } = string.Empty;
        public SessionState State { get; private set; } = SessionState.Idle;
        public string GMPlayerId { get; private set; } = string.Empty;

        public IReadOnlyCollection<PlayerSessionRecord> Players => players.Values;
        public bool HasGM => !string.IsNullOrEmpty(GMPlayerId) && players.ContainsKey(GMPlayerId);
        public bool IsOpen => State == SessionState.Lobby || State == SessionState.InCombat;

        public event Action<PlayerSessionRecord> PlayerJoined;
        public event Action<PlayerSessionRecord> PlayerLeft;
        public event Action<PlayerSessionRecord> PlayerRoleChanged;
        public event Action<SessionState> StateChanged;

        /// Open a new session. Only valid while Idle.
        public bool TryOpenSession(
            string sessionId,
            string worldProjectId,
            out string error)
        {
            if (State != SessionState.Idle)
            {
                error = $"Cannot open session: current state is {State}.";
                return false;
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                error = "Session ID cannot be empty.";
                return false;
            }

            SessionId = sessionId;
            WorldProjectId = worldProjectId ?? string.Empty;
            ChangeState(SessionState.Lobby);
            error = string.Empty;
            return true;
        }

        /// Register a player joining the session.
        public bool TryRegisterPlayer(
            PlayerSessionRecord record,
            out string error)
        {
            if (!IsOpen)
            {
                error = $"Session is not accepting players (state={State}).";
                return false;
            }

            if (record == null || string.IsNullOrEmpty(record.PlayerId))
            {
                error = "Invalid player record.";
                return false;
            }

            if (players.ContainsKey(record.PlayerId))
            {
                error = $"Player '{record.PlayerId}' is already registered.";
                return false;
            }

            if (record.IsGM && HasGM)
            {
                error = "Session already has a GM. Reassign role before registering another GM.";
                return false;
            }

            record.JoinedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            record.IsConnected = true;
            players[record.PlayerId] = record;

            if (record.IsGM && string.IsNullOrEmpty(GMPlayerId))
            {
                GMPlayerId = record.PlayerId;
            }

            PlayerJoined?.Invoke(record);
            error = string.Empty;
            return true;
        }

        /// Mark a player as disconnected. They can reconnect with the same PlayerId.
        public bool TryDisconnectPlayer(string playerId, out string error)
        {
            if (!players.TryGetValue(playerId, out var record))
            {
                error = $"Player '{playerId}' is not in this session.";
                return false;
            }

            record.IsConnected = false;
            PlayerLeft?.Invoke(record);
            error = string.Empty;
            return true;
        }

        /// Reassign a player's role (host/GM authority only).
        public bool TrySetRole(
            string requesterId,
            string targetPlayerId,
            PlayerRole newRole,
            out string error)
        {
            if (!IsGMOrHost(requesterId))
            {
                error = $"Requester '{requesterId}' is not authorised to change roles.";
                return false;
            }

            if (!players.TryGetValue(targetPlayerId, out var record))
            {
                error = $"Player '{targetPlayerId}' not found.";
                return false;
            }

            if (newRole == PlayerRole.GameMaster
                && HasGM
                && !string.Equals(GMPlayerId, targetPlayerId, StringComparison.Ordinal))
            {
                if (players.TryGetValue(GMPlayerId, out var currentGm))
                {
                    currentGm.Role = PlayerRole.Player;
                    PlayerRoleChanged?.Invoke(currentGm);
                }
            }

            record.Role = newRole;

            if (newRole == PlayerRole.GameMaster)
            {
                GMPlayerId = targetPlayerId;
            }
            else if (string.Equals(GMPlayerId, targetPlayerId, StringComparison.Ordinal))
            {
                GMPlayerId = string.Empty;
            }

            PlayerRoleChanged?.Invoke(record);
            error = string.Empty;
            return true;
        }

        /// Assign a combatant to a player (GM authority only).
        public bool TryAssignCombatant(
            string requesterId,
            string targetPlayerId,
            string combatantId,
            out string error)
        {
            if (!IsGMOrHost(requesterId))
            {
                error = $"Requester '{requesterId}' is not authorised to assign combatants.";
                return false;
            }

            if (!players.TryGetValue(targetPlayerId, out var record))
            {
                error = $"Player '{targetPlayerId}' not found.";
                return false;
            }

            if (!string.IsNullOrEmpty(combatantId))
            {
                var owner = GetCombatantOwner(combatantId);
                if (owner != null && !string.Equals(owner.PlayerId, targetPlayerId, StringComparison.Ordinal))
                {
                    error = $"Combatant '{combatantId}' is already assigned to '{owner.PlayerId}'.";
                    return false;
                }
            }

            record.AssignedCombatantId = combatantId ?? string.Empty;
            error = string.Empty;
            return true;
        }

        /// Assign a lobby character selection to a player (GM authority only).
        public bool TryAssignCharacter(
            string requesterId,
            string targetPlayerId,
            string characterId,
            out string error)
        {
            if (!IsGMOrHost(requesterId))
            {
                error = $"Requester '{requesterId}' is not authorised to assign characters.";
                return false;
            }

            if (!players.TryGetValue(targetPlayerId, out var record))
            {
                error = $"Player '{targetPlayerId}' not found.";
                return false;
            }

            if (!string.IsNullOrEmpty(characterId))
            {
                var owner = GetCharacterOwner(characterId);
                if (owner != null && !string.Equals(owner.PlayerId, targetPlayerId, StringComparison.Ordinal))
                {
                    error = $"Character '{characterId}' is already assigned to '{owner.PlayerId}'.";
                    return false;
                }
            }

            record.AssignedCharacterId = characterId ?? string.Empty;
            error = string.Empty;
            return true;
        }

        /// Resolve the combatant owner for a given combatant ID.
        /// Returns null if no player is assigned to that combatant.
        public PlayerSessionRecord GetCombatantOwner(string combatantId)
        {
            if (string.IsNullOrEmpty(combatantId))
            {
                return null;
            }

            foreach (var player in players.Values)
            {
                if (string.Equals(player.AssignedCombatantId, combatantId, StringComparison.Ordinal))
                {
                    return player;
                }
            }

            return null;
        }

        /// Resolve the character owner for a given selected character ID.
        /// Returns null if no player is assigned to that character.
        public PlayerSessionRecord GetCharacterOwner(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                return null;
            }

            foreach (var player in players.Values)
            {
                if (string.Equals(player.AssignedCharacterId, characterId, StringComparison.Ordinal))
                {
                    return player;
                }
            }

            return null;
        }

        /// Look up a player by their network client ID.
        public PlayerSessionRecord GetPlayerByClientId(ulong networkClientId)
        {
            foreach (var player in players.Values)
            {
                if (player.NetworkClientId == networkClientId)
                {
                    return player;
                }
            }

            return null;
        }

        /// Look up a player by their application-scoped ID.
        public PlayerSessionRecord GetPlayer(string playerId)
        {
            players.TryGetValue(playerId, out var record);
            return record;
        }

        /// Transition to InCombat state.
        public bool TryStartCombat(string requesterId, out string error)
        {
            if (!IsGMOrHost(requesterId))
            {
                error = $"Only the GM can start combat.";
                return false;
            }

            if (State != SessionState.Lobby)
            {
                error = $"Combat can only start from Lobby state (current: {State}).";
                return false;
            }

            ChangeState(SessionState.InCombat);
            error = string.Empty;
            return true;
        }

        /// Return to lobby after an encounter ends.
        public void EndCombat()
        {
            if (State == SessionState.InCombat)
            {
                ChangeState(SessionState.Lobby);
            }
        }

        /// Close the session entirely.
        public void CloseSession()
        {
            players.Clear();
            GMPlayerId = string.Empty;
            SessionId = string.Empty;
            WorldProjectId = string.Empty;
            ChangeState(SessionState.Idle);
        }

        /// Create a persistence snapshot suitable for SQLite storage.
        public SessionPersistenceSnapshot CreateSnapshot()
        {
            var snapshot = new SessionPersistenceSnapshot
            {
                SessionId = SessionId,
                WorldProjectId = WorldProjectId,
                State = State,
                GMPlayerId = GMPlayerId,
            };

            foreach (var player in players.Values)
            {
                snapshot.Players.Add(player);
            }

            return snapshot;
        }

        /// Restore the service from a previously saved snapshot.
        public void RestoreFromSnapshot(SessionPersistenceSnapshot snapshot)
        {
            players.Clear();
            SessionId = snapshot?.SessionId ?? string.Empty;
            WorldProjectId = snapshot?.WorldProjectId ?? string.Empty;
            State = snapshot?.State ?? SessionState.Idle;
            GMPlayerId = string.Empty;

            if (snapshot?.Players == null)
            {
                return;
            }

            var assignedCombatants = new HashSet<string>(StringComparer.Ordinal);
            var assignedCharacters = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < snapshot.Players.Count; i++)
            {
                var player = snapshot.Players[i];
                if (player != null && !string.IsNullOrEmpty(player.PlayerId))
                {
                    if (!string.IsNullOrEmpty(player.AssignedCombatantId))
                    {
                        if (assignedCombatants.Contains(player.AssignedCombatantId))
                        {
                            player.AssignedCombatantId = string.Empty;
                        }
                        else
                        {
                            assignedCombatants.Add(player.AssignedCombatantId);
                        }
                    }

                    if (!string.IsNullOrEmpty(player.AssignedCharacterId))
                    {
                        if (assignedCharacters.Contains(player.AssignedCharacterId))
                        {
                            player.AssignedCharacterId = string.Empty;
                        }
                        else
                        {
                            assignedCharacters.Add(player.AssignedCharacterId);
                        }
                    }

                    if (player.IsGM)
                    {
                        if (string.IsNullOrEmpty(GMPlayerId))
                        {
                            GMPlayerId = player.PlayerId;
                        }
                        else
                        {
                            player.Role = PlayerRole.Player;
                        }
                    }

                    players[player.PlayerId] = player;
                }
            }

            if (!string.IsNullOrEmpty(snapshot.GMPlayerId)
                && players.TryGetValue(snapshot.GMPlayerId, out var snapshotGm))
            {
                if (!snapshotGm.IsGM)
                {
                    snapshotGm.Role = PlayerRole.GameMaster;
                }

                if (!string.IsNullOrEmpty(GMPlayerId)
                    && !string.Equals(GMPlayerId, snapshot.GMPlayerId, StringComparison.Ordinal)
                    && players.TryGetValue(GMPlayerId, out var previousGm))
                {
                    previousGm.Role = PlayerRole.Player;
                }

                GMPlayerId = snapshot.GMPlayerId;
            }
        }

        private bool IsGMOrHost(string requesterId)
        {
            return string.Equals(requesterId, GMPlayerId, StringComparison.Ordinal)
                || (players.TryGetValue(requesterId, out var r) && r.IsGM);
        }

        private void ChangeState(SessionState newState)
        {
            State = newState;
            StateChanged?.Invoke(newState);
        }
    }

    public enum SessionState
    {
        /// No active session.
        Idle = 0,

        /// Session is open; players can join before combat begins.
        Lobby = 1,

        /// An encounter is active.
        InCombat = 2,
    }

    [Serializable]
    public sealed class SessionPersistenceSnapshot
    {
        public string SessionId = string.Empty;
        public string WorldProjectId = string.Empty;
        public SessionState State = SessionState.Idle;
        public string GMPlayerId = string.Empty;
        public List<PlayerSessionRecord> Players = new List<PlayerSessionRecord>();
    }
}
