using System;
using System.Collections.Generic;

namespace Elysium.Packaging
{
    [Serializable]
    public sealed class EwmManifest
    {
        public int FormatVersion = 1;
        public string PackageId = string.Empty;
        public string DisplayName = string.Empty;
        public string Author = string.Empty;
        public EwmPackageMode PackageMode = EwmPackageMode.Template;
        public string GameVersion = string.Empty;
        public string ApiVersion = string.Empty;
        public string Ruleset = "Pathfinder1e";
        public string EntryAreaId = string.Empty;
        public string CreatedUtc = string.Empty;
        public List<string> Dependencies = new();
        public List<DependencyRequirement> DependencyRequirements = new();
        public LuaManifest Lua = new();
        public AssetManifest Assets = new();
    }

    [Serializable]
    public sealed class DependencyRequirement
    {
        public string Id = string.Empty;
        public string MinVersion = string.Empty;
    }

    [Serializable]
    public sealed class LuaManifest
    {
        public bool Enabled = true;
        public string HostApiVersion = "1.0.0";
        public List<LuaManifestEntry> Scripts = new();
    }

    [Serializable]
    public sealed class LuaManifestEntry
    {
        public string Id = string.Empty;
        public string Path = string.Empty;
        public string AttachmentKind = string.Empty;
        public List<string> Capabilities = new();
    }

    [Serializable]
    public sealed class AssetManifest
    {
        public bool Embedded = true;
        public string ManifestPath = "Assets/manifest.json";
    }
}