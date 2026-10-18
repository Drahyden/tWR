using UnityEngine;

namespace TWR.Audio
{
    /// <summary>
    /// The ambient bed (D-077, D-223), in Core so it keeps playing across section loads (D-161, D-167).
    /// 1. A 2D fluorescent hum loop on this object's AudioSource (no position, reverbZoneMix 0), so walking through a
    ///    portal (a 500 m teleport) causes no audio pop (D-141, D-144).
    /// 2. Sparse, randomized distant office one-shots (phone, printer, keyboard), placed 15-40 m from the player in a
    ///    random direction on a small pool of AudioSources that follows the player's position (so a portal crossing
    ///    doesn't cut them off), spatialBlend about 0.6, max 60 m. Only while SetOfficeSounds(true): the RunAmbienceDriver
    ///    turns them on per section template (SectionData flag) and in the lobby; off in silent rooms, boss arenas and
    ///    ending scenes.
    /// Silent rooms are handled by the mixer's Silent snapshot (D-168, D-220), not here. Only the Overtime ending's
    /// "ambience off" (D-221) fades the hum out locally. Clips come from the AudioLibrary (D-227); if it has no hum, a
    /// generated placeholder hum is used. All sources go to the AmbienceState group (D-219). No per-frame allocations.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AmbientBed : MonoBehaviour
    {
        [SerializeField] AmbientBedSettings settings;
        [Tooltip("The player: office one-shots are placed around it and follow it. Empty = the AudioListener found once at Start.")]
        [SerializeField] Transform follow;
        [Tooltip("Office one-shots on from the start (sandbox scene). In the run the RunAmbienceDriver switches them.")]
        [SerializeField] bool officeSoundsOnStart;

        const int SampleRate = 22050;
        static AudioClip placeholderHum;

        AudioSource hum;
        Transform anchor;
        AudioSource[] pool;
        int nextPoolIndex;
        float timer;
        bool officeOn;
        bool started;
        float humMultiplier = 1f, humTarget = 1f;

        /// <summary>Are the distant office one-shots switched on?</summary>
        public bool OfficeSoundsEnabled => officeOn;

        float HumVolume => settings != null ? settings.humVolume : 0.12f;

        void Awake()
        {
            hum = GetComponent<AudioSource>();
            hum.spatialBlend = 0f;     // 2D: no position, no distance falloff
            hum.loop = true;
            hum.playOnAwake = false;
            hum.dopplerLevel = 0f;
            hum.reverbZoneMix = 0f;    // the hum is "in the room", never in the reverb zones (D-223)
            AudioRouting.RouteIfUnassigned(hum, AudioBus.Ambience);
            officeOn = officeSoundsOnStart;
        }

        void OnEnable() => AudioState.Changed += HandleAudioStateChanged;
        void OnDisable() => AudioState.Changed -= HandleAudioStateChanged;

        void Start()
        {
            if (follow == null)
            {
                AudioListener listener = FindFirstObjectByType<AudioListener>(); // once, never in Update
                if (listener != null) follow = listener.transform;
            }
            CreatePool();

            humMultiplier = humTarget = AudioState.AmbienceOff ? 0f : 1f;
            AudioClip clip = AudioLibrary.Shared != null ? AudioLibrary.Shared.GetClip(AudioIds.AmbienceHum) : null;
            hum.clip = clip != null ? clip : PlaceholderHum();
            hum.volume = HumVolume * humMultiplier;
            hum.Play();

            started = true;
            if (officeOn) ScheduleNext(true);
        }

        /// <summary>Switch the distant office one-shots on or off (D-223). Off also stops any that are playing.</summary>
        public void SetOfficeSounds(bool on)
        {
            if (officeOn == on) return;
            officeOn = on;
            if (on)
            {
                if (started) ScheduleNext(true);
                return;
            }
            if (pool == null) return;
            for (int i = 0; i < pool.Length; i++) if (pool[i] != null) pool[i].Stop();
        }

        void Update()
        {
            // Hum fade only for the Overtime "ambience off" (D-221); silent rooms use the Silent snapshot.
            if (humMultiplier != humTarget)
            {
                float speed = settings != null && settings.humFadeOutSeconds > 0f ? 1f / settings.humFadeOutSeconds : 1f;
                humMultiplier = Mathf.MoveTowards(humMultiplier, humTarget, speed * Time.unscaledDeltaTime);
                hum.volume = HumVolume * humMultiplier;
            }

            if (!officeOn || pool == null || SoundscapeState.IsSilenced || AudioState.AmbienceOff) return;
            timer -= Time.deltaTime; // scaled: a paused game (timeScale 0) starts no new one-shots
            if (timer > 0f) return;
            PlayOfficeSound();
            ScheduleNext(false);
        }

        void LateUpdate()
        {
            // The pool follows the player's position only (not its rotation), so turning the head doesn't swing the sounds.
            if (anchor != null && follow != null) anchor.position = follow.position;
        }

        void OnDestroy()
        {
            if (anchor != null) Destroy(anchor.gameObject);
        }

        void HandleAudioStateChanged()
        {
            humTarget = AudioState.AmbienceOff ? 0f : 1f;
            if (AudioState.AmbienceOff && pool != null)
                for (int i = 0; i < pool.Length; i++) if (pool[i] != null) pool[i].Stop();
        }

        void CreatePool()
        {
            int size = settings != null ? settings.poolSize : 3;
            var anchorGO = new GameObject("Office One-Shots (follow the player, D-223)");
            anchor = anchorGO.transform;
            if (follow != null) anchor.position = follow.position;

            pool = new AudioSource[size];
            for (int i = 0; i < size; i++)
            {
                var go = new GameObject("Office One-Shot " + (i + 1));
                go.transform.SetParent(anchor, false);
                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.dopplerLevel = 0f;
                source.spatialBlend = settings != null ? settings.spatialBlend : 0.6f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = settings != null ? settings.minDistance : 5f;
                source.maxDistance = settings != null ? Mathf.Min(settings.maxDistance, 60f) : 60f;
                AudioRouting.Route(source, AudioBus.Ambience); // D-219
                pool[i] = source;
            }
        }

        void ScheduleNext(bool first)
        {
            Vector2 interval = settings != null ? settings.intervalSeconds : new Vector2(14f, 38f);
            timer = Random.Range(interval.x, interval.y);
            if (first) timer *= Random.Range(settings != null ? settings.firstDelayScale : 0.5f, 1f);
        }

        void PlayOfficeSound()
        {
            AudioLibrary library = AudioLibrary.Shared;
            if (library == null) return;
            string id = AudioIds.OfficeSounds[Random.Range(0, AudioIds.OfficeSounds.Length)];
            if (!library.TryGet(id, out AudioLibrary.Entry entry)) return;
            AudioClip clip = entry.PickClip();
            if (clip == null) return;

            AudioSource source = pool[nextPoolIndex];
            nextPoolIndex = (nextPoolIndex + 1) % pool.Length;
            if (source == null) return;

            Vector2 direction = Random.insideUnitCircle;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
            direction.Normalize();
            Vector2 distance = settings != null ? settings.distanceMetres : new Vector2(15f, 40f);
            Vector2 height = settings != null ? settings.heightMetres : new Vector2(0.5f, 2.5f);
            Vector2 volume = settings != null ? settings.volume : new Vector2(0.35f, 0.6f);
            float metres = Random.Range(distance.x, distance.y);

            source.Stop();
            source.transform.localPosition = new Vector3(direction.x * metres, Random.Range(height.x, height.y), direction.y * metres);
            source.clip = clip;
            source.pitch = entry.PickPitch();
            source.volume = entry.PickVolume() * Random.Range(volume.x, volume.y);
            source.Play();
        }

        /// Mains hum (60 Hz) + its buzzy harmonics (120, 180, 240 Hz), made once per session. Only used when the
        /// AudioLibrary has no "ambience.hum" clip (normally the A1 builder's placeholder WAV is there).
        static AudioClip PlaceholderHum()
        {
            if (placeholderHum != null) return placeholderHum;
            var data = new float[SampleRate]; // exactly 1 second: whole cycles, so the loop doesn't click
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
