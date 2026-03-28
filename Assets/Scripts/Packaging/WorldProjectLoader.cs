using System;
using System.IO;
using Elysium.World.Authoring;
using UnityEngine;

namespace Elysium.Packaging
{
    public static class WorldProjectLoader
    {
        private const string WorldProjectsRoot = "WorldProjects";

        public static bool TryLoadFromStreamingAssets(
            string projectFolderName,
            out WorldProject worldProject,
            out string error)
        {
            var projectPath = Path.Combine(Application.streamingAssetsPath, WorldProjectsRoot, projectFolderName);
            return TryLoadFromPath(projectPath, out worldProject, out error);
        }

        /// Ownership-enforced overload. Denies edit access if requestingPlayerId is not the
        /// owner or a collaborator on the loaded project.
        public static bool TryLoadFromStreamingAssets(
            string projectFolderName,
            string requestingPlayerId,
            out WorldProject worldProject,
            out string error)
        {
            var projectPath = Path.Combine(Application.streamingAssetsPath, WorldProjectsRoot, projectFolderName);
            return TryLoadFromPath(projectPath, requestingPlayerId, out worldProject, out error);
        }

        public static bool TryLoadFromPath(
            string projectRootPath,
            out WorldProject worldProject,
            out string error)
        {
            worldProject = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(projectRootPath))
            {
                error = "Project root path is empty.";
                return false;
            }

            if (!Directory.Exists(projectRootPath))
            {
                error = $"Project directory does not exist: {projectRootPath}";
                return false;
            }

            var projectFilePath = Path.Combine(projectRootPath, "project.json");
            if (!File.Exists(projectFilePath))
            {
                error = $"Missing project file: {projectFilePath}";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(projectFilePath);
            }
            catch (Exception ex)
            {
                error = $"Failed reading project.json: {ex.Message}";
                return false;
            }

            WorldProjectDefinition definition;
            try
            {
                definition = JsonUtility.FromJson<WorldProjectDefinition>(json);
            }
            catch (Exception ex)
            {
                error = $"Failed parsing project.json: {ex.Message}";
                return false;
            }

            if (definition == null)
            {
                error = "Project definition was empty or invalid.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.ProjectId))
            {
                error = "project.json is missing required field: projectId";
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.EntryAreaId))
            {
                error = "project.json is missing required field: entryAreaId";
                return false;
            }

            worldProject = new WorldProject(projectRootPath, definition);
            return true;
        }

        /// Ownership-enforced overload. Denies access if requestingPlayerId is not the
        /// owner or a collaborator on the loaded project.
        public static bool TryLoadFromPath(
            string projectRootPath,
            string requestingPlayerId,
            out WorldProject worldProject,
            out string error)
        {
            if (!TryLoadFromPath(projectRootPath, out worldProject, out error))
                return false;

            if (!WorldAccessPolicy.CanEdit(worldProject.Definition, requestingPlayerId))
            {
                error = WorldAccessPolicy.DenialReason(worldProject.Definition, requestingPlayerId);
                worldProject = null;
                return false;
            }

            return true;
        }
    }
}