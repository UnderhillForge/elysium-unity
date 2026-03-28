using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Elysium.Characters;
using Elysium.Networking;
using UnityEngine;

namespace Elysium.Boot
{
    /// Verifies character creation validation, persistence into the gallery file, and session assignment.
    public sealed class CharacterCreationSmokeTestRunner : MonoBehaviour
    {
        private const string SourceProjectFolder = "starter_forest_edge";

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        public void RunCharacterCreationSmokeTest()
        {
            try
            {
                LastSummary = RunCharacterCreationSmokeTestInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"Character creation smoke test failed: {ex}");
            }
        }

        private string RunCharacterCreationSmokeTestInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Character Creation Smoke Test ===");

            var tempRoot = PrepareTempWorldCopy();
            var gallery = new CharacterGalleryService();
            var creator = new CharacterCreationService();

            Require(gallery.TryLoadFromWorldRoot(tempRoot, out var existingCharacters, out var error), error);
            var originalCount = existingCharacters.Count;
            log.AppendLine($"Initial gallery count: {originalCount} — ok");

            var request = new CharacterCreationRequest
            {
                DisplayName = "Mira Dawnshield",
                Level = 1,
                AbilityStrength = 12,
                AbilityDexterity = 13,
                AbilityConstitution = 14,
                AbilityIntelligence = 10,
                AbilityWisdom = 16,
                AbilityCharisma = 11,
                HitPointsMax = 10,
                HitPointsCurrent = 10,
                ArmorClass = 16,
                ArmorClassTouch = 11,
                ArmorClassFlatFooted = 15,
                BaseAttackBonus = 0,
                CriticalThreatRange = 20,
                CriticalMultiplier = 2,
                SaveFortitude = 4,
                SaveReflex = 1,
                SaveWill = 5,
            };

            Require(creator.TryCreateAndPersistToWorldRoot(tempRoot, request, out var createdCharacter, out error), error);
            if (createdCharacter == null)
            {
                throw new InvalidOperationException("Created character was null.");
            }

            if (!string.Equals(createdCharacter.Id, "pc_mira_dawnshield", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected generated ID '{createdCharacter.Id}'.");
            }

            log.AppendLine($"Created character '{createdCharacter.DisplayName}' with ID '{createdCharacter.Id}' — ok");

            Require(gallery.TryLoadFromWorldRoot(tempRoot, out var updatedCharacters, out error), error);
            if (updatedCharacters.Count != originalCount + 1)
            {
                throw new InvalidOperationException(
                    $"Gallery count should increase by 1, expected {originalCount + 1}, got {updatedCharacters.Count}.");
            }

            var persistedCharacter = gallery.FindById(updatedCharacters, createdCharacter.Id);
            if (persistedCharacter == null)
            {
                throw new InvalidOperationException("Persisted character was not found after reload.");
            }

            log.AppendLine($"Gallery persisted and reloaded new character '{persistedCharacter.Id}' — ok");

            var duplicateRequest = new CharacterCreationRequest
            {
                Id = createdCharacter.Id,
                DisplayName = "Copy Cat",
                Level = 1,
                AbilityStrength = 10,
                AbilityDexterity = 10,
                AbilityConstitution = 10,
                AbilityIntelligence = 10,
                AbilityWisdom = 10,
                AbilityCharisma = 10,
                HitPointsMax = 8,
                HitPointsCurrent = 8,
                ArmorClass = 10,
                ArmorClassTouch = 10,
                ArmorClassFlatFooted = 10,
            };

            if (!creator.TryCreateCharacter(duplicateRequest, updatedCharacters, out var duplicateCharacter, out error))
            {
                throw new InvalidOperationException($"Explicit duplicate ID should auto-suffix, but creation failed: {error}");
            }

            if (!string.Equals(duplicateCharacter.Id, "pc_mira_dawnshield_002", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Duplicate ID suffixing failed, got '{duplicateCharacter.Id}'.");
            }

            log.AppendLine($"Duplicate ID auto-suffixed to '{duplicateCharacter.Id}' — ok");

            var invalidRequest = new CharacterCreationRequest
            {
                DisplayName = "",
                HitPointsMax = 0,
            };

            var invalidRejected = !creator.TryCreateCharacter(invalidRequest, updatedCharacters, out _, out var invalidErr);
            if (!invalidRejected)
            {
                throw new InvalidOperationException("Invalid character request should be rejected.");
            }

            log.AppendLine($"Invalid request rejected: {invalidErr} — ok");

            var session = new SessionService();
            Require(session.TryOpenSession("character_creation_session", SourceProjectFolder, out error), error);
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

            Require(session.TryAssignCharacter("gm_001", "player_001", createdCharacter.Id, out error), error);
            var assigned = session.GetPlayer("player_001");
            if (assigned == null || !string.Equals(assigned.AssignedCharacterId, createdCharacter.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Created character could not be assigned through the session service.");
            }

            log.AppendLine($"Created character assigned to session player: {assigned.PlayerId}->{assigned.AssignedCharacterId} — ok");
            log.AppendLine("=== Character Creation Smoke Test COMPLETE ===");
            return log.ToString();
        }

        private static string PrepareTempWorldCopy()
        {
            var sourceRoot = Path.Combine(Application.streamingAssetsPath, "WorldProjects", SourceProjectFolder);
            var tempRoot = Path.Combine(Application.persistentDataPath, "CharacterCreationSmoke", Guid.NewGuid().ToString("N"));
            CopyDirectory(sourceRoot, tempRoot);
            return tempRoot;
        }

        private static void CopyDirectory(string sourceRoot, string destinationRoot)
        {
            Directory.CreateDirectory(destinationRoot);

            var directories = Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories);
            for (var i = 0; i < directories.Length; i++)
            {
                var directory = directories[i];
                var relative = Path.GetRelativePath(sourceRoot, directory);
                Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
            }

            var files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
            for (var i = 0; i < files.Length; i++)
            {
                var file = files[i];
                var relative = Path.GetRelativePath(sourceRoot, file);
                var destination = Path.Combine(destinationRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination, true);
            }
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