using UnityEngine;
using UnityEngine.Localization;

namespace TWR.Core
{
    /// <summary>
    /// Development stand-in for the inner-voice player (D-080): writes every VoiceEvents cue to the Console
    /// with its subtitle key, so voice triggers can be tested before any line is recorded (D-031).
    /// Covers section start/end (D-167), fork doors (D-166) and puzzle family hints (D-178).
    /// Lives in the Core scene. Logs only; never shown to the player.
    /// </summary>
    public class VoiceEventLogger : MonoBehaviour
    {
        void OnEnable()
        {
            VoiceEvents.SectionStarted += HandleSectionStarted;
            VoiceEvents.SectionEnded += HandleSectionEnded;
            VoiceEvents.ForkDoorSeen += HandleForkDoorSeen;
            VoiceEvents.PuzzleHint += HandlePuzzleHint;
        }

        void OnDisable()
        {
            VoiceEvents.SectionStarted -= HandleSectionStarted;
            VoiceEvents.SectionEnded -= HandleSectionEnded;
            VoiceEvents.ForkDoorSeen -= HandleForkDoorSeen;
            VoiceEvents.PuzzleHint -= HandlePuzzleHint;
        }

        void HandleSectionStarted(string id) => Debug.Log($"[Voice] section started: {id}", this);
        void HandleSectionEnded(string id) => Debug.Log($"[Voice] section ended: {id}", this);

        void HandleForkDoorSeen(string doorId, LocalizedString subtitle) =>
            Debug.Log($"[Voice] fork door seen: {doorId} | subtitle {Key(subtitle)} (no audio yet)", this);

        void HandlePuzzleHint(string puzzleId, LocalizedString subtitle) =>
            Debug.Log($"[Voice] puzzle hint: {puzzleId} | subtitle {Key(subtitle)} (no audio yet)", this);

        static string Key(LocalizedString subtitle) => subtitle == null || subtitle.IsEmpty
            ? "(none)"
            : $"{subtitle.TableReference.TableCollectionName}/{subtitle.TableEntryReference.Key}";
    }
}
