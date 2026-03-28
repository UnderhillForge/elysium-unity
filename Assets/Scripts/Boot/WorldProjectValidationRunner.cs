using Elysium.Packaging;
using System.Collections.Generic;
using UnityEngine;

namespace Elysium.Boot
{
    public sealed class WorldProjectValidationRunner : MonoBehaviour
    {
        [SerializeField] private string projectFolderName = "starter_forest_edge";
        [SerializeField] private EwmPackageMode packageMode = EwmPackageMode.Template;
        [SerializeField] private bool runOnStart = true;

        public bool HasRun { get; private set; }

        public bool LastIsValid { get; private set; }

        public int LastErrorCount { get; private set; }

        public int LastWarningCount { get; private set; }

        public string LastSummary { get; private set; } = "Validation has not run yet.";

        public IReadOnlyList<string> LastErrors => lastErrors;

        public IReadOnlyList<string> LastWarnings => lastWarnings;

        private readonly List<string> lastErrors = new();
        private readonly List<string> lastWarnings = new();

        private void Start()
        {
            if (!runOnStart)
            {
                return;
            }

            RunValidation();
        }

        [ContextMenu("Run World Project Validation")]
        public void RunValidation()
        {
            RunValidationInternal(projectFolderName, packageMode);
        }

        public bool RunValidationForProject(string folderName, EwmPackageMode mode)
        {
            return RunValidationInternal(folderName, mode);
        }

        private bool RunValidationInternal(string folderName, EwmPackageMode mode)
        {
            HasRun = true;
            lastErrors.Clear();
            lastWarnings.Clear();

            if (!WorldProjectLoader.TryLoadFromStreamingAssets(folderName, out var project, out var loadError))
            {
                LastIsValid = false;
                LastErrorCount = 1;
                LastWarningCount = 0;
                LastSummary = $"Load failed: {loadError}";
                lastErrors.Add(loadError);
                Debug.LogError($"[Elysium] World project load failed: {loadError}");
                return false;
            }

            var result = WorldProjectValidator.Validate(project, mode);
            LastIsValid = result.IsValid;
            LastErrorCount = result.Errors.Count;
            LastWarningCount = result.Warnings.Count;
            LastSummary = result.IsValid
                ? $"Validation passed for '{project.Definition.DisplayName}'."
                : $"Validation failed for '{project.Definition.DisplayName}'.";
            lastWarnings.AddRange(result.Warnings);
            lastErrors.AddRange(result.Errors);

            if (result.IsValid)
            {
                Debug.Log($"[Elysium] Validation passed for '{project.Definition.DisplayName}' ({project.Definition.ProjectId}). Warnings: {result.Warnings.Count}");
            }
            else
            {
                Debug.LogError($"[Elysium] Validation failed for '{project.Definition.DisplayName}' ({project.Definition.ProjectId}). Errors: {result.Errors.Count}, Warnings: {result.Warnings.Count}");
            }

            for (var i = 0; i < result.Warnings.Count; i++)
            {
                Debug.LogWarning($"[Elysium][Warning {i + 1}] {result.Warnings[i]}");
            }

            for (var i = 0; i < result.Errors.Count; i++)
            {
                Debug.LogError($"[Elysium][Error {i + 1}] {result.Errors[i]}");
            }

            return result.IsValid;
        }
    }
}