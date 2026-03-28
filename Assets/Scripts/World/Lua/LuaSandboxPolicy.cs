using System;
using System.Collections.Generic;

namespace Elysium.World.Lua
{
    [Serializable]
    public sealed class LuaSandboxPolicy
    {
        public string HostApiVersion = "1.0.0";
        public bool AllowWorldRead = true;
        public bool AllowWorldWrite;
        public bool AllowQuestWrite;
        public bool AllowEncounterControl;
        public bool AllowInventoryWrite;
        public bool AllowCombatRead;
        public bool AllowSessionRead;
        public bool AllowDebugLog;

        public IReadOnlyList<string> EnumerateGrantedCapabilities()
        {
            var capabilities = new List<string>();

            if (AllowWorldRead)
            {
                capabilities.Add("world.read");
            }

            if (AllowWorldWrite)
            {
                capabilities.Add("world.write");
            }

            if (AllowQuestWrite)
            {
                capabilities.Add("quest.write");
            }

            if (AllowEncounterControl)
            {
                capabilities.Add("encounter.control");
            }

            if (AllowCombatRead)
            {
                capabilities.Add("rules.query.combat");
            }

            if (AllowSessionRead)
            {
                capabilities.Add("session.read");
            }

            if (AllowInventoryWrite)
            {
                capabilities.Add("inventory.write");
            }

            if (AllowDebugLog)
            {
                capabilities.Add("debug.log");
            }

            return capabilities;
        }
    }
}