using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Elysium.Shared;
using UnityEngine;

namespace Elysium.Packaging
{
    public static class EwmPackageService
    {
        private const string WorldProjectsRoot = "WorldProjects";
        private const string IntegrityFileName = "ewm-integrity.json";
        private const string CurrentHostApiVersion = "1.0.0";

        public static EwmExportResult TryExportFromStreamingAssets(
            string projectFolderName,
            EwmPackageMode packageMode,
            string outputDirectory,
            string gameVersion,
            string apiVersion)
        {
            var result = new EwmExportResult();

            if (!WorldProjectLoader.TryLoadFromStreamingAssets(projectFolderName, out var worldProject, out var loadError))
            {
                result.Error = loadError;
                return result;
            }

            return TryExportFromProject(worldProject, packageMode, outputDirectory, gameVersion, apiVersion);
        }

        public static EwmExportResult TryExportFromProject(
            WorldProject worldProject,
            EwmPackageMode packageMode,
            string outputDirectory,
            string gameVersion,
            string apiVersion)
        {
            var result = new EwmExportResult();

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                result.Error = "Output directory is empty.";
                return result;
            }

            var validation = WorldProjectValidator.Validate(worldProject, packageMode);
            result.Warnings.AddRange(validation.Warnings);

            if (!validation.IsValid)
            {
                result.Error = $"Export blocked by validation errors ({validation.Errors.Count}).";
                return result;
            }

            var tempRoot = CreateTempDirectory();

            try
            {
                CopyProjectForExport(worldProject, packageMode, tempRoot, result.Warnings);

                var manifest = BuildManifest(worldProject, packageMode, gameVersion, apiVersion);
                var manifestJson = JsonUtility.ToJson(manifest, true);
                File.WriteAllText(Path.Combine(tempRoot, "manifest.json"), manifestJson);
                WriteIntegrityFile(tempRoot);

                Directory.CreateDirectory(outputDirectory);
                var packageName = BuildPackageFileName(worldProject.Definition.DisplayName, packageMode);
                var packagePath = Path.Combine(outputDirectory, packageName + WorldConstants.PackageExtension);

                if (File.Exists(packagePath))
                {
                    File.Delete(packagePath);
                }

                CreateZipFromDirectory(tempRoot, packagePath);

                result.Success = true;
                result.OutputPackagePath = packagePath;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = $"Export failed: {ex.Message}";
                return result;
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        public static EwmImportResult TryImportToStreamingAssets(
            string packagePath,
            string targetFolderName,
            bool overwrite)
        {
            var result = new EwmImportResult();

            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                result.Error = "Package path does not exist.";
                return result;
            }

            if (!packagePath.EndsWith(WorldConstants.PackageExtension, StringComparison.OrdinalIgnoreCase))
            {
                result.Error = $"Package must use {WorldConstants.PackageExtension} extension.";
                return result;
            }

            var extractRoot = CreateTempDirectory();

            try
            {
                if (!ExtractZipSafely(packagePath, extractRoot, out var extractError))
                {
                    result.Error = extractError;
                    return result;
                }

                var manifestPath = Path.Combine(extractRoot, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    result.Error = "Package is missing manifest.json.";
                    return result;
                }

                var manifestJson = File.ReadAllText(manifestPath);
                var manifest = JsonUtility.FromJson<EwmManifest>(manifestJson);

                if (manifest == null)
                {
                    result.Error = "manifest.json could not be parsed.";
                    return result;
                }

                if (manifest.FormatVersion != WorldConstants.CurrentPackageFormatVersion)
                {
                    result.Error = $"Unsupported package format version: {manifest.FormatVersion}.";
                    return result;
                }

                if (!string.IsNullOrWhiteSpace(manifest.GameVersion)
                    && !string.IsNullOrWhiteSpace(Application.version)
                    && !string.Equals(manifest.GameVersion, Application.version, StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add($"Package gameVersion ({manifest.GameVersion}) differs from host ({Application.version}).");
                }

                if (!string.IsNullOrWhiteSpace(manifest.ApiVersion)
                    && !string.Equals(manifest.ApiVersion, CurrentHostApiVersion, StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add($"Package apiVersion ({manifest.ApiVersion}) differs from host API ({CurrentHostApiVersion}).");
                }

                if (!VerifyIntegrity(extractRoot, out var integrityError, out var integrityWarnings))
                {
                    result.IntegrityVerified = false;
                    result.IntegrityCheckSkipped = false;
                    result.IntegrityMessage = integrityError;
                    result.Error = integrityError;
                    return result;
                }

                result.IntegrityVerified = !ContainsSkippedMessage(integrityWarnings);
                result.IntegrityCheckSkipped = !result.IntegrityVerified;
                result.IntegrityMessage = result.IntegrityCheckSkipped
                    ? "Skipped: package has no ewm-integrity.json"
                    : "Verified: integrity hashes passed";
                result.Warnings.AddRange(integrityWarnings);

                if (!ValidateDependencyCompatibility(manifest, out var dependencyError, out var dependencyWarnings))
                {
                    result.DependencyCompatible = false;
                    result.DependencyCheckSkipped = false;
                    result.DependencyMessage = dependencyError;
                    result.Error = dependencyError;
                    return result;
                }

                result.DependencyCheckSkipped = ContainsSkippedMessage(dependencyWarnings);
                result.DependencyCompatible = !result.DependencyCheckSkipped;
                result.DependencyMessage = result.DependencyCheckSkipped
                    ? "Skipped: installed pack registry unavailable"
                    : "Passed: dependencies are compatible";
                result.Warnings.AddRange(dependencyWarnings);

                var effectiveFolderName = string.IsNullOrWhiteSpace(targetFolderName)
                    ? SanitizeName(ExtractLeafPackageId(manifest.PackageId, "imported_world"))
                    : SanitizeName(targetFolderName);

                var destinationRoot = Path.Combine(Application.streamingAssetsPath, WorldProjectsRoot, effectiveFolderName);

                if (Directory.Exists(destinationRoot))
                {
                    if (!overwrite)
                    {
                        result.Error = $"Destination already exists: {destinationRoot}";
                        return result;
                    }

                    Directory.Delete(destinationRoot, true);
                }

                CopyExtractedProject(extractRoot, destinationRoot);

                if (!WorldProjectLoader.TryLoadFromPath(destinationRoot, out var importedProject, out var loadError))
                {
                    result.Error = $"Imported package is not a valid world project: {loadError}";
                    return result;
                }

                var validation = WorldProjectValidator.Validate(importedProject, manifest.PackageMode);
                result.Warnings.AddRange(validation.Warnings);

                if (!validation.IsValid)
                {
                    result.Error = $"Imported world failed validation ({validation.Errors.Count} errors).";
                    return result;
                }

                result.Success = true;
                result.ImportedProjectPath = destinationRoot;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = $"Import failed: {ex.Message}";
                return result;
            }
            finally
            {
                TryDeleteDirectory(extractRoot);
            }
        }

        private static void CopyProjectForExport(
            WorldProject worldProject,
            EwmPackageMode packageMode,
            string destinationRoot,
            List<string> warnings)
        {
            var root = worldProject.RootPath;

            CopyFileIfPresent(root, destinationRoot, "project.json");
            CopyFileIfPresent(root, destinationRoot, "preview.png");

            CopyDirectoryIfPresent(root, destinationRoot, "Areas");
            CopyDirectoryIfPresent(root, destinationRoot, "Actors");
            CopyDirectoryIfPresent(root, destinationRoot, "Quests");
            CopyDirectoryIfPresent(root, destinationRoot, "Scripts");
            CopyDirectoryIfPresent(root, destinationRoot, "Assets");
            CopyDirectoryIfPresent(root, destinationRoot, "Dependencies");

            var worldDbRelative = worldProject.Definition.WorldDatabasePath;
            var campaignDbRelative = worldProject.Definition.CampaignDatabasePath;

            if (File.Exists(Path.Combine(root, worldDbRelative)))
            {
                CopyFileIfPresent(root, destinationRoot, worldDbRelative);
            }
            else
            {
                warnings.Add("world.db not found; export will proceed without it.");
            }

            if (packageMode == EwmPackageMode.CampaignSnapshot)
            {
                if (File.Exists(Path.Combine(root, campaignDbRelative)))
                {
                    CopyFileIfPresent(root, destinationRoot, campaignDbRelative);
                }
                else
                {
                    warnings.Add("campaign.db not found for snapshot export.");
                }
            }
        }

        private static EwmManifest BuildManifest(
            WorldProject worldProject,
            EwmPackageMode packageMode,
            string gameVersion,
            string apiVersion)
        {
            var manifest = new EwmManifest
            {
                FormatVersion = WorldConstants.CurrentPackageFormatVersion,
                PackageId = worldProject.Definition.ProjectId,
                DisplayName = worldProject.Definition.DisplayName,
                Author = worldProject.Definition.Author,
                PackageMode = packageMode,
                GameVersion = gameVersion,
                ApiVersion = apiVersion,
                Ruleset = worldProject.Definition.Ruleset,
                EntryAreaId = worldProject.Definition.EntryAreaId,
                CreatedUtc = DateTime.UtcNow.ToString("O"),
            };

            var dependenciesPath = Path.Combine(worldProject.RootPath, "Dependencies", "dependencies.json");
            if (File.Exists(dependenciesPath))
            {
                var dependenciesJson = File.ReadAllText(dependenciesPath);
                var dependencies = JsonUtility.FromJson<DependencyContainer>(dependenciesJson);
                if (dependencies?.Dependencies != null)
                {
                    for (var i = 0; i < dependencies.Dependencies.Count; i++)
                    {
                        var dep = dependencies.Dependencies[i];
                        if (!string.IsNullOrWhiteSpace(dep.Id))
                        {
                            manifest.Dependencies.Add(dep.Id);
                            manifest.DependencyRequirements.Add(new DependencyRequirement
                            {
                                Id = dep.Id,
                                MinVersion = dep.MinVersion,
                            });
                        }
                    }
                }
            }

            manifest.Lua.Scripts = BuildLuaManifestEntries(worldProject.RootPath);
            manifest.Lua.Enabled = manifest.Lua.Scripts.Count > 0;
            manifest.Lua.HostApiVersion = CurrentHostApiVersion;
            return manifest;
        }

        private static List<LuaManifestEntry> BuildLuaManifestEntries(string projectRoot)
        {
            var entries = new List<LuaManifestEntry>();
            var scriptsRoot = Path.Combine(projectRoot, "Scripts");
            if (!Directory.Exists(scriptsRoot))
            {
                return entries;
            }

            var files = Directory.GetFiles(scriptsRoot, "*.lua", SearchOption.AllDirectories);
            for (var i = 0; i < files.Length; i++)
            {
                var filePath = files[i];
                var relativeToProject = GetRelativePath(projectRoot, filePath).Replace('\\', '/');
                var relativeToScripts = GetRelativePath(scriptsRoot, filePath).Replace('\\', '/');
                var scriptId = Path.GetFileNameWithoutExtension(relativeToScripts).Replace('\\', '.').Replace('/', '.');
                var attachmentKind = InferAttachmentKind(relativeToScripts);
                var capabilities = new List<string>();

                ApplyLuaHeaderOverrides(filePath, ref scriptId, ref attachmentKind, capabilities);

                entries.Add(new LuaManifestEntry
                {
                    Id = scriptId,
                    Path = relativeToProject,
                    AttachmentKind = attachmentKind,
                    Capabilities = capabilities,
                });
            }

            return entries;
        }

        private static void ApplyLuaHeaderOverrides(
            string luaFilePath,
            ref string scriptId,
            ref string attachmentKind,
            List<string> capabilities)
        {
            var lines = File.ReadAllLines(luaFilePath);
            var maxHeaderLines = Math.Min(lines.Length, 32);

            for (var i = 0; i < maxHeaderLines; i++)
            {
                var line = lines[i];

                if (!line.TrimStart().StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                var idMatch = Regex.Match(line, "^\\s*--\\s*@id\\s*:\\s*(.+)$", RegexOptions.IgnoreCase);
                if (idMatch.Success)
                {
                    var value = idMatch.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        scriptId = value;
                    }

                    continue;
                }

                var attachmentMatch = Regex.Match(line, "^\\s*--\\s*@attachment\\s*:\\s*(.+)$", RegexOptions.IgnoreCase);
                if (attachmentMatch.Success)
                {
                    var value = attachmentMatch.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        attachmentKind = NormalizeAttachmentKind(value);
                    }

                    continue;
                }

                var capabilityMatch = Regex.Match(line, "^\\s*--\\s*@capabilities\\s*:\\s*(.+)$", RegexOptions.IgnoreCase);
                if (capabilityMatch.Success)
                {
                    var value = capabilityMatch.Groups[1].Value;
                    var items = value.Split(',');
                    for (var idx = 0; idx < items.Length; idx++)
                    {
                        var capability = items[idx].Trim();
                        if (!string.IsNullOrWhiteSpace(capability) && !capabilities.Contains(capability))
                        {
                            capabilities.Add(capability);
                        }
                    }
                }
            }
        }

        private static string NormalizeAttachmentKind(string raw)
        {
            var normalized = raw.Trim().ToLowerInvariant();
            return normalized switch
            {
                "asset" => "Asset",
                "placeable" => "Placeable",
                "trigger" => "Trigger",
                "npc" => "Npc",
                _ => "World",
            };
        }

        private static string InferAttachmentKind(string relativeToScripts)
        {
            var lower = relativeToScripts.ToLowerInvariant();
            if (lower.StartsWith("assets/"))
            {
                return "Asset";
            }

            if (lower.StartsWith("placeables/"))
            {
                return "Placeable";
            }

            if (lower.StartsWith("triggers/"))
            {
                return "Trigger";
            }

            if (lower.StartsWith("npcs/"))
            {
                return "Npc";
            }

            return "World";
        }

        private static void CopyExtractedProject(string extractRoot, string destinationRoot)
        {
            Directory.CreateDirectory(destinationRoot);

            var entries = Directory.GetFileSystemEntries(extractRoot);
            for (var i = 0; i < entries.Length; i++)
            {
                var sourcePath = entries[i];
                var name = Path.GetFileName(sourcePath);

                if (string.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, IntegrityFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var destinationPath = Path.Combine(destinationRoot, name);
                if (Directory.Exists(sourcePath))
                {
                    CopyDirectoryRecursive(sourcePath, destinationPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationRoot);
                    File.Copy(sourcePath, destinationPath, true);
                }
            }
        }

        private static string BuildPackageFileName(string displayName, EwmPackageMode packageMode)
        {
            var safeName = SanitizeName(string.IsNullOrWhiteSpace(displayName) ? "world" : displayName);
            return $"{safeName}_{packageMode.ToString().ToLowerInvariant()}";
        }

        private static string SanitizeName(string raw)
        {
            var safe = raw.Trim().ToLowerInvariant().Replace(' ', '_');
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(c.ToString(), string.Empty);
            }

            return string.IsNullOrWhiteSpace(safe) ? "world" : safe;
        }

        private static string ExtractLeafPackageId(string packageId, string fallback)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return fallback;
            }

            var parts = packageId.Split('.');
            return parts.Length == 0 ? fallback : parts[parts.Length - 1];
        }

        private static void CopyFileIfPresent(string sourceRoot, string destinationRoot, string relativePath)
        {
            var sourcePath = Path.Combine(sourceRoot, relativePath);
            if (!File.Exists(sourcePath))
            {
                return;
            }

            var destinationPath = Path.Combine(destinationRoot, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(sourcePath, destinationPath, true);
        }

        private static void CopyDirectoryIfPresent(string sourceRoot, string destinationRoot, string relativeDirectory)
        {
            var sourceDirectory = Path.Combine(sourceRoot, relativeDirectory);
            if (!Directory.Exists(sourceDirectory))
            {
                return;
            }

            var destinationDirectory = Path.Combine(destinationRoot, relativeDirectory);
            CopyDirectoryRecursive(sourceDirectory, destinationDirectory);
        }

        private static void CopyDirectoryRecursive(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            var files = Directory.GetFiles(sourceDirectory);
            for (var i = 0; i < files.Length; i++)
            {
                var sourceFile = files[i];
                var destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, destinationFile, true);
            }

            var subDirectories = Directory.GetDirectories(sourceDirectory);
            for (var i = 0; i < subDirectories.Length; i++)
            {
                var sourceSubDirectory = subDirectories[i];
                var destinationSubDirectory = Path.Combine(destinationDirectory, Path.GetFileName(sourceSubDirectory));
                CopyDirectoryRecursive(sourceSubDirectory, destinationSubDirectory);
            }
        }

