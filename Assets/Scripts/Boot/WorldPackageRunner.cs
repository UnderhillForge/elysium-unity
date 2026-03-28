using Elysium.Packaging;
using UnityEngine;

namespace Elysium.Boot
{
    public sealed class WorldPackageRunner : MonoBehaviour
    {
        [SerializeField] private string projectFolderName = "starter_forest_edge";
        [SerializeField] private EwmPackageMode packageMode = EwmPackageMode.Template;
        [SerializeField] private string outputDirectory = "EwmExports";
        [SerializeField] private string gameVersion = "0.1.0";
        [SerializeField] private string apiVersion = "1.0.0";
        [SerializeField] private string importPackagePath = string.Empty;
        [SerializeField] private string importTargetFolderName = string.Empty;
        [SerializeField] private bool overwriteOnImport;
        [SerializeField] private string roundTripImportSuffix = "_roundtrip";
        [SerializeField] private bool overwriteOnRoundTripImport = true;
        [SerializeField] private WorldProjectValidationRunner validationRunner;

        public string LastExportSummary { get; private set; } = "No export run yet.";
        public string LastImportSummary { get; private set; } = "No import run yet.";

        public bool HasImportRun { get; private set; }
        public bool LastIntegrityVerified { get; private set; }
        public bool LastIntegritySkipped { get; private set; }
        public string LastIntegritySummary { get; private set; } = "Integrity check not run.";
        public bool LastDependencyCompatible { get; private set; }
        public bool LastDependencySkipped { get; private set; }
        public string LastDependencySummary { get; private set; } = "Dependency check not run.";

        public bool HasRoundTripRun { get; private set; }
        public bool LastRoundTripSuccess { get; private set; }
        public string LastRoundTripSummary { get; private set; } = "Round-trip test not run.";

        [ContextMenu("Export EWM")]
        public void ExportEwm()
        {
            var outputPath = BuildOutputDirectoryPath();
            var result = EwmPackageService.TryExportFromStreamingAssets(
                projectFolderName,
                packageMode,
                outputPath,
                gameVersion,
                apiVersion);

            if (!result.Success)
            {
                LastExportSummary = $"Export failed: {result.Error}";
                Debug.LogError($"[Elysium] {LastExportSummary}");
                return;
            }

            LastExportSummary = $"Export succeeded: {result.OutputPackagePath}";
            Debug.Log($"[Elysium] {LastExportSummary}");

            for (var i = 0; i < result.Warnings.Count; i++)
            {
                Debug.LogWarning($"[Elysium][Export Warning {i + 1}] {result.Warnings[i]}");
            }
        }

        [ContextMenu("Import EWM")]
        public void ImportEwm()
        {
            HasImportRun = true;
            var result = EwmPackageService.TryImportToStreamingAssets(
                importPackagePath,
                importTargetFolderName,
                overwriteOnImport);

            ApplyImportTrustStatus(result);

            if (!result.Success)
            {
                LastImportSummary = $"Import failed: {result.Error}";
                Debug.LogError($"[Elysium] {LastImportSummary}");
                return;
            }

            LastImportSummary = $"Import succeeded: {result.ImportedProjectPath}";
            Debug.Log($"[Elysium] {LastImportSummary}");

            for (var i = 0; i < result.Warnings.Count; i++)
            {
                Debug.LogWarning($"[Elysium][Import Warning {i + 1}] {result.Warnings[i]}");
            }
        }

        [ContextMenu("Run EWM Round-Trip Smoke Test")]
        public void RunRoundTripSmokeTest()
        {
            HasRoundTripRun = true;
            LastRoundTripSuccess = false;

            var outputPath = BuildOutputDirectoryPath();
            var exportResult = EwmPackageService.TryExportFromStreamingAssets(
                projectFolderName,
                packageMode,
                outputPath,
                gameVersion,
                apiVersion);

            if (!exportResult.Success)
            {
                LastRoundTripSummary = $"Round-trip failed at export: {exportResult.Error}";
                Debug.LogError($"[Elysium] {LastRoundTripSummary}");
                return;
            }

            var importFolder = SanitizeFolderName(projectFolderName + roundTripImportSuffix);
            var importResult = EwmPackageService.TryImportToStreamingAssets(
                exportResult.OutputPackagePath,
                importFolder,
                overwriteOnRoundTripImport);

            ApplyImportTrustStatus(importResult);

            if (!importResult.Success)
            {
                LastRoundTripSummary = $"Round-trip failed at import: {importResult.Error}";
                Debug.LogError($"[Elysium] {LastRoundTripSummary}");
                return;
            }

            if (validationRunner == null)
            {
                validationRunner = GetComponent<WorldProjectValidationRunner>();
            }

            if (validationRunner == null)
            {
                LastRoundTripSummary = "Round-trip import succeeded, but no validation runner is assigned.";
                Debug.LogWarning($"[Elysium] {LastRoundTripSummary}");
                return;
            }

            var validationPassed = validationRunner.RunValidationForProject(importFolder, packageMode);
            if (!validationPassed)
            {
                LastRoundTripSummary = "Round-trip failed at validation. See validation panel details.";
                Debug.LogError($"[Elysium] {LastRoundTripSummary}");
                return;
            }

            LastRoundTripSuccess = true;
            LastRoundTripSummary = $"Round-trip passed: exported and re-imported as '{importFolder}' with successful validation.";
            Debug.Log($"[Elysium] {LastRoundTripSummary}");
        }

        private string BuildOutputDirectoryPath()
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return System.IO.Path.Combine(Application.persistentDataPath, "EwmExports");
            }

            if (System.IO.Path.IsPathRooted(outputDirectory))
            {
                return outputDirectory;
            }

            return System.IO.Path.Combine(Application.persistentDataPath, outputDirectory);
        }

        private void ApplyImportTrustStatus(EwmImportResult result)
        {
            LastIntegrityVerified = result.IntegrityVerified;
            LastIntegritySkipped = result.IntegrityCheckSkipped;
            LastIntegritySummary = result.IntegrityMessage;
            LastDependencyCompatible = result.DependencyCompatible;
            LastDependencySkipped = result.DependencyCheckSkipped;
            LastDependencySummary = result.DependencyMessage;
        }

        private static string SanitizeFolderName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "imported_world_roundtrip";
            }

            var value = raw.Trim().ToLowerInvariant().Replace(' ', '_');
            foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid.ToString(), string.Empty);
            }

            return string.IsNullOrWhiteSpace(value) ? "imported_world_roundtrip" : value;
        }
    }
}