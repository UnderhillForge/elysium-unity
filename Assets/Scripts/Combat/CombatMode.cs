namespace Elysium.Combat
{
    /// Combat mode determines who controls turn progression and initiative.
    /// Defines the governance model for a combat session.
    public enum CombatMode
    {
        /// GM-guided turn-based combat in multiplayer.
        /// - GM initiates combat and has exclusive control over turn progression
        /// - GM approves actions, resolves rolls, applies effects
        /// - Players submit intents; GM validates and commits results
        /// - Requires active GM connection to progress
        GMGuided = 0,
        
        /// Solo player training/practice mode without GM.
        /// - Single player can freely test combat mechanics
        /// - No validation or approval needed for actions
        /// - Encounters pre-configured; no dynamic scaling
        /// - Full access to all rolls and mechanics
        /// - Useful for learning rules and testing character builds
        PlayerTraining = 1,
        
        /// Special/future combat modes.
        /// - Potential uses: auto-difficulty scaling, AI opponents, tournament modes
        /// - Specific behavior TBD per implementation
        /// - May have hybrid rules (semi-automated combat)
        Special = 2,
    }
}
