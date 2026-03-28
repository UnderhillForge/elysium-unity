using System;
using System.Text;
using Elysium.Characters;
using Elysium.Networking;
using UnityEngine;

namespace Elysium.Boot
{
    /// Verifies lobby character gallery loading and explicit character selection.
    public sealed class CharacterGallerySmokeTestRunner : MonoBehaviour
    {
        private const string SourceProjectFolder = "starter_forest_edge";

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        public void RunCharacterGallerySmokeTest()
        {
            try
            {
                LastSummary = RunCharacterGallerySmokeTestInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"Character gallery smoke test failed: {ex}");
            }
        }

        private string RunCharacterGallerySmokeTestInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Character Gallery Smoke Test ===");

            var gallery = new CharacterGalleryService();
            if (!gallery.TryLoadFromStreamingAssets(SourceProjectFolder, out var characters, out var error))
            {
                throw new InvalidOperationException($"Failed to load character gallery: {error}");
            }

            if (characters.Count < 2)
            {
                throw new InvalidOperationException("Character gallery should expose at least 2 selectable characters.");
            }

            var first = gallery.FindById(characters, "pc_ranger_001");
            var second = gallery.FindById(characters, "pc_cleric_001");
            if (first == null || second == null)
            {
                throw new InvalidOperationException("Expected starter gallery characters were not found.");
            }

            log.AppendLine($"Loaded character gallery: {characters.Count} entries — ok");
            log.AppendLine($"Starter entries found: {first.DisplayName}, {second.DisplayName} — ok");

            var session = new SessionService();
            Require(session.TryOpenSession("gallery_session", SourceProjectFolder, out error), error);
            Require(session.TryRegisterPlayer(new PlayerSessionRecord
            {
                PlayerId = "gm_001",
                DisplayName = "GM",
                Role = PlayerRole.GameMaster,
                NetworkClientId = 0,
            }, out error), error);
            Require(session.TryRegisterPlayer(new PlayerSessionRecord
            {
                PlayerId = "player_001",
                DisplayName = "Alice",
                Role = PlayerRole.Player,
                NetworkClientId = 1,
            }, out error), error);
            Require(session.TryRegisterPlayer(new PlayerSessionRecord
            {
                PlayerId = "player_002",
                DisplayName = "Bob",
                Role = PlayerRole.Player,
                NetworkClientId = 2,
            }, out error), error);

            Require(session.TryAssignCharacter("gm_001", "player_001", first.Id, out error), error);
            Require(session.TryAssignCharacter("gm_001", "player_002", second.Id, out error), error);

            var player1 = session.GetPlayer("player_001");
            var player2 = session.GetPlayer("player_002");
            if (player1 == null || player2 == null)
            {
                throw new InvalidOperationException("Player lookup failed after registration.");
            }

            if (!string.Equals(player1.AssignedCharacterId, first.Id, StringComparison.Ordinal)
                || !string.Equals(player2.AssignedCharacterId, second.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Assigned character IDs did not persist on player records.");
            }

            log.AppendLine($"Assigned characters: {player1.DisplayName}->{player1.AssignedCharacterId}, {player2.DisplayName}->{player2.AssignedCharacterId} — ok");

            var owner = session.GetCharacterOwner(first.Id);
            if (owner == null || !string.Equals(owner.PlayerId, "player_001", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Character ownership lookup returned the wrong player.");
            }
            log.AppendLine($"Character ownership verified: {first.Id} -> {owner.PlayerId} — ok");

            var duplicateRejected = !session.TryAssignCharacter("gm_001", "player_002", first.Id, out var duplicateErr);
            if (!duplicateRejected)
            {
                throw new InvalidOperationException("Duplicate character assignment should be rejected.");
            }
            log.AppendLine($"Duplicate assignment rejected: {duplicateErr} — ok");

            var unauthorizedRejected = !session.TryAssignCharacter("player_001", "player_002", second.Id, out var unauthorizedErr);
            if (!unauthorizedRejected)
            {
                throw new InvalidOperationException("Non-GM character assignment should be rejected.");
            }
            log.AppendLine($"Unauthorized assignment rejected: {unauthorizedErr} — ok");

            var snapshot = session.CreateSnapshot();
            var restored = new SessionService();
            restored.RestoreFromSnapshot(snapshot);
            var restoredPlayer1 = restored.GetPlayer("player_001");
            if (restoredPlayer1 == null || !string.Equals(restoredPlayer1.AssignedCharacterId, first.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("AssignedCharacterId did not survive snapshot restore.");
            }
            log.AppendLine("Snapshot restore retained character assignment — ok");

            log.AppendLine("=== Character Gallery Smoke Test COMPLETE ===");
            return log.ToString();
        }

        private static void Require(bool condition, string error)
        {
            if (!condition)
            {
                throw new InvalidOperationException(error);
            }
        }
    }
}