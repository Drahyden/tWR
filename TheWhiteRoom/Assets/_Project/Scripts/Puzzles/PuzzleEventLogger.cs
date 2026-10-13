using UnityEngine;

namespace TWR.Puzzles
{
    /// <summary>
    /// TEST ONLY: writes every PuzzleEvents call to the Console (D-126) so you can see the
    /// hooks fire that the inner voice (D-080), run stats (D-029, D-104) and music (D-075) will use later.
    /// Builds log strings, so keep it out of real levels.
    /// </summary>
    public class PuzzleEventLogger : MonoBehaviour
    {
        void OnEnable()
        {
            PuzzleEvents.Started += LogStarted;
            PuzzleEvents.SwitchActivated += LogSwitchActivated;
            PuzzleEvents.SwitchDeactivated += LogSwitchDeactivated;
            PuzzleEvents.Reset += LogReset;
            PuzzleEvents.Solved += LogSolved;
        }

        void OnDisable()
        {
            PuzzleEvents.Started -= LogStarted;
            PuzzleEvents.SwitchActivated -= LogSwitchActivated;
            PuzzleEvents.SwitchDeactivated -= LogSwitchDeactivated;
            PuzzleEvents.Reset -= LogReset;
            PuzzleEvents.Solved -= LogSolved;
        }

        void LogStarted(string puzzleId) => Debug.Log($"[PuzzleEvents] Started: {puzzleId}", this);

        void LogSwitchActivated(string puzzleId, string switchId, GameObject activator) =>
            Debug.Log($"[PuzzleEvents] SwitchActivated: {puzzleId} / {switchId} by {(activator != null ? activator.name : "unknown")}", this);

        void LogSwitchDeactivated(string puzzleId, string switchId, GameObject activator) =>
            Debug.Log($"[PuzzleEvents] SwitchDeactivated: {puzzleId} / {switchId} by {(activator != null ? activator.name : "unknown")}", this);

        void LogReset(string puzzleId) => Debug.Log($"[PuzzleEvents] Reset: {puzzleId}", this);

        void LogSolved(string puzzleId, bool isOptional) =>
            Debug.Log($"[PuzzleEvents] Solved: {puzzleId} (optional: {isOptional})", this);
    }
}
