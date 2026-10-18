using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWR.Save
{
    /// <summary>
    /// What the one local save file holds (D-099, D-201, D-212), as JSON: a format version, the ids of every ending
    /// found (EndingData.id) and, since version 2, the player's settings (D-226; for now the five sound volumes, D-225).
    /// Ids the game doesn't know (e.g. from a newer version) are kept, never dropped. When the format changes: bump
    /// CurrentVersion and migrate in SaveFile.Load. Meta only: nothing in here may give a gameplay advantage (D-039).
    /// </summary>
    [Serializable]
    public class SaveData
    {
        /// <summary>1 = endings only (R5). 2 = + settings (A1, D-226).</summary>
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;
        public List<string> foundEndings = new List<string>();
        public SettingsData settings = new SettingsData();
    }

    /// <summary>
    /// The player's settings in the save file (D-090, D-226). Sound volumes are stored 0..1 (the sliders show 0..100,
    /// D-225); 1 = full volume (0 dB). Missing values (older files) are the defaults.
    /// </summary>
    [Serializable]
    public class SettingsData
    {
        [Range(0f, 1f)] public float musicVolume = 1f;
        [Range(0f, 1f)] public float ambienceVolume = 1f;
        [Range(0f, 1f)] public float sfxVolume = 1f;
        [Range(0f, 1f)] public float voiceVolume = 1f;
        [Range(0f, 1f)] public float uiVolume = 1f;

        public SettingsData Clone() => (SettingsData)MemberwiseClone();

        /// <summary>Clamp to 0..1; broken values (NaN) become the default 1.</summary>
        public void Sanitize()
        {
            musicVolume = Clean(musicVolume);
            ambienceVolume = Clean(ambienceVolume);
            sfxVolume = Clean(sfxVolume);
            voiceVolume = Clean(voiceVolume);
            uiVolume = Clean(uiVolume);
        }

        static float Clean(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp01(value);
    }
}
