using System;
using System.Collections.Generic;
using System.Text;

namespace Elysium.Characters
{
    /// Validates and persists newly created player characters into the world gallery.
    public sealed class CharacterCreationService
    {
        private readonly CharacterGalleryService galleryService = new CharacterGalleryService();

        public bool TryCreateAndPersistToWorldRoot(
            string worldRootPath,
            CharacterCreationRequest request,
            out CharacterRecord character,
            out string error)
        {
            character = null;

            if (!galleryService.TryLoadFromWorldRoot(worldRootPath, out var existingCharacters, out error))
            {
                return false;
            }

            if (!TryCreateCharacter(request, existingCharacters, out character, out error))
            {
                return false;
            }

            var updatedCharacters = new List<CharacterRecord>(existingCharacters.Count + 1);
            for (var i = 0; i < existingCharacters.Count; i++)
            {
                updatedCharacters.Add(existingCharacters[i]);
            }

            updatedCharacters.Add(character);

            return galleryService.TrySaveToWorldRoot(worldRootPath, updatedCharacters, out error);
        }

        public bool TryCreateCharacter(
            CharacterCreationRequest request,
            IReadOnlyList<CharacterRecord> existingCharacters,
            out CharacterRecord character,
            out string error)
        {
            character = null;
            error = string.Empty;

            if (request == null)
            {
                error = "Character creation request is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                error = "DisplayName is required.";
                return false;
            }

            if (!string.Equals(request.Ruleset, Rules.RulesetId.Pathfinder1e, StringComparison.Ordinal))
            {
                error = $"Unsupported ruleset '{request.Ruleset}'.";
                return false;
            }

            if (request.Level < 1 || request.Level > 20)
            {
                error = "Level must be between 1 and 20.";
                return false;
            }

            if (!IsAbilityScoreValid(request.AbilityStrength)
                || !IsAbilityScoreValid(request.AbilityDexterity)
                || !IsAbilityScoreValid(request.AbilityConstitution)
                || !IsAbilityScoreValid(request.AbilityIntelligence)
                || !IsAbilityScoreValid(request.AbilityWisdom)
                || !IsAbilityScoreValid(request.AbilityCharisma))
            {
                error = "Ability scores must be between 3 and 18.";
                return false;
            }

            if (request.HitPointsMax <= 0)
            {
                error = "HitPointsMax must be greater than zero.";
                return false;
            }

            if (request.HitPointsCurrent < 0 || request.HitPointsCurrent > request.HitPointsMax)
            {
                error = "HitPointsCurrent must be between 0 and HitPointsMax.";
                return false;
            }

            if (request.ArmorClass <= 0 || request.ArmorClassTouch <= 0 || request.ArmorClassFlatFooted <= 0)
            {
                error = "Armor class values must be greater than zero.";
                return false;
            }

            if (request.CriticalThreatRange < 2 || request.CriticalThreatRange > 20)
            {
                error = "CriticalThreatRange must be between 2 and 20.";
                return false;
            }

            if (request.CriticalMultiplier < 2)
            {
                error = "CriticalMultiplier must be at least 2.";
                return false;
            }

            var characterId = BuildUniqueCharacterId(request, existingCharacters);
            if (string.IsNullOrWhiteSpace(characterId))
            {
                error = "Character ID could not be generated.";
                return false;
            }

            if (ContainsCharacterId(existingCharacters, characterId))
            {
                error = $"Character '{characterId}' already exists.";
                return false;
            }

            character = new CharacterRecord
            {
                Id = characterId,
                DisplayName = request.DisplayName.Trim(),
                Ruleset = request.Ruleset,
                Level = request.Level,
                AbilityStrength = request.AbilityStrength,
                AbilityDexterity = request.AbilityDexterity,
                AbilityConstitution = request.AbilityConstitution,
                AbilityIntelligence = request.AbilityIntelligence,
                AbilityWisdom = request.AbilityWisdom,
                AbilityCharisma = request.AbilityCharisma,
                HitPointsMax = request.HitPointsMax,
                HitPointsCurrent = request.HitPointsCurrent,
                ArmorClass = request.ArmorClass,
                ArmorClassTouch = request.ArmorClassTouch,
                ArmorClassFlatFooted = request.ArmorClassFlatFooted,
                BaseAttackBonus = request.BaseAttackBonus,
                CriticalThreatRange = request.CriticalThreatRange,
                CriticalMultiplier = request.CriticalMultiplier,
                SaveFortitude = request.SaveFortitude,
                SaveReflex = request.SaveReflex,
                SaveWill = request.SaveWill,
                ExperiencePoints = request.ExperiencePoints,
            };

            return true;
        }

        private static bool IsAbilityScoreValid(int score)
        {
            return score >= 3 && score <= 18;
        }

        private static bool ContainsCharacterId(IReadOnlyList<CharacterRecord> existingCharacters, string characterId)
        {
            if (existingCharacters == null)
            {
                return false;
            }

            for (var i = 0; i < existingCharacters.Count; i++)
            {
                var character = existingCharacters[i];
                if (character != null && string.Equals(character.Id, characterId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildUniqueCharacterId(CharacterCreationRequest request, IReadOnlyList<CharacterRecord> existingCharacters)
        {
            var baseId = string.IsNullOrWhiteSpace(request.Id)
                ? BuildSlugFromDisplayName(request.DisplayName)
                : request.Id.Trim();

            if (string.IsNullOrWhiteSpace(baseId))
            {
                return string.Empty;
            }

            if (!ContainsCharacterId(existingCharacters, baseId))
            {
                return baseId;
            }

            for (var suffix = 2; suffix < 1000; suffix++)
            {
                var candidate = $"{baseId}_{suffix:000}";
                if (!ContainsCharacterId(existingCharacters, candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static string BuildSlugFromDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return string.Empty;
            }

            var slug = new StringBuilder("pc_");
            var lastWasSeparator = false;
            foreach (var ch in displayName.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    slug.Append(ch);
                    lastWasSeparator = false;
                }
                else if (!lastWasSeparator)
                {
                    slug.Append('_');
                    lastWasSeparator = true;
                }
            }

            while (slug.Length > 0 && slug[slug.Length - 1] == '_')
            {
                slug.Length -= 1;
            }

            return slug.ToString();
        }
    }
}