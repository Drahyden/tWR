using System;
using UnityEngine;
using UnityEngine.Localization;

namespace TWR.Core
{
    /// <summary>
    /// Minimal inner-voice trigger events (D-080, D-024). No voice player exists yet: these are the hooks it will
    /// listen to. Raised for the start/end of every section (D-167) and when the player first reaches a fork door
    /// (D-166, D-150), and with a generic hint when a puzzle starts (D-178). Each cue may carry a localized subtitle key (D-084, D-089); no audio is attached yet.
    /// Silent rooms suppress every inner-voice trigger while they are loaded (D-168): Push/PopSuppression.
    /// Static, like PuzzleEvents / InteractionEvents, so listeners need no scene references (D-161).
    /// </summary>
    public static class VoiceEvents
    {
        static int suppression;

        /// <summary>(sectionId). A section (or the lobby, id "lobby") finished loading and is now playable.</summary>
        public static event Action<string> SectionStarted;
        /// <summary>(sectionId). The player left a section (exit, death, quit or run complete).</summary>
        public static event Action<string> SectionEnded;
        /// <summary>(doorId, subtitle). The player first came close to a fork door (D-166).</summary>
        public static event Action<string, LocalizedString> ForkDoorSeen;
        /// <summary>(puzzleId, subtitle). A puzzle started; the generic hint for its rule family (D-056, D-178).</summary>
        public static event Action<string, LocalizedString> PuzzleHint;

        /// <summary>True while a silent room is loaded (D-168): no inner-voice trigger fires.</summary>
        public static bool Suppressed => suppression > 0;

        public static void PushSuppression() => suppression++;
        public static void PopSuppression() => suppression = Mathf.Max(0, suppression - 1);

        public static void RaiseSectionStarted(string sectionId)
        {
            if (IsSuppressed("section started", sectionId)) return;
            SectionStarted?.Invoke(sectionId);
        }

        public static void RaiseSectionEnded(string sectionId)
        {
            if (IsSuppressed("section ended", sectionId)) return;
            SectionEnded?.Invoke(sectionId);
        }

        public static void RaiseForkDoorSeen(string doorId, LocalizedString subtitle)
        {
            if (IsSuppressed("fork door seen", doorId)) return;
            ForkDoorSeen?.Invoke(doorId, subtitle);
        }

        public static void RaisePuzzleHint(string puzzleId, LocalizedString subtitle)
        {
            if (IsSuppressed("puzzle hint", puzzleId)) return;
            PuzzleHint?.Invoke(puzzleId, subtitle);
        }

        static bool IsSuppressed(string what, string id)
        {
            if (!Suppressed) return false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Voice] '{what}' ({id}) suppressed: silent room (D-168).");
#endif
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            suppression = 0;
            SectionStarted = null;
            SectionEnded = null;
            ForkDoorSeen = null;
            PuzzleHint = null;
        }
    }
}
