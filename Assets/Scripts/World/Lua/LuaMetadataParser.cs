using System;
using System.Collections.Generic;
using System.IO;

namespace Elysium.World.Lua
{
    public static class LuaMetadataParser
    {
        public static LuaScriptReference ParseScriptReference(string projectRootPath, string scriptRelativePath)
        {
            var result = new LuaScriptReference
            {
                RelativePath = scriptRelativePath,
                AttachmentKind = InferAttachmentKind(scriptRelativePath),
                Id = Path.GetFileNameWithoutExtension(scriptRelativePath).Replace('\\', '.').Replace('/', '.'),
            };

            var scriptPath = Path.Combine(projectRootPath, scriptRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(scriptPath))
            {
                return result;
            }

            var lines = File.ReadAllLines(scriptPath);
            var maxHeader = Math.Min(lines.Length, 32);

            for (var i = 0; i < maxHeader; i++)
            {
                var line = lines[i].Trim();
                if (!line.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                ParseHeader(line, result);
            }

            return result;
        }

        private static void ParseHeader(string line, LuaScriptReference result)
        {
            var header = line.Substring(2).Trim();
            if (header.StartsWith("@id:", StringComparison.OrdinalIgnoreCase))
            {
                var id = header.Substring(4).Trim();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    result.Id = id;
                }

                return;
            }

            if (header.StartsWith("@attachment:", StringComparison.OrdinalIgnoreCase))
            {
                var attachment = header.Substring(12).Trim();
                result.AttachmentKind = ParseAttachmentKind(attachment);
                return;
            }

            if (header.StartsWith("@capabilities:", StringComparison.OrdinalIgnoreCase))
            {
                var raw = header.Substring(14).Trim();
                var split = raw.Split(',');
                for (var i = 0; i < split.Length; i++)
                {
                    var cap = split[i].Trim();
                    if (!string.IsNullOrWhiteSpace(cap) && !result.Capabilities.Contains(cap))
                    {
                        result.Capabilities.Add(cap);
                    }
                }
            }
        }

        private static LuaAttachmentKind ParseAttachmentKind(string raw)
        {
            var normalized = raw.Trim().ToLowerInvariant();
            return normalized switch
            {
                "asset" => LuaAttachmentKind.Asset,
                "placeable" => LuaAttachmentKind.Placeable,
                "trigger" => LuaAttachmentKind.Trigger,
                "npc" => LuaAttachmentKind.Npc,
                _ => LuaAttachmentKind.World,
            };
        }

        private static LuaAttachmentKind InferAttachmentKind(string path)
        {
            var normalized = path.Replace('\\', '/').ToLowerInvariant();
            if (normalized.StartsWith("scripts/assets/"))
            {
                return LuaAttachmentKind.Asset;
            }

            if (normalized.StartsWith("scripts/placeables/"))
            {
                return LuaAttachmentKind.Placeable;
            }

            if (normalized.StartsWith("scripts/triggers/"))
            {
                return LuaAttachmentKind.Trigger;
            }

            if (normalized.StartsWith("scripts/npcs/"))
            {
                return LuaAttachmentKind.Npc;
            }

            return LuaAttachmentKind.World;
        }
    }
}