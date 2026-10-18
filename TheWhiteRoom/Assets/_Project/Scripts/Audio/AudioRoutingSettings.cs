using System;
using UnityEngine;
using UnityEngine.Audio;

namespace TWR.Audio
{
    /// <summary>
    /// The mixer references the game needs at runtime (D-219, D-220): TWR_Mixer, its five STATE groups and its four
    /// snapshots, plus the snapshot transition times. Lives in a Resources folder (Data/Resources/AudioRoutingSettings)
    /// so code-created AudioSources (pools, the bootstrap's UI source) can be routed without scene references (D-161).
    /// "TWR > Audio > Build Audio (A1)" fills the references by NAME from the mixer; you only tune the times.
    /// If a group reference is empty at runtime it is looked up once by name (AudioMixer.FindMatchingGroups).
    /// </summary>
    [CreateAssetMenu(fileName = "AudioRoutingSettings", menuName = "TWR/Audio/Audio Routing Settings")]
    public class AudioRoutingSettings : ScriptableObject
    {
        /// <summary>Resources.Load path (the asset must sit directly in a Resources folder).</summary>
        public const string ResourcePath = "AudioRoutingSettings";

        [Header("Mixer - D-219 (filled by TWR > Audio > Build Audio (A1))")]
        public AudioMixer mixer;

        [Header("State groups (child of each user group) - D-219")]
        public AudioMixerGroup musicState;
        public AudioMixerGroup ambienceState;
        public AudioMixerGroup sfxState;
        public AudioMixerGroup voiceState;
        public AudioMixerGroup uiState;

        [Header("Snapshots - D-220")]
        public AudioMixerSnapshot defaultSnapshot;
        public AudioMixerSnapshot silentSnapshot;
        public AudioMixerSnapshot endingSnapshot;
        public AudioMixerSnapshot pausedSnapshot;

        [Header("Snapshot transition times (seconds, unscaled) - D-220, D-221")]
        [Tooltip("Back to Default (leaving an ending, main menu, new run). Silent rooms use SoundscapeSettings instead.")]
        [Min(0f)] public float toDefaultSeconds = 1f;
        [Tooltip("Into the Ending snapshot (scripted ending scene start, Overtime button).")]
        [Min(0f)] public float toEndingSeconds = 1.5f;
        [Tooltip("Into / out of the Paused snapshot (future pause menu, D-087).")]
        [Min(0f)] public float toPausedSeconds = 0.2f;

        [NonSerialized] AudioMixerGroup[] foundByName;

        /// <summary>The state group of a bus (the serialized reference, else found once by name). Null if missing.</summary>
        public AudioMixerGroup GetStateGroup(AudioBus bus)
        {
            AudioMixerGroup group = SerializedGroup(bus);
            if (group != null || mixer == null) return group;

            if (foundByName == null) foundByName = new AudioMixerGroup[AudioRouting.BusCount];
            int index = (int)bus;
            if (foundByName[index] == null) foundByName[index] = AudioRouting.FindGroup(mixer, AudioRouting.StateGroupName(bus));
            return foundByName[index];
        }

        /// <summary>The snapshot of this kind (the serialized reference, else found by name). Null if missing.</summary>
        public AudioMixerSnapshot GetSnapshot(AudioSnapshotKind kind)
        {
            AudioMixerSnapshot snapshot;
            switch (kind)
            {
                case AudioSnapshotKind.Silent: snapshot = silentSnapshot; break;
                case AudioSnapshotKind.Ending: snapshot = endingSnapshot; break;
                case AudioSnapshotKind.Paused: snapshot = pausedSnapshot; break;
                default: snapshot = defaultSnapshot; break;
            }
            if (snapshot == null && mixer != null) snapshot = mixer.FindSnapshot(AudioRouting.SnapshotName(kind));
            return snapshot;
        }

        AudioMixerGroup SerializedGroup(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Music: return musicState;
                case AudioBus.Ambience: return ambienceState;
                case AudioBus.SFX: return sfxState;
                case AudioBus.Voice: return voiceState;
                default: return uiState;
            }
        }
    }
}
