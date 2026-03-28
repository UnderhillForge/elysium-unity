using System;
using System.Collections.Generic;

namespace Elysium.World.Lua
{
    [Serializable]
    public sealed class LuaScriptReference
    {
        public string Id = string.Empty;
        public string RelativePath = string.Empty;
        public LuaAttachmentKind AttachmentKind = LuaAttachmentKind.World;
        public List<string> Capabilities = new();
    }
}