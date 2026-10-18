using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWR.Audio
{
    /// <summary>
    /// Sound ID -> clip(s) for the global sounds: ambience, UI, mechanisms and the player (D-227). Code asks for an ID
    /// from AudioIds; you swap files by dragging new clips into this asset, without touching code. Each entry can hold
    /// several clips (one is picked at random) plus a volume and pitch range. Enemy and boss sounds stay in their own
    /// sound set assets (D-138). Lives in Resources (Data/Resources/AudioLibrary) and is loaded once; lookups use a
    /// dictionary built on first use, so playing a sound allocates nothing (D-100).
    /// Filled by TWR > Audio > Build Audio (A1): real files where they exist (fluorescent hum, violin stinger), else the
    /// generated placeholders; clips you assign by hand are never replaced (D-228). It may hold clips nothing plays yet
    /// (the stinger, A5). Every clip needs a LICENSES.md row (D-218).
    /// </summary>
    [CreateAssetMenu(fileName = "AudioLibrary", menuName = "TWR/Audio/Audio Library")]
    public class AudioLibrary : ScriptableObject
    {
        public const string ResourcePath = "AudioLibrary";

        [Serializable]
        public class Entry
        {
            [Tooltip("An ID from TWR.Audio.AudioIds, e.g. 'ui.click'.")]
            public string id;
            [Tooltip("One is picked at random each time. Empty = silent.")]
            public AudioClip[] clips = new AudioClip[0];
            [Range(0f, 1f)] public float volumeMin = 1f;
            [Range(0f, 1f)] public float volumeMax = 1f;
            [Range(0.5f, 2f)] public float pitchMin = 1f;
            [Range(0.5f, 2f)] public float pitchMax = 1f;
            [Tooltip("These clips are loops. The AudioSource that plays them sets looping itself; this flag tells " +
                     "TWR > Audio > Check Loops to measure the loop seam (D-228). Set by the builder for AudioIds.Loops.")]
            public bool loop;

            public bool HasClips
            {
                get
                {
                    if (clips == null) return false;
                    for (int i = 0; i < clips.Length; i++) if (clips[i] != null) return true;
                    return false;
                }
            }

            /// <summary>A random non-empty clip, or null.</summary>
            public AudioClip PickClip()
            {
                if (clips == null || clips.Length == 0) return null;
                AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Length)];
                if (clip != null) return clip;
                for (int i = 0; i < clips.Length; i++) if (clips[i] != null) return clips[i];
                return null;
            }

            public float PickVolume() => UnityEngine.Random.Range(Mathf.Min(volumeMin, volumeMax), Mathf.Max(volumeMin, volumeMax));
            public float PickPitch() => UnityEngine.Random.Range(Mathf.Min(pitchMin, pitchMax), Mathf.Max(pitchMin, pitchMax));
        }

        public Entry[] entries = new Entry[0];

        [NonSerialized] Dictionary<string, Entry> lookup;

        static AudioLibrary shared;
        static bool loaded;

        /// <summary>Resources/AudioLibrary (null + one warning if missing).</summary>
        public static AudioLibrary Shared
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    shared = Resources.Load<AudioLibrary>(ResourcePath);
                    if (shared == null)
                        Debug.LogWarning("[Audio] Resources/AudioLibrary not found: library sounds are silent. Run TWR > Audio > Build Audio (A1).");
                }
                return shared;
            }
        }

        public bool TryGet(string id, out Entry entry)
        {
            if (lookup == null) BuildLookup();
            entry = null;
            return !string.IsNullOrEmpty(id) && lookup.TryGetValue(id, out entry) && entry != null;
        }

        /// <summary>A random clip of this ID, or null (unknown ID or no clips).</summary>
        public AudioClip GetClip(string id) => TryGet(id, out Entry entry) ? entry.PickClip() : null;

        /// <summary>
        /// Play the sound on this source as a one-shot (volume from the entry x volumeScale). applyPitch = set the source's
        /// pitch from the entry first; only use it on sources nothing else plays on at the same time (UI, doors).
        /// False if nothing was played.
        /// </summary>
        public static bool PlayOneShot(AudioSource source, string id, float volumeScale = 1f, bool applyPitch = false)
        {
            AudioLibrary library = Shared;
            if (source == null || library == null || !library.TryGet(id, out Entry entry)) return false;
            AudioClip clip = entry.PickClip();
            if (clip == null) return false;
            if (applyPitch) source.pitch = entry.PickPitch();
            source.PlayOneShot(clip, entry.PickVolume() * volumeScale);
            return true;
        }

        void BuildLookup()
        {
            lookup = new Dictionary<string, Entry>(entries != null ? entries.Length : 0);
            if (entries == null) return;
            foreach (Entry entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                if (lookup.ContainsKey(entry.id)) Debug.LogWarning($"[Audio] AudioLibrary: duplicate id '{entry.id}', the first one is used.", this);
                else lookup.Add(entry.id, entry);
            }
        }

        void OnValidate() => lookup = null; // edited in the Inspector: rebuild on the next lookup

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            shared = null;
            loaded = false;
        }
    }
}
