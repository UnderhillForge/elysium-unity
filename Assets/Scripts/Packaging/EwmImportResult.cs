using System.Collections.Generic;

namespace Elysium.Packaging
{
    public sealed class EwmImportResult
    {
        public bool Success;
        public string ImportedProjectPath = string.Empty;
        public string Error = string.Empty;
        public List<string> Warnings = new();

        public bool IntegrityVerified;
        public bool IntegrityCheckSkipped;
        public string IntegrityMessage = "Integrity check not run.";

        public bool DependencyCompatible;
        public bool DependencyCheckSkipped;
        public string DependencyMessage = "Dependency check not run.";
    }
}