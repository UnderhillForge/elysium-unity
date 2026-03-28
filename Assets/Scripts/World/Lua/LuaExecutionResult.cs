namespace Elysium.World.Lua
{
    public sealed class LuaExecutionResult
    {
        public bool Success;
        public bool UsedMoonSharp;
        public string Error = string.Empty;
        public string Warning = string.Empty;
    }
}