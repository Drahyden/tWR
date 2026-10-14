using UnityEngine;

namespace TWR.Audio
{
    /// <summary>
    /// The non-spatial ambient bed (D-077 fluorescent hum; D-144). A 2D looping AudioSource, so it sounds the
    /// same everywhere: walking through a portal (a 500 m teleport) causes no audio pop. The clip slot may be empty:
    /// then a cheap generated placeholder hum is used (whole cycles in 1 s, so the loop doesn't click) until a real
    /// SUNO/FADR bed exists (D-006, D-083). Volume will move to the Sound settings / mixer later (D-090).
    /// In the run it lives in the Core scene and keeps playing across section loads (D-161, D-167); silent rooms
    /// fade it out and back in through SoundscapeState (D-168).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AmbientBed : MonoBehaviour
    {
        [Tooltip("Empty = generated placeholder hum.")]
        [SerializeField] AudioClip clip;
        [SerializeField, Range(0f, 1f)] float volume = 0.12f;

        const int SampleRate = 22050;
        static AudioClip placeholderHum;

        AudioSource source;
        float multiplier = 1f;       // 1 = normal, 0 = silent room (D-168)
        float targetMultiplier = 1f;
        float fadeSpeed;             // multiplier units per second

        void OnEnable() => SoundscapeState.Changed += HandleSoundscapeChanged;
        void OnDisable() => SoundscapeState.Changed -= HandleSoundscapeChanged;

        void Start()
        {
            source = GetComponent<AudioSource>();
            source.spatialBlend = 0f; // 2D: no position, no distance falloff
            source.loop = true;
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            multiplier = targetMultiplier = SoundscapeState.IsSilenced ? 0f : 1f;
            source.volume = volume * multiplier;
            source.clip = clip != null ? clip : PlaceholderHum();
            source.Play();
        }

        void Update()
        {
            if (source == null || multiplier == targetMultiplier) return; // nothing to fade: no work (D-100)
            multiplier = Mathf.MoveTowards(multiplier, targetMultiplier, fadeSpeed * Time.unscaledDeltaTime);
            source.volume = volume * multiplier;
        }

        void HandleSoundscapeChanged(bool silenced, float fadeSeconds)
        {
            targetMultiplier = silenced ? 0f : 1f;
            fadeSpeed = fadeSeconds > 0f ? 1f / fadeSeconds : float.MaxValue;
        }

        /// Mains hum (60 Hz) + its buzzy harmonics (120, 180, 240 Hz). Made once per session.
        static AudioClip PlaceholderHum()
        {
            if (placeholderHum != null) return placeholderHum;
            var data = new float[SampleRate]; // exactly 1 second
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                data[i] = 0.30f * Mathf.Sin(2f * Mathf.PI * 60f * t)
                        + 0.35f * Mathf.Sin(2f * Mathf.PI * 120f * t)
                        + 0.12f * Mathf.Sin(2f * Mathf.PI * 180f * t)
                        + 0.08f * Mathf.Sin(2f * Mathf.PI * 240f * t);
            }
            placeholderHum = AudioClip.Create("Placeholder Ambient Hum", data.Length, 1, SampleRate, false);
            placeholderHum.SetData(data, 0);
            return placeholderHum;
        }
    }
}
