using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Elysium.Characters
{
    /// Loads a world project's player-selectable character gallery from JSON.
    /// The initial contract uses Actors/player_characters.json so world packages can
    /// ship curated starter characters without requiring DB access.
    public sealed class CharacterGalleryService
    {
        private const string GalleryRelativePath = "Actors/player_characters.json";

        [Serializable]
        private sealed class CharacterGalleryFile
        {
            public List<CharacterRecord> characters = new List<CharacterRecord>();
        }

        public bool TryLoadFromStreamingAssets(
            string worldProjectFolder,
            out IReadOnlyList<CharacterRecord> characters,
            out string error)
        {
            var rootPath = Path.Combine(Application.streamingAssetsPath, "WorldProjects", worldProjectFolder ?? string.Empty);
            return TryLoadFromWorldRoot(rootPath, out characters, out error);
        }

        public bool TryLoadFromWorldRoot(
            string worldRootPath,
            out IReadOnlyList<CharacterRecord> characters,
            out string error)
        {
            characters = Array.Empty<CharacterRecord>();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(worldRootPath))
            {
                error = "World root path is empty.";
                return false;
            }

            var galleryPath = Path.Combine(worldRootPath, GalleryRelativePath);
            if (!File.Exists(galleryPath))
            {
                error = $"Character gallery file is missing: {galleryPath}";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(galleryPath);
            }
            catch (Exception ex)
            {
                error = $"Failed reading character gallery: {ex.Message}";
                return false;
            }

            CharacterGalleryFile file;
            try
            {
                file = JsonUtility.FromJson<CharacterGalleryFile>(json);
            }
            catch (Exception ex)
            {
                error = $"Failed parsing character gallery: {ex.Message}";
                return false;
            }

            if (file?.characters == null)
            {
                error = "Character gallery was empty or invalid.";
                return false;
            }

            var deduped = new List<CharacterRecord>(file.characters.Count);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < file.characters.Count; i++)
            {
                var character = file.characters[i];
                if (character == null || string.IsNullOrWhiteSpace(character.Id) || seenIds.Contains(character.Id))
                {
                    continue;
                }

                seenIds.Add(character.Id);
                deduped.Add(character);
            }

            characters = deduped;
            return true;
        }

        public CharacterRecord FindById(IReadOnlyList<CharacterRecord> characters, string characterId)
        {
            if (characters == null || string.IsNullOrEmpty(characterId))
            {
                return null;
            }

            for (var i = 0; i < characters.Count; i++)
            {
                var character = characters[i];
                if (character != null && string.Equals(character.Id, characterId, StringComparison.Ordinal))
                {
                    return character;
                }
            }

            return null;
        }
    }
}