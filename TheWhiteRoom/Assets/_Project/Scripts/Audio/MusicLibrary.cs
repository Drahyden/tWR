using UnityEngine;

namespace TWR.Audio
{
    /// <summary>
    /// Every MusicData the game uses plus the director-wide music settings (D-230 to D-235). Lives in Resources
    /// (Data/Resources/MusicLibrary) so the persistent MusicDirector, which is created by code at startup, can load it in
    /// every scene (MainMenu, Core, endings) without scene references, like AudioLibrary.Shared. All music clips are
    /// Streaming (D-228), so loading this asset loads no audio data until a track plays.
    /// Ending stingers are NOT here: each EndingData holds its own clip (D-234).
    /// Filled by TWR > Audio > Build Music (A2); your times are kept.
    /// </summary>
    [CreateAssetMenu(fileName = "MusicLibrary", menuName = "TWR/Audio/Music Library")]
    public class MusicLibrary : ScriptableObject
    {
        public const string ResourcePath = "MusicLibrary";

        [Header("Tracks per state - D-075, D-213, D-230")]
        [Tooltip("Hub lobby (all stems) and the main menu (menu stem only), D-230.")]
        public MusicData lobby;
        public MusicData exploration;
        public MusicData puzzle;
        public MusicData combat;
        [Tooltip("One per boss, matched by MusicData.bossId = BossData.bossId (D-213).")]
        public MusicData[] bosses = new MusicData[0];

        [Header("Where the states come from")]
        [Tooltip("Scene name of the main menu (copied from the RunConfig by the builder, D-202).")]
        public string mainMenuSceneName = "MainMenu";
        [Tooltip("Section id the RunManager raises for the hub lobby (VoiceEvents.SectionStarted).")]
        public string lobbySectionId = "lobby";

        [Header("Endings - D-234")]
        [Tooltip("The ending stinger fades in this fast once the ending scene is ready (screen still white).")]
        [Min(0f)] public float endingFadeInSeconds = 0.3f;
        [Tooltip("How fast the looping music fades out when an ending starts.")]
        [Min(0f)] public float endingFadeOutSeconds = 1.5f;
        [Range(0f, 1f)] public float endingVolume = 0.9f;
        [Tooltip("Debug only: played when 'Ending' is forced in the F1 menu outside an ending.")]
        public AudioClip debugEndingClip;

        [Header("Playback")]
        [Tooltip("Stems are scheduled this far ahead (AudioSource.PlayScheduled) so streaming clips all start on the same sample.")]
        [Range(0.05f, 0.5f)] public float scheduleLeadSeconds = 0.2f;
        [Tooltip("Fade used when a silent room starts/ends if SoundscapeState gives no time.")]
        [Min(0f)] public float silenceFadeSeconds = 1.5f;

        static MusicLibrary shared;
        static bool loaded;

        /// <summary>Resources/MusicLibrary (null + one warning if missing).</summary>
        public static MusicLibrary Shared
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    shared = Resources.Load<MusicLibrary>(ResourcePath);
                    if (shared == null)
                        Debug.LogWarning("[Audio] Resources/MusicLibrary not found: no music. Run TWR > Audio > Build Music (A2).");
                }
                return shared;
            }
        }

        /// <summary>The boss track for this boss id, else the first boss track (with a warning), else null.</summary>
        public MusicData FindBoss(string bossId)
        {
            if (bosses == null) return null;
            for (int i = 0; i < bosses.Length; i++)
                if (bosses[i] != null && bosses[i].bossId == bossId) return bosses[i];
            for (int i = 0; i < bosses.Length; i++)
                if (bosses[i] != null)
                {
                    Debug.LogWarning($"[Audio] No boss music for '{bossId}': using {bosses[i].DisplayName}. Run TWR > Audio > Build Music (A2).", this);
                    return bosses[i];
                }
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            shared = null;
            loaded = false;
        }
    }
}
