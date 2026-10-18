using System;
using UnityEngine;

namespace TWR.Audio
{
    /// <summary>The kinds of space a reverb zone can be (D-224). Both sides of a portal pair use the same one.</summary>
    public enum ReverbSpace
    {
        SmallRoom = 0,
        MediumRoom = 1,
        LargeHall = 2,
        BossArena = 3,
    }

    /// <summary>
    /// Reverb values per kind of space (D-224, D-077: high reverb overall; bigger in halls and boss arenas, less in small
    /// rooms). Each preset is either one of Unity's built-in AudioReverbPresets or custom values (decay time, room level,
    /// reflections, ...). ReverbZoneSetup reads this asset when its scene loads, so tuning here needs no rebuild.
    /// Made by TWR > Audio > Build Audio (A1) at Data/Audio/ReverbPresets. "Reset" in the asset's menu restores the defaults.
    /// </summary>
    [CreateAssetMenu(fileName = "ReverbPresets", menuName = "TWR/Audio/Reverb Presets")]
    public class ReverbPresets : ScriptableObject
    {
        [Serializable]
        public class Preset
        {
            public ReverbSpace space;
            [Tooltip("Off = use Unity's preset below. On = use the custom values.")]
            public bool useCustomValues = true;
            public AudioReverbPreset unityPreset = AudioReverbPreset.Room;

            [Header("Custom values (Unity reverb units: levels in mB, 100 mB = 1 dB)")]
            [Tooltip("Overall room effect level.")]
            [Range(-10000, 0)] public int room = -1000;
            [Tooltip("High-frequency room level (more negative = darker).")]
            [Range(-10000, 0)] public int roomHF = -300;
            [Tooltip("Reverb tail length in seconds.")]
            [Range(0.1f, 20f)] public float decayTime = 2f;
            [Range(0.1f, 2f)] public float decayHFRatio = 0.8f;
            [Tooltip("Early reflections level.")]
            [Range(-10000, 1000)] public int reflections = -1000;
            [Range(0f, 0.3f)] public float reflectionsDelay = 0.02f;
            [Tooltip("Late reverb level.")]
            [Range(-10000, 2000)] public int reverb = 0;
            [Range(0f, 0.1f)] public float reverbDelay = 0.03f;
            [Range(0f, 100f)] public float diffusion = 100f;
            [Range(0f, 100f)] public float density = 100f;

            [Header("Zone shape")]
            [Tooltip("Metres between full effect (inside) and no effect (outside), added around the room.")]
            [Min(0f)] public float fadeMargin = 3f;
        }

        public Preset[] presets = DefaultPresets();

        /// <summary>The preset for this space (null if none is listed).</summary>
        public Preset Get(ReverbSpace space)
        {
            if (presets == null) return null;
            for (int i = 0; i < presets.Length; i++)
                if (presets[i] != null && presets[i].space == space) return presets[i];
            return null;
        }

        /// <summary>Write a preset into a reverb zone (distances are set by the builder from the room size).</summary>
        public static void Apply(AudioReverbZone zone, Preset preset)
        {
            if (zone == null || preset == null) return;
            if (!preset.useCustomValues)
            {
                zone.reverbPreset = preset.unityPreset;
                return;
            }
            zone.reverbPreset = AudioReverbPreset.User; // custom values only stick on the User preset
            zone.room = preset.room;
            zone.roomHF = preset.roomHF;
            zone.decayTime = preset.decayTime;
            zone.decayHFRatio = preset.decayHFRatio;
            zone.reflections = preset.reflections;
            zone.reflectionsDelay = preset.reflectionsDelay;
            zone.reverb = preset.reverb;
            zone.reverbDelay = preset.reverbDelay;
            zone.diffusion = preset.diffusion;
            zone.density = preset.density;
        }

        void Reset() => presets = DefaultPresets();

        /// D-077 "high reverb": even small rooms ring a little; the halls and arenas have long tails.
        public static Preset[] DefaultPresets() => new[]
        {
            new Preset { space = ReverbSpace.SmallRoom, room = -1000, roomHF = -450, decayTime = 1.4f, decayHFRatio = 0.8f, reflections = -900, reflectionsDelay = 0.008f, reverb = -200, reverbDelay = 0.012f, fadeMargin = 2f },
            new Preset { space = ReverbSpace.MediumRoom, room = -1000, roomHF = -350, decayTime = 2.4f, decayHFRatio = 0.8f, reflections = -1100, reflectionsDelay = 0.015f, reverb = 0, reverbDelay = 0.02f, fadeMargin = 3f },
            new Preset { space = ReverbSpace.LargeHall, room = -1000, roomHF = -300, decayTime = 4.2f, decayHFRatio = 0.75f, reflections = -1300, reflectionsDelay = 0.025f, reverb = 200, reverbDelay = 0.035f, fadeMargin = 4f },
            new Preset { space = ReverbSpace.BossArena, room = -900, roomHF = -350, decayTime = 6.5f, decayHFRatio = 0.7f, reflections = -1500, reflectionsDelay = 0.04f, reverb = 300, reverbDelay = 0.05f, fadeMargin = 6f },
        };
    }
}
