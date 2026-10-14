using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// Anything on the persistent player that must go back to its run-start state when a new run begins
    /// (D-162: HP, magazines + reserve ammo, guns, inventory, abilities carry over between sections, but reset at
    /// run start, on death and on quit; D-120 run-start ammo). The RunManager calls ResetForNewRun on every
    /// implementer under the player through RunResets.ResetAll. Implementations must be safe to call twice in a row.
    /// </summary>
    public interface IRunResettable
    {
        void ResetForNewRun();
    }

    /// <summary>The one reset entry point (D-162): resets every IRunResettable on an object and its children.</summary>
    public static class RunResets
    {
        /// <summary>Calls ResetForNewRun on each IRunResettable under root. Returns how many were reset. Allocates; run start only.</summary>
        public static int ResetAll(GameObject root)
        {
            if (root == null) return 0;
            IRunResettable[] parts = root.GetComponentsInChildren<IRunResettable>(true);
            for (int i = 0; i < parts.Length; i++) parts[i].ResetForNewRun();
            return parts.Length;
        }
    }
}
