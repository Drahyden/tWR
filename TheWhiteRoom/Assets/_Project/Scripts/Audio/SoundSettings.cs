using UnityEngine;
using UnityEngine.Audio;
using TWR.Save;

namespace TWR.Audio
{
    /// <summary>
    /// The five sound volumes (D-225): Music, Ambience, Sound effects, Voice, Interface. Stored 0..1 (sliders show 0..100),
    /// default 1. Each one sets its user group's exposed mixer parameter (MusicVol, ...) in decibels:
    /// dB = 20 * log10(max(v, 0.0001)), so 1 = 0 dB and 0 = -80 dB. Only these settings touch the exposed parameters;
    /// snapshots never do (D-219, D-220). Set applies live; SaveIfChanged writes the D-212 save file (D-226).
    /// Loaded from the save file on first use. AudioMixer.SetFloat is ignored during Awake, so the audio bootstrap
    /// calls ApplyAll in Start.
    /// </summary>
    public static class SoundSettings
    {
        public const float MinLinear = 0.0001f; // -80 dB

        static readonly float[] volumes = { 1f, 1f, 1f, 1f, 1f };
        static bool loaded, dirty, warnedMissingParameter;

        public static float Get(AudioBus bus)
        {
            EnsureLoaded();
            return volumes[(int)bus];
        }

        /// <summary>Set one volume (0..1) and hear it at once. Saved later by SaveIfChanged.</summary>
        public static void Set(AudioBus bus, float value)
        {
            EnsureLoaded();
            value = Mathf.Clamp01(value);
            int index = (int)bus;
            if (Mathf.Approximately(volumes[index], value)) return;
            volumes[index] = value;
            dirty = true;
            ApplyBus(bus);
        }

        /// <summary>Push all five volumes into the mixer (after Awake: the bootstrap's Start).</summary>
        public static void ApplyAll()
        {
            EnsureLoaded();
            for (int i = 0; i < AudioRouting.BusCount; i++) ApplyBus((AudioBus)i);
        }

        /// <summary>Write the volumes to the save file if they changed since the last save (D-225: when leaving the screen).</summary>
        public static bool SaveIfChanged()
        {
            if (!loaded || !dirty) return true;
            dirty = false;
            var data = new SettingsData
            {
                musicVolume = volumes[(int)AudioBus.Music],
                ambienceVolume = volumes[(int)AudioBus.Ambience],
                sfxVolume = volumes[(int)AudioBus.SFX],
                voiceVolume = volumes[(int)AudioBus.Voice],
                uiVolume = volumes[(int)AudioBus.UI],
            };
            bool ok = SettingsSave.Save(data);
            if (ok) Debug.Log("[Audio] Sound settings saved (D-225, D-226).");
            return ok;
        }

        /// <summary>0..1 -> decibels for the mixer: 20 * log10(max(v, 0.0001)). 1 = 0 dB, 0 = -80 dB.</summary>
        public static float ToDecibels(float linear) => 20f * Mathf.Log10(Mathf.Max(linear, MinLinear));

        static void ApplyBus(AudioBus bus)
        {
            AudioMixer mixer = AudioRouting.Mixer;
            if (mixer == null) return;
            string parameter = AudioRouting.VolumeParameter(bus);
            if (!mixer.SetFloat(parameter, ToDecibels(volumes[(int)bus])) && !warnedMissingParameter)
            {
                warnedMissingParameter = true;
                Debug.LogWarning($"[Audio] TWR_Mixer has no exposed parameter '{parameter}' (or it was set during Awake). " +
                                 "Expose the Volume of each user group (MusicVol, AmbienceVol, SFXVol, VoiceVol, UIVol).");
            }
        }

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            SettingsData data = SettingsSave.Load();
            volumes[(int)AudioBus.Music] = data.musicVolume;
            volumes[(int)AudioBus.Ambience] = data.ambienceVolume;
            volumes[(int)AudioBus.SFX] = data.sfxVolume;
            volumes[(int)AudioBus.Voice] = data.voiceVolume;
            volumes[(int)AudioBus.UI] = data.uiVolume;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            for (int i = 0; i < volumes.Length; i++) volumes[i] = 1f;
            loaded = false;
            dirty = false;
            warnedMissingParameter = false;
        }
    }
}
