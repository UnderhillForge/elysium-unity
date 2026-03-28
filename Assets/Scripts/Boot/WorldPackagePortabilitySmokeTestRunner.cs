using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Elysium.Combat;
using Elysium.Networking;
using Elysium.Packaging;
using Elysium.Persistence;
using Elysium.Shared;
using UnityEngine;

namespace Elysium.Boot
{
    /// Verifies world package export/import portability and replay of core runtime flow.
    public sealed class WorldPackagePortabilitySmokeTestRunner : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = false;
        [SerializeField] private string sourceProjectFolder = "starter_forest_edge";
        [SerializeField] private EwmPackageMode packageMode = EwmPackageMode.CampaignSnapshot;
        [SerializeField] private string apiVersion = "1.0.0";
        [SerializeField] private string importSuffix = "_portability_smoke";

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        private void Start()
        {
            if (runOnStart)
            {
                RunPortabilitySmokeTest();
            }
        }

        public void RunPortabilitySmokeTest()
        {
            try
            {
                LastSummary = RunPortabilitySmokeTestInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"World package portability smoke test failed: {ex}");
            }
        }

        private string RunPortabilitySmokeTestInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("=== World Package Portability Smoke Test ===");

            var outputDirectory = Path.Combine(Application.persistentDataPath, "EwmExportsSmoke");
            var importFolder = SanitizeFolderName(sourceProjectFolder + importSuffix);
            var importPath = Path.Combine(Application.streamingAssetsPath, "WorldProjects", importFolder);

            if (Directory.Exists(importPath))
            {
                Directory.Delete(importPath, true);
            }

            try
            {
                var exportResult = EwmPackageService.TryExportFromStreamingAssets(
                    sourceProjectFolder,
                    packageMode,
                    outputDirectory,
                    Application.version,
                    apiVersion);
                Require(exportResult.Success, exportResult.Error);
                Require(File.Exists(exportResult.OutputPackagePath), "Export did not produce an .ewm file.");
                log.AppendLine($"[1] Export succeeded: {exportResult.OutputPackagePath}");

                var manifest = ReadAndValidateManifest(exportResult.OutputPackagePath);
                log.AppendLine("[2] Package archive contains valid manifest and integrity file.");
                log.AppendLine($"    packageId={manifest.PackageId}");
                log.AppendLine($"    mode={manifest.PackageMode}");

                var importResult = EwmPackageService.TryImportToStreamingAssets(
                    exportResult.OutputPackagePath,
                    importFolder,
                    overwrite: true);
                Require(importResult.Success, importResult.Error);
                Require(!string.IsNullOrWhiteSpace(importResult.IntegrityMessage), "Import did not report integrity status.");
                Require(!string.IsNullOrWhiteSpace(importResult.DependencyMessage), "Import did not report dependency status.");
                log.AppendLine("[3] Import succeeded with trust checks.");
                log.AppendLine($"    Integrity: {importResult.IntegrityMessage}");
                log.AppendLine($"    Dependencies: {importResult.DependencyMessage}");

                Require(WorldProjectLoader.TryLoadFromStreamingAssets(importFolder, out var importedProject, out var loadError), loadError);
                var validation = WorldProjectValidator.Validate(importedProject, packageMode);
                if (!validation.IsValid)
                {
                    throw new InvalidOperationException(
                        $"Imported project validation failed: {string.Join(" | ", validation.Errors)}");
                }
                log.AppendLine("[4] Imported world validates as a playable project.");

                RunImportedProjectCoreReplay(importedProject, log);
                log.AppendLine("=== Smoke Test Complete ===");
                return log.ToString();
            }
            finally
            {
                if (Directory.Exists(importPath))
                {
                    Directory.Delete(importPath, true);
                }
            }
        }

        private static EwmManifest ReadAndValidateManifest(string packagePath)
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null)
            {
                throw new InvalidOperationException("Exported package is missing manifest.json.");
            }

            if (archive.GetEntry("ewm-integrity.json") == null)
            {
                throw new InvalidOperationException("Exported package is missing ewm-integrity.json.");
            }

            using var stream = manifestEntry.Open();
            using var reader = new StreamReader(stream);
            var manifestJson = reader.ReadToEnd();
            var manifest = JsonUtility.FromJson<EwmManifest>(manifestJson);
            if (manifest == null)
            {
                throw new InvalidOperationException("manifest.json could not be parsed.");
            }

            if (manifest.FormatVersion != WorldConstants.CurrentPackageFormatVersion)
            {
                throw new InvalidOperationException(
                    $"Unexpected package format version: {manifest.FormatVersion}.");
            }

            if (string.IsNullOrWhiteSpace(manifest.PackageId))
            {
                throw new InvalidOperationException("manifest.json is missing packageId.");
            }

            if (string.IsNullOrWhiteSpace(manifest.EntryAreaId))
            {
                throw new InvalidOperationException("manifest.json is missing entryAreaId.");
            }

            if (manifest.DependencyRequirements == null)
            {
                throw new InvalidOperationException("manifest.json is missing dependency requirements list.");
            }

            if (manifest.Lua == null)
            {
                throw new InvalidOperationException("manifest.json is missing lua section.");
            }

            return manifest;
        }

        private static void RunImportedProjectCoreReplay(WorldProject importedProject, StringBuilder log)
        {
            var session = new SessionService();
            Require(session.TryOpenSession("pkg_replay_session_001", importedProject.Definition.ProjectId, out var error), error);
            Require(session.TryRegisterPlayer(new PlayerSessionRecord
            {
                PlayerId = "gm_001",
                DisplayName = "GM",
                NetworkClientId = 0,
                Role = PlayerRole.GameMaster,
            }, out error), error);
            Require(session.TryRegisterPlayer(new PlayerSessionRecord
            {
                PlayerId = "player_001",
                DisplayName = "Player",
                NetworkClientId = 1,
                Role = PlayerRole.Player,
            }, out error), error);
            Require(session.TryAssignCombatant("gm_001", "player_001", "combatant_player", out error), error);
            Require(session.TryStartCombat("gm_001", out error), error);

            var combat = CombatStateService.CreateForEncounter("pkg_replay_encounter", CombatMode.GMGuided, "gm_001");
            combat.InitializeCombat(new System.Collections.Generic.List<Combatant>
            {
                new Combatant
                {
                    CombatantId = "combatant_player",
                    ActorName = "Imported Hero",
                    CharacterId = "player_001",
                    InitiativeRoll = 20,
                    HitPointsCurrent = 30,
                    HitPointsMax = 30,
                    ArmorClass = 17,
                },
                new Combatant
                {
                    CombatantId = "enemy_001",
                    ActorName = "Imported Enemy",
                    CharacterId = "npc_001",
                    InitiativeRoll = 12,
                    HitPointsCurrent = 20,
                    HitPointsMax = 20,
                    ArmorClass = 12,
                }
            });

            var action = combat.AttemptAction(new TurnActionRequest
            {
                CombatantId = "combatant_player",
                ActionName = "Imported Slash",
                Description = "Portability replay action",
                ActionType = TurnActionType.StandardAction,
                TargetCombatantId = "enemy_001",
            }, out error);
            Require(action != null, error);

            var campaignDbPath = Path.Combine(importedProject.RootPath, importedProject.Definition.CampaignDatabasePath);
            var persistence = new CampaignPersistenceService(campaignDbPath);
            Require(persistence.TrySaveCampaignState(
                session,
                "pkg_replay_instance_001",
                combat.EncounterId,
                importedProject.Definition.EntryAreaId,
                combat,
                out error), error);
            Require(persistence.TryLoadCampaignState("pkg_replay_instance_001", out var loadedSession, out var loadedCombat, out error), error);

            if (loadedSession.State != SessionState.InCombat)
            {
                throw new InvalidOperationException($"Imported replay restored wrong session state: {loadedSession.State}.");
            }

            if (loadedSession.GetCombatantOwner("combatant_player")?.PlayerId != "player_001")
            {
                throw new InvalidOperationException("Imported replay lost combatant ownership mapping.");
            }

            if (loadedCombat == null || loadedCombat.PendingActions.Count != 1)
            {
                throw new InvalidOperationException("Imported replay did not restore the pending action list.");
            }

            if (!string.Equals(loadedCombat.PendingActions[0].ActionName, "Imported Slash", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Imported replay pending action content changed after restore.");
            }

            log.AppendLine("[5] Session -> combat -> save/load replay passed on imported package.");
        }

        private static string SanitizeFolderName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "imported_world_portability";
            }

            var value = raw.Trim().ToLowerInvariant().Replace(' ', '_');
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid.ToString(), string.Empty);
            }

            return string.IsNullOrWhiteSpace(value) ? "imported_world_portability" : value;
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