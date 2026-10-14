using UnityEngine;

namespace TWR.Core
{
    /// <summary>What the player-side scripts may ask of the run system (D-157). Implemented by TWR.Run.RunManager.</summary>
    public interface IRunHandler
    {
        /// <summary>The player died (D-034): end the run and go back to the hub lobby with a new plan.</summary>
        void HandlePlayerDeath();
        /// <summary>The player quit mid-run (D-093): same reset as death.</summary>
        void HandleQuit();
    }

    /// <summary>
    /// A tiny meeting point so Player scripts (PlayerHealth, the future pause menu) can reach the RunManager
    /// without referencing the TWR.Run namespace, which already depends on Player through RunStats (D-157).
    /// Handler is null when no RunManager exists (then callers fall back to reloading the scene).
    /// </summary>
    public static class RunHooks
    {
        public static IRunHandler Handler { get; private set; }

        public static void Register(IRunHandler handler) => Handler = handler;

        public static void Unregister(IRunHandler handler)
        {
            if (Handler == handler) Handler = null;
        }

        // Clears the handler when entering Play mode without a domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Handler = null;
    }
}
