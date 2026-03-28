using System.Collections.Generic;

namespace Elysium.Packaging
{
    public sealed class EwmExportResult
    {
        public bool Success;
        public string OutputPackagePath = string.Empty;
        public string Error = string.Empty;
        public List<string> Warnings = new();
    }
}