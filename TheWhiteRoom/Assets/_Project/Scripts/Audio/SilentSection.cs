using UnityEngine;
using TWR.Core;

namespace TWR.Audio
{
    /// <summary>
    /// Makes the scene it sits in a silent room (D-168, D-082): while it is loaded, it requests silence from
    /// SoundscapeState, which switches the mixer to the Silent snapshot (music + ambient bed fade out, D-220), and
    /// inner-voice triggers are suppressed (VoiceEvents).
    /// Player, gun and enemy sound effects keep playing; gameplay noise events are untouched. Unloading the
    /// section (OnDisable) gives everything back. The silent room's exit is never locked (D-165).
    /// </summary>
    public class SilentSection : MonoBehaviour
    {
        [SerializeField] SoundscapeSettings settings;

        bool active;

        float FadeOut => settings != null ? settings.silenceFadeOutSeconds : 1.5f;
        float FadeIn => settings != null ? settings.restoreFadeInSeconds : 1f;

        void OnEnable()
        {
            if (active) return;
            active = true;
            SoundscapeState.RequestSilence(FadeOut);
            VoiceEvents.PushSuppression();
            Debug.Log("[Audio] Silent room: Silent snapshot (music + ambience fade out), inner voice suppressed (D-168, D-220).", this);
        }

        void OnDisable()
        {
            if (!active) return;
            active = false;
            SoundscapeState.ReleaseSilence(FadeIn);
            VoiceEvents.PopSuppression();
        }
    }
}
