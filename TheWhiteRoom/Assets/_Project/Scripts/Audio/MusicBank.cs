using UnityEngine;

namespace TWR.Audio
{
    /// <summary>
    /// One set of music AudioSources that plays one track (D-214, D-231). The MusicDirector owns two banks, so the old state
    /// can fade out while the new one fades in (crossfade, no beat-matching, D-214).
    /// - Begin: every stem gets its own 2D source and all of them start with AudioSource.PlayScheduled on the SAME dspTime,
    ///   so they stay sample-locked while they loop (D-231). All stems always play; a layer is switched by volume only.
    /// - BeginOneShot: one ending stinger, not looping (D-234).
    /// - Each frame Tick moves the bank volume and every layer volume towards its target. Source volume =
    ///   baseVolume x bank volume x layer volume. A bank that has faded to 0 stops its sources (no CPU when idle).
    /// Sources are created once, routed to MusicState (D-219, D-233) and reused, so nothing allocates while playing (D-100).
    /// </summary>
    public sealed class MusicBank
    {
        const float Instant = 1000000f;

        readonly AudioSource[] sources = new AudioSource[MusicData.MaxStems];
        readonly float[] layerVolume = new float[MusicData.MaxStems];
        readonly float[] layerTarget = new float[MusicData.MaxStems];

        double startDsp;
        float fadeRate = Instant;
        float layerRate = Instant;
        float baseVolume = 1f;
        bool snapLayers;

        public string Label { get; }
        /// <summary>The looping track playing on this bank (null if idle or playing an ending stinger).</summary>
        public MusicData Data { get; private set; }
        /// <summary>The ending stinger playing on this bank (null otherwise).</summary>
        public AudioClip OneShot { get; private set; }
        /// <summary>Layers in use (0 = idle).</summary>
        public int Count { get; private set; }
        public bool IsPlaying => Count > 0;
        public float Volume { get; private set; }
        public float TargetVolume { get; private set; }

        public MusicBank(Transform parent, string label)
        {
            Label = label;
            var go = new GameObject("Music " + label + " (2D, MusicState)");
            go.transform.SetParent(parent, false);
            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;         // 2D music
                source.dopplerLevel = 0f;
                source.reverbZoneMix = 0f;        // reverb zones are for the world, not the score (D-224)
                source.bypassReverbZones = true;
                source.priority = 0;              // never stolen by SFX voices
                source.ignoreListenerPause = true; // music keeps playing under a future pause menu (Paused snapshot, D-220)
                source.volume = 0f;
                AudioRouting.Route(source, AudioBus.Music); // MusicState (D-219, D-233)
                sources[i] = source;
            }
        }

        /// <summary>Start every stem of this track on the same dspTime. Layer volumes snap to the first targets set.</summary>
        public void Begin(MusicData data, double dspTime)
        {
            StopNow();
            if (data == null) return;
            Data = data;
            Count = data.StemCount;
            baseVolume = data.baseVolume;
            layerRate = Rate(data.layerFadeSeconds);
            for (int i = 0; i < Count; i++)
            {
                AudioClip clip = data.StemClip(i);
                AudioSource source = sources[i];
                source.clip = clip;
                source.loop = true;
                source.volume = 0f;
                layerVolume[i] = 0f;
                layerTarget[i] = 0f;
                if (clip != null) source.PlayScheduled(dspTime);
            }
            startDsp = dspTime;
            Volume = 0f;
            snapLayers = true;
        }

        /// <summary>Play one ending stinger once (D-234).</summary>
        public void BeginOneShot(AudioClip clip, float volume, double dspTime)
        {
            StopNow();
            if (clip == null) return;
            OneShot = clip;
            Count = 1;
            baseVolume = volume;
            layerRate = Instant;
            AudioSource source = sources[0];
            source.clip = clip;
            source.loop = false;
            source.volume = 0f;
            layerVolume[0] = 1f;
            layerTarget[0] = 1f;
            source.PlayScheduled(dspTime);
            startDsp = dspTime;
            Volume = 0f;
            snapLayers = false;
        }

        /// <summary>Fade the whole bank to target (0..1) in this many seconds.</summary>
        public void FadeTo(float target, float seconds)
        {
            TargetVolume = Mathf.Clamp01(target);
            fadeRate = Rate(seconds);
        }

        public void SetLayer(int index, bool on)
        {
            if (index >= 0 && index < Count) layerTarget[index] = on ? 1f : 0f;
        }

        /// <summary>Move the fades on (call once per frame with unscaled delta time). gain = an extra multiplier on every
        /// source (the jump-scare dip, ScareDip.MusicGain, D-237).</summary>
        public void Tick(float deltaTime, float gain = 1f)
        {
            if (Count == 0) return;
            if (snapLayers)
            {
                snapLayers = false;
                for (int i = 0; i < Count; i++) layerVolume[i] = layerTarget[i];
            }

            Volume = Mathf.MoveTowards(Volume, TargetVolume, fadeRate * deltaTime);
            for (int i = 0; i < Count; i++)
            {
                layerVolume[i] = Mathf.MoveTowards(layerVolume[i], layerTarget[i], layerRate * deltaTime);
                sources[i].volume = baseVolume * Volume * layerVolume[i] * gain;
            }

            if (TargetVolume <= 0f && Volume <= 0f) StopNow();
            else if (OneShot != null && AudioSettings.dspTime > startDsp + 0.5 && !sources[0].isPlaying) StopNow(); // stinger ended
        }

        /// <summary>Stop at once and forget the track.</summary>
        public void StopNow()
        {
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] == null) continue;
                sources[i].Stop();
                sources[i].clip = null;
                sources[i].volume = 0f;
                layerVolume[i] = 0f;
                layerTarget[i] = 0f;
            }
            Count = 0;
            Data = null;
            OneShot = null;
            Volume = 0f;
            TargetVolume = 0f;
        }

        // ---------- debug overlay (read only) ----------

        public float LayerVolume(int index) => index >= 0 && index < Count ? layerVolume[index] : 0f;
        public bool LayerOn(int index) => index >= 0 && index < Count && layerTarget[index] > 0.5f;
        public AudioSource Source(int index) => index >= 0 && index < Count ? sources[index] : null;

        static float Rate(float seconds) => seconds > 0.0001f ? 1f / seconds : Instant;
    }
}