        private static void CreateZipFromDirectory(string sourceDirectory, string zipPath)
        {
            using var stream = File.Create(zipPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

            var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
            for (var i = 0; i < files.Length; i++)
            {
                var file = files[i];
                var entryName = GetRelativePath(sourceDirectory, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, entryName, System.IO.Compression.CompressionLevel.Optimal);
            }
        }

        private static bool ExtractZipSafely(string zipPath, string destinationDirectory, out string error)
        {
            error = string.Empty;

            using var archive = ZipFile.OpenRead(zipPath);
            var destinationRoot = Path.GetFullPath(destinationDirectory);

            for (var i = 0; i < archive.Entries.Count; i++)
            {
                var entry = archive.Entries[i];
                var normalizedName = entry.FullName.Replace('\\', '/');

                if (string.IsNullOrWhiteSpace(normalizedName))
                {
                    continue;
                }

                var outputPath = Path.GetFullPath(Path.Combine(destinationRoot, normalizedName));
                if (!outputPath.StartsWith(destinationRoot, StringComparison.Ordinal))
                {
                    error = $"Unsafe package entry path: {entry.FullName}";
                    return false;
                }

                if (normalizedName.EndsWith("/", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(outputPath);
                    continue;
                }

                var parentDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(parentDirectory))
                {
                    Directory.CreateDirectory(parentDirectory);
                }

                entry.ExtractToFile(outputPath, true);
            }

            return true;
        }

        private static void WriteIntegrityFile(string stagingRoot)
        {
            var integrity = new IntegrityFile
            {
                Algorithm = "SHA256",
            };

            var files = Directory.GetFiles(stagingRoot, "*", SearchOption.AllDirectories);
            for (var i = 0; i < files.Length; i++)
            {
                var filePath = files[i];
                var relativePath = GetRelativePath(stagingRoot, filePath).Replace('\\', '/');
                if (string.Equals(relativePath, IntegrityFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                integrity.Entries.Add(new IntegrityEntry
                {
                    Path = relativePath,
                    Hash = ComputeSha256Hex(filePath),
                });
            }

            var integrityJson = JsonUtility.ToJson(integrity, true);
            File.WriteAllText(Path.Combine(stagingRoot, IntegrityFileName), integrityJson);
        }

        private static bool VerifyIntegrity(string extractedRoot, out string error, out List<string> warnings)
        {
            warnings = new List<string>();
            error = string.Empty;

            var integrityPath = Path.Combine(extractedRoot, IntegrityFileName);
            if (!File.Exists(integrityPath))
            {
                warnings.Add($"Package does not include {IntegrityFileName}; integrity verification skipped.");
                return true;
            }

            var json = File.ReadAllText(integrityPath);
            var integrity = JsonUtility.FromJson<IntegrityFile>(json);
            if (integrity == null || integrity.Entries == null)
            {
                error = "Invalid integrity metadata in package.";
                return false;
            }

            for (var i = 0; i < integrity.Entries.Count; i++)
            {
                var entry = integrity.Entries[i];
                if (string.IsNullOrWhiteSpace(entry.Path) || string.IsNullOrWhiteSpace(entry.Hash))
                {
                    error = "Integrity metadata contains empty file path or hash.";
                    return false;
                }

                var normalized = entry.Path.Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(extractedRoot, normalized);
                if (!File.Exists(fullPath))
                {
                    error = $"Integrity check failed. Missing file: {entry.Path}";
                    return false;
                }

                var actualHash = ComputeSha256Hex(fullPath);
                if (!string.Equals(actualHash, entry.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Integrity check failed for file: {entry.Path}";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateDependencyCompatibility(
            EwmManifest manifest,
            out string error,
            out List<string> warnings)
        {
            error = string.Empty;
            warnings = new List<string>();

            var requirements = new List<DependencyRequirement>();
            if (manifest.DependencyRequirements != null && manifest.DependencyRequirements.Count > 0)
            {
                requirements.AddRange(manifest.DependencyRequirements);
            }
            else if (manifest.Dependencies != null)
            {
                for (var i = 0; i < manifest.Dependencies.Count; i++)
                {
                    var depId = manifest.Dependencies[i];
                    if (!string.IsNullOrWhiteSpace(depId))
                    {
                        requirements.Add(new DependencyRequirement
                        {
                            Id = depId,
                            MinVersion = string.Empty,
                        });
                    }
                }
            }

            if (requirements.Count == 0)
            {
                return true;
            }

            var installedPacksPath = Path.Combine(Application.streamingAssetsPath, "Rules", "installed-packs.json");
            if (!File.Exists(installedPacksPath))
            {
                warnings.Add("installed-packs.json not found; dependency compatibility checks skipped.");
                return true;
            }

            var json = File.ReadAllText(installedPacksPath);
            var installed = JsonUtility.FromJson<InstalledPackContainer>(json);
            if (installed?.Packs == null)
            {
                warnings.Add("installed-packs.json is invalid; dependency compatibility checks skipped.");
                return true;
            }

            for (var i = 0; i < requirements.Count; i++)
            {
                var required = requirements[i];
                if (string.IsNullOrWhiteSpace(required.Id))
                {
                    continue;
                }

                InstalledPack matched = null;
                for (var j = 0; j < installed.Packs.Count; j++)
                {
                    var candidate = installed.Packs[j];
                    if (string.Equals(candidate.Id, required.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = candidate;
                        break;
                    }
                }

                if (matched == null)
                {
                    error = $"Missing required dependency: {required.Id}";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(required.MinVersion)
                    && CompareVersionStrings(matched.Version, required.MinVersion) < 0)
                {
                    error = $"Dependency {required.Id} requires >= {required.MinVersion}, installed {matched.Version}.";
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsSkippedMessage(List<string> warnings)
        {
            for (var i = 0; i < warnings.Count; i++)
            {
                if (warnings[i].IndexOf("skipped", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareVersionStrings(string left, string right)
        {
            var leftParts = ParseVersion(left);
            var rightParts = ParseVersion(right);

            for (var i = 0; i < 3; i++)
            {
                var cmp = leftParts[i].CompareTo(rightParts[i]);
                if (cmp != 0)
                {
                    return cmp;
                }
            }

            return 0;
        }

        private static int[] ParseVersion(string version)
        {
            var output = new[] { 0, 0, 0 };
            if (string.IsNullOrWhiteSpace(version))
            {
                return output;
            }

            var match = Regex.Match(version, "(\\d+)(?:\\.(\\d+))?(?:\\.(\\d+))?");
            if (!match.Success)
            {
                return output;
            }

            for (var i = 1; i <= 3; i++)
            {
                if (match.Groups.Count > i && int.TryParse(match.Groups[i].Value, out var value))
                {
                    output[i - 1] = value;
                }
            }

            return output;
        }

        private static string ComputeSha256Hex(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha.ComputeHash(stream);

            var builder = new StringBuilder(hash.Length * 2);
            for (var i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2"));
            }

            return builder.ToString();
        }

        private static string GetRelativePath(string rootPath, string fullPath)
        {
            var rootUri = new Uri(AppendDirectorySeparatorChar(rootPath));
            var pathUri = new Uri(fullPath);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString());
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path + Path.DirectorySeparatorChar;
            }

            return path;
        }

        private static string CreateTempDirectory()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "elysium_ewm_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempPath);
            return tempPath;
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
                // Cleanup failures are non-fatal.
            }
        }

        [Serializable]
        private sealed class DependencyContainer
        {
            public List<DependencyEntry> Dependencies = new();
        }

        [Serializable]
        private sealed class DependencyEntry
        {
            public string Id = string.Empty;
            public string MinVersion = string.Empty;
        }

        [Serializable]
        private sealed class InstalledPackContainer
        {
            public List<InstalledPack> Packs = new();
        }

        [Serializable]
        private sealed class InstalledPack
        {
            public string Id = string.Empty;
            public string Version = string.Empty;
        }

        [Serializable]
        private sealed class IntegrityFile
        {
            public string Algorithm = "SHA256";
            public List<IntegrityEntry> Entries = new();
        }

        [Serializable]
        private sealed class IntegrityEntry
        {
            public string Path = string.Empty;
            public string Hash = string.Empty;
        }
    }
}