using System.Collections.Generic;

namespace Elysium.Packaging
{
    public sealed class WorldProjectValidationResult
    {
        public List<string> Errors { get; } = new();

        public List<string> Warnings { get; } = new();

        public bool IsValid => Errors.Count == 0;
    }
}