using System;
using UnityEngine;

namespace TWR.Puzzles
{
    /// <summary>
    /// Shared static puzzle events (D-126). Hooks for the inner voice (D-080, D-056 hints),
    /// run stats / puzzle completeness (D-029, D-104) and adaptive music (D-075). Listeners need no
    /// scene references: they get the puzzle ID and decide for themselves whether to react.
    /// Only PuzzleController raises these.
    /// </summary>
    public static class PuzzleEvents
    {
        /// <summary>(puzzleId). The player entered the puzzle area. At most once per scene load.</summary>
        public static event Action<string> Started;

        /// <summary>(puzzleId, switchId, activator). A switch turned ON. activator = the shooter (or interactor).</summary>
        public static event Action<string, string, GameObject> SwitchActivated;

        /// <summary>(puzzleId, switchId, activator). A Toggle switch turned OFF again.</summary>
        public static event Action<string, string, GameObject> SwitchDeactivated;

        /// <summary>(puzzleId). A Sequence puzzle got a wrong switch and every switch turned off.</summary>
        public static event Action<string> Reset;

        /// <summary>(puzzleId, isOptional). The puzzle is solved. isOptional feeds puzzle completeness (D-104).</summary>
        public static event Action<string, bool> Solved;

        public static void RaiseStarted(string puzzleId) => Started?.Invoke(puzzleId);
        public static void RaiseSwitchActivated(string puzzleId, string switchId, GameObject activator) => SwitchActivated?.Invoke(puzzleId, switchId, activator);
        public static void RaiseSwitchDeactivated(string puzzleId, string switchId, GameObject activator) => SwitchDeactivated?.Invoke(puzzleId, switchId, activator);
        public static void RaiseReset(string puzzleId) => Reset?.Invoke(puzzleId);
        public static void RaiseSolved(string puzzleId, bool isOptional) => Solved?.Invoke(puzzleId, isOptional);

        // Clears listeners when entering Play mode without a domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Started = null;
            SwitchActivated = null;
            SwitchDeactivated = null;
            Reset = null;
            Solved = null;
        }
    }
}
