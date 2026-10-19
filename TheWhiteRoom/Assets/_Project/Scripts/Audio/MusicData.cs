using System;
using UnityEngine;

namespace TWR.Audio
{
    /// <summary>
    /// One looping music track and its stems (D-214, D-231, D-232, D-235). Every FADR stem file of the track
    /// (mus_&lt;state&gt;_a_&lt;stem&gt;) is one layer; there is no fixed set (bass, drums, guitar, piano, other, vocals ...).
    /// All stems always play together in sync; a layer is "off" when its volume is faded to 0 (D-231).
    /// Which layers are on is decided by the state's rule in MusicDirector (D-232) from these values:
    /// - onAtEntry: on as soon as the state starts (Exploration / Puzzle / Boss: everything but drums; Combat / Lobby: all).
    /// - drumsStem: the stem that Puzzle brings in after drumsDelaySeconds, Boss on the first hit, and Combat fades out
    ///   first when no enemy chases any more (drumsOutDelaySeconds later the state goes back to Exploration).
    /// - Boss phase 2 turns every stem on (D-213), so a stem with onAtEntry off that is not the drums enters at phase 2.
    /// - menuStem: the main menu plays the Lobby track with only this stem (D-230).
    /// Created and kept up to date by TWR > Audio > Build Music (A2), which finds the stems by file name; your ticks and
    /// times are kept. Create by hand via: Create > TWR > Audio > Music Data.
    /// </summary>
    [CreateAssetMenu(fileName = "MusicData_", menuName = "TWR/Audio/Music Data")]
    public class MusicData : ScriptableObject
    {
        /// <summary>Most stems one track can have (the director makes this many AudioSources per bank).</summary>
        public const int MaxStems = 8;

        [Serializable]
        public class Stem
        {
            [Tooltip("Stem name from the file name, e.g. 'drums' for mus_combat_a_drums.ogg.")]
            public string name;
            public AudioClip clip;
            [Tooltip("On as soon as the state starts (D-232). Off = enters later (drums by the state rule, others at boss phase 2).")]
            public bool onAtEntry = true;
        }

        [Header("Identity - D-235")]
        [Tooltip("Which state plays this track.")]
        public MusicStateKind state = MusicStateKind.Exploration;
        [Tooltip("File prefix of the track, e.g. 'mus_combat_a' (for logs and the debug overlay).")]
        public string trackName;
        [Tooltip("Boss tracks only: the BossData.bossId this track belongs to, e.g. 'boss.bull'.")]
        public string bossId;

        [Header("Stems (filled by Build Music (A2)) - D-231")]
        public Stem[] stems = new Stem[0];
        [Tooltip("Name of the drums stem (D-232).")]
        public string drumsStem = "drums";
        [Tooltip("Lobby track only: the one stem the main menu plays (D-230).")]
        public string menuStem = "other";

        [Header("Mix and fades (seconds, unscaled) - D-214, D-231")]
        [Range(0f, 1f)] public float baseVolume = 0.8f;
        [Tooltip("How long one layer takes to fade in or out (D-231: about 1.5 s).")]
        [Min(0f)] public float layerFadeSeconds = 1.5f;
        [Tooltip("Crossfade into this track from another state (D-214: about 2 s).")]
        [Min(0f)] public float crossfadeSeconds = 2f;

        [Header("State rules - D-232")]
        [Tooltip("Puzzle: the drums fade in after this many seconds unsolved (counted only while Puzzle is playing).")]
        [Min(0f)] public float drumsDelaySeconds = 30f;
        [Tooltip("Combat: after the last chaser drops, the drums fade out; this many seconds later the state goes back.")]
        [Min(0f)] public float drumsOutDelaySeconds = 2.5f;

        public int StemCount => stems != null ? Mathf.Min(stems.Length, MaxStems) : 0;

        public bool IsDrums(int index) =>
            stems != null && index >= 0 && index < stems.Length && stems[index] != null && stems[index].name == drumsStem;

        public bool IsMenuStem(int index) =>
            stems != null && index >= 0 && index < stems.Length && stems[index] != null && stems[index].name == menuStem;

        public bool OnAtEntry(int index) =>
            stems != null && index >= 0 && index < stems.Length && stems[index] != null && stems[index].onAtEntry;

        public string StemName(int index) =>
            stems != null && index >= 0 && index < stems.Length && stems[index] != null ? stems[index].name : "?";

        public AudioClip StemClip(int index) =>
            stems != null && index >= 0 && index < stems.Length && stems[index] != null ? stems[index].clip : null;

        public string DisplayName => string.IsNullOrEmpty(trackName) ? name : trackName;
    }
}
