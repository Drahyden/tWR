using UnityEngine;

namespace TWR.Audio
{
    /// <summary>
    /// The short music / ambience dip around a jump scare (D-237). It is a code-side gain, not a mixer snapshot or exposed
    /// parameter, so D-219 / D-220 stay as they are: the MusicDirector multiplies its music volume by MusicGain and the
    /// AmbientBed its hum by AmbienceGain. Envelope: attack -> hold -> release, all in unscaled seconds (audio fades always
    /// run, like the music fades). Gains are worked out from the clock when read, so nothing needs ticking. Only the
    /// ScareDirector starts it.
    /// </summary>
    public static class ScareDip
    {
        static float musicFloor = 1f, ambienceFloor = 1f;
        static float start, attack, hold, release;
        static bool active;

        /// <summary>Music volume multiplier right now (1 = no dip).</summary>
        public static float MusicGain => active ? Mathf.Lerp(1f, musicFloor, Depth()) : 1f;
        /// <summary>Ambient bed (hum) volume multiplier right now (1 = no dip).</summary>
        public static float AmbienceGain => active ? Mathf.Lerp(1f, ambienceFloor, Depth()) : 1f;
        public static bool IsActive => active && Depth() > 0f;

        /// <summary>Start a dip. dB at full depth (-80 or lower = silence, 0 = untouched).</summary>
        public static void Begin(float musicDb, float ambienceDb, float attackSeconds, float holdSeconds, float releaseSeconds)
        {
            musicFloor = DbToGain(musicDb);
            ambienceFloor = DbToGain(ambienceDb);
            attack = Mathf.Max(0.001f, attackSeconds);
            hold = Mathf.Max(0f, holdSeconds);
            release = Mathf.Max(0.001f, releaseSeconds);
            start = Time.unscaledTime;
            active = true;
        }

        /// <summary>End any dip at once (run reset, debug).</summary>
        public static void Stop() => active = false;

        /// 0 = no dip, 1 = full depth.
        static float Depth()
        {
            float t = Time.unscaledTime - start;
            if (t < attack) return t / attack;
            t -= attack;
            if (t < hold) return 1f;
            t -= hold;
            if (t < release) return 1f - t / release;
            active = false;
            return 0f;
        }

        static float DbToGain(float db) => db <= -79f ? 0f : Mathf.Pow(10f, db / 20f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => active = false;
    }
}
