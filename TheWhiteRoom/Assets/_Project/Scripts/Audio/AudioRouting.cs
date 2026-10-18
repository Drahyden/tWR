using UnityEngine;
using UnityEngine.Audio;

namespace TWR.Audio
{
    /// <summary>
    /// The one place that knows where sounds go (D-219). Route(source, bus) sends an AudioSource to that bus's STATE
    /// group; the builders use it at build time and runtime code uses it for sources it creates (ambient bed pool,
    /// bootstrap UI source). Also holds the agreed names of the mixer parts (groups, exposed volume parameters,
    /// snapshots) so code, builders and the [TWR] validation never disagree (D-227). While a state group is missing from
    /// the mixer, sources go to its parent user group and one warning is logged per session (never per sound). Settings come from
    /// Resources/AudioRoutingSettings, loaded once.
    /// </summary>
    public static class AudioRouting
    {
        static readonly string[] UserGroups = { "Music", "Ambience", "SFX", "Voice", "UI" };
        static readonly string[] StateGroups = { "MusicState", "AmbienceState", "SFXState", "VoiceState", "UIState" };
        static readonly string[] VolumeParams = { "MusicVol", "AmbienceVol", "SFXVol", "VoiceVol", "UIVol" };
        static readonly string[] Snapshots = { "Default", "Silent", "Ending", "Paused" };

        public const int BusCount = 5;
        public const int SnapshotCount = 4;

        static AudioRoutingSettings settings;
        static bool loaded;
        static bool warnedMissingStateGroups;

        /// <summary>The user group name, e.g. "Music" (its Volume is the exposed slider parameter).</summary>
        public static string UserGroupName(AudioBus bus) => UserGroups[(int)bus];
        /// <summary>The state group name, e.g. "MusicState" (child of the user group; snapshots control it).</summary>
        public static string StateGroupName(AudioBus bus) => StateGroups[(int)bus];
        /// <summary>The exposed volume parameter, e.g. "MusicVol" (set only by the sound settings, D-225).</summary>
        public static string VolumeParameter(AudioBus bus) => VolumeParams[(int)bus];
        public static string SnapshotName(AudioSnapshotKind kind) => Snapshots[(int)kind];

        /// <summary>Resources/AudioRoutingSettings (null + one warning if missing).</summary>
        public static AudioRoutingSettings Settings
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    settings = Resources.Load<AudioRoutingSettings>(AudioRoutingSettings.ResourcePath);
                    if (settings == null)
                        Debug.LogWarning("[Audio] Resources/AudioRoutingSettings not found: sounds play unrouted. Run TWR > Audio > Build Audio (A1).");
                }
                return settings;
            }
        }

        public static AudioMixer Mixer => Settings != null ? Settings.mixer : null;

        /// <summary>
        /// The bus's state group. If TWR_Mixer has no such group yet, the parent user group (e.g. "SFX") instead, with ONE
        /// warning per session listing every missing state group: the volume sliders still work, the snapshots and the
        /// voice ducking don't (D-219, D-222). Null if neither exists.
        /// </summary>
        public static AudioMixerGroup GetGroup(AudioBus bus)
        {
            AudioRoutingSettings routing = Settings;
            if (routing == null) return null;
            AudioMixerGroup state = routing.GetStateGroup(bus);
            if (state != null) return state;
            WarnMissingStateGroupsOnce(routing);
            return routing.GetUserGroup(bus);
        }

        static void WarnMissingStateGroupsOnce(AudioRoutingSettings routing)
        {
            if (warnedMissingStateGroups || routing.mixer == null) return;
            warnedMissingStateGroups = true;
            string missing = "", parents = "";
            for (int i = 0; i < BusCount; i++)
            {
                if (routing.GetStateGroup((AudioBus)i) != null) continue;
                missing += (missing.Length > 0 ? ", " : "") + StateGroups[i];
                parents += (parents.Length > 0 ? ", " : "") + UserGroups[i];
            }
            Debug.LogWarning($"[Audio] TWR_Mixer has no state group(s) {missing}: those sounds play on {parents} instead, so the " +
                             "volume sliders work but snapshots (silent rooms, endings) and voice ducking don't. Add them by hand; " +
                             "TWR > Audio > Validate Audio (A1) lists the steps (D-219, D-222).");
        }

        /// <summary>Send this source to the bus's state group (or its parent group while the state group is missing).</summary>
        public static void Route(AudioSource source, AudioBus bus)
        {
            if (source == null) return;
            AudioMixerGroup group = GetGroup(bus);
            if (group != null) source.outputAudioMixerGroup = group;
        }

        /// <summary>Safety net for hand-added sources: route only if nothing is assigned yet.</summary>
        public static void RouteIfUnassigned(AudioSource source, AudioBus bus)
        {
            if (source != null && source.outputAudioMixerGroup == null) Route(source, bus);
        }

        /// <summary>The group with exactly this name in the mixer, or null. A one-time lookup, never per frame.</summary>
        public static AudioMixerGroup FindGroup(AudioMixer mixer, string groupName)
        {
            if (mixer == null || string.IsNullOrEmpty(groupName)) return null;
            AudioMixerGroup[] matches = mixer.FindMatchingGroups(groupName);
            if (matches == null) return null;
            for (int i = 0; i < matches.Length; i++)
                if (matches[i] != null && matches[i].name == groupName) return matches[i];
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            settings = null;
            loaded = false;
            warnedMissingStateGroups = false;
        }
    }
}
