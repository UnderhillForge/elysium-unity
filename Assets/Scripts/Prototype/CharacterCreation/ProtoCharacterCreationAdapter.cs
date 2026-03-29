using Elysium.Characters;
using Elysium.Rules;

namespace Elysium.Prototype.CharacterCreation
{
    /// Converts a prototype UI selection (ProtoCharacterAppearance) into a
    /// fully-populated CharacterCreationRequest that satisfies Elysium's
    /// CharacterCreationService validation rules.
    ///
    /// Authority boundary: the adapter always forces Ruleset = Pathfinder1e and
    /// fills stat values from the registered class preset. The prototype UI
    /// never owns raw PF1e numbers; it only owns the class-key selection.
    public static class ProtoCharacterCreationAdapter
    {
        /// Attempt to convert the supplied appearance into a valid
        /// CharacterCreationRequest. Returns false with a human-readable
        /// <paramref name="error"/> if validation fails.
        public static bool TryBuildRequest(
            ProtoCharacterAppearance appearance,
            out CharacterCreationRequest request,
            out string error)
        {
            request = null;
            error   = string.Empty;

            if (appearance == null)
            {
                error = "Appearance is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(appearance.DisplayName))
            {
                error = "DisplayName is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(appearance.ClassKey))
            {
                error = "ClassKey is required.";
                return false;
            }

            if (!ProtoClassPresetRegistry.TryGet(appearance.ClassKey, out var preset))
            {
                error = $"Unknown ClassKey '{appearance.ClassKey}'. " +
                        $"Valid keys: {string.Join(", ", ProtoClassPresetRegistry.All.Keys)}";
                return false;
            }

            request = new CharacterCreationRequest
            {
                DisplayName           = appearance.DisplayName.Trim(),
                Ruleset               = RulesetId.Pathfinder1e,
                Level                 = 1,
                AbilityStrength       = preset.AbilityStrength,
                AbilityDexterity      = preset.AbilityDexterity,
                AbilityConstitution   = preset.AbilityConstitution,
                AbilityIntelligence   = preset.AbilityIntelligence,
                AbilityWisdom         = preset.AbilityWisdom,
                AbilityCharisma       = preset.AbilityCharisma,
                HitPointsMax          = preset.HitPointsMax,
                HitPointsCurrent      = preset.HitPointsMax,
                ArmorClass            = preset.ArmorClass,
                ArmorClassTouch       = preset.ArmorClassTouch,
                ArmorClassFlatFooted  = preset.ArmorClassFlatFooted,
                BaseAttackBonus       = preset.BaseAttackBonus,
                CriticalThreatRange   = 20,
                CriticalMultiplier    = 2,
                SaveFortitude         = preset.SaveFortitude,
                SaveReflex            = preset.SaveReflex,
                SaveWill              = preset.SaveWill,
                ExperiencePoints      = 0,
            };

            return true;
        }
    }
}
