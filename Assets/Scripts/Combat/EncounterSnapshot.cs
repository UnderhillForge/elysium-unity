using System;
using System.Collections.Generic;

namespace Elysium.Combat
{
    [Serializable]
    public sealed class EncounterSnapshot
    {
        public string EncounterId = string.Empty;
        public int RoundNumber;
        public bool IsActive;
        
        // Combat governance
        public CombatMode Mode = CombatMode.GMGuided;
        public string GMId = string.Empty;  // Only set if Mode == GMGuided; the authoritative GM
        
        // Turn tracking (v1.2+)
        public string CurrentCombatantId = string.Empty;  // Whose turn is it?
        public List<string> CombatantIds = new List<string>();  // All participants in order
        
        /// In GM-guided mode, combat cannot progress without an active GM connection.
        public bool RequiresGMPresence => Mode == CombatMode.GMGuided && !string.IsNullOrEmpty(GMId);
        
        /// In player training mode, combat progresses freely without GM supervision.
        public bool IsPlayerTraining => Mode == CombatMode.PlayerTraining;
    }
}