using UnityEngine;
using UnityEngine.Localization;

namespace TWR.Core
{
    /// <summary>
    /// Development stand-in for the inner-voice player (D-080): writes every VoiceEvents cue to the Console
    /// with its subtitle key, so voice triggers can be tested before any line is recorded (D-031).
    /// Lives in the Core scene. Logs only; never shown to the player.
    /// </summary>
    public class VoiceEventLogger : MonoBehaviour
    {
        void OnEnable()
        {
            VoiceEvents.SectionStarted += HandleSectionStarted;
            VoiceEvents.SectionEnded += HandleSectionEnded;
            VoiceEvents.ForkDoorSeen += HandleForkDoorSeen;
        }

        void OnDisable()
        {
            VoiceEvents.SectionStarted -= HandleSectionStarted;
            VoiceEvents.SectionEnded -= HandleSectionEnded;
            VoiceEvents.ForkDoorSeen -= HandleForkDoorSeen;
        }

        void HandleSectionStarted(string id) => Debug.Log($"[Voice] section started: {id}", this);
        void HandleSectionEnded(string id) => Debug.Log($"[Voice] section ended: {id}", this);

        void HandleForkDoorSeen(string doorId, LocalizedString subtitle)
        {
            string key = subtitle == null || subtitle.IsEmpty
                ? "(none)"
                : $"{subtitle.TableReference.TableCollectionName}/{subtitle.TableEntryReference.Key}";
            Debug.Log($"[Voice] fork door seen: {doorId} | subtitle {key} (no audio yet)", this);
        }
    }
}
