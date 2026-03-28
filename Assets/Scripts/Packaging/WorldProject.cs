using Elysium.World.Authoring;

namespace Elysium.Packaging
{
    public sealed class WorldProject
    {
        public WorldProject(string rootPath, WorldProjectDefinition definition)
        {
            RootPath = rootPath;
            Definition = definition;
        }

        public string RootPath { get; }

        public WorldProjectDefinition Definition { get; }
    }
}