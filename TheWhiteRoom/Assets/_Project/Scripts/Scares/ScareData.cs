using UnityEngine;
using TWR.Audio;
using TWR.Run;

namespace TWR.Scares
{
    /// <summary>What makes a scare hit (D-236).</summary>
    public enum ScareTrigger
    {
        /// <summary>"Relief": after the section's main puzzle is solved, the player is near the exit door and facing it.</summary>
        FacingExitAfterSolve,
        /// <summary>"Behind you": after crossing a portal, a spot that was unobserved for a while is seen up close (D-053a).</summary>
        SeenAfterUnobserved,
    }

    /// <summary>
    /// One jump scare (D-236 to D-239): which template it belongs to, what triggers it, which stinger it plays (an AudioLibrary
    /// ID, swappable per scare), the music / ambience dip and the light timings. The rules shared by all scares (once per
    /// run, 120 s apart, held during combat) live in ScareSettings. Placed in a section scene by a ScareAnchor; played by
    /// the ScareDirector in Core. Created by TWR > Run > Build Run Scenes (Data/Scares); your edits are kept.
    /// </summary>
    [CreateAssetMenu(fileName = "Scare_", menuName = "TWR/Scares/Scare Data")]
    public class ScareData : ScriptableObject
    {
        [Header("Scare - D-236")]
        [Tooltip("Unique id for logs, ScareEvents.Fired and the F1 menu, e.g. 'scare.relief'.")]
        public string id;
        public ScareTrigger trigger = ScareTrigger.FacingExitAfterSolve;
        [Tooltip("The section template this scare lives in. Never Silent, Fork or BossArena (D-238).")]
        public SectionType template = SectionType.Puzzle;

        [Header("Stinger - D-081, D-237")]
        [Tooltip("An AudioLibrary ID (TWR.Audio.AudioIds). Swap the sound here or in the AudioLibrary.")]
        public string stingerId = AudioIds.StingerViolin;
        [Range(0f, 1f)] public float stingerVolume = 1f;

        [Header("Music / ambience dip - D-237")]
        [Tooltip("Music level at the deepest point. -80 = silence, 0 = no dip.")]
        [Range(-80f, 0f)] public float musicDipDb = -12f;
        [Tooltip("Ambient bed (hum) level at the deepest point. -80 = silence, 0 = no dip.")]
        [Range(-80f, 0f)] public float ambienceDipDb = 0f;
        [Min(0f)] public float dipAttackSeconds = 0.05f;
        [Tooltip("How long the dip stays down AFTER the hit (the pre-hit darkness is added automatically).")]
        [Min(0f)] public float dipHoldSeconds = 0.3f;
        [Tooltip("Recovery time back to normal (D-237: about 2 s).")]
        [Min(0f)] public float dipReleaseSeconds = 2f;

        [Header("Lights and figure - D-236, D-238")]
        [Tooltip("Lights out before the hit (the stinger hits when they come back). 0 = no darkness before the hit.")]
        [Min(0f)] public float preHitDarkSeconds = 0f;
        [Tooltip("The figure stays visible this long after the hit, then a flicker removes it. Ignored if 'Remove Figure When Unobserved'.")]
        [Min(0f)] public float figureVisibleSeconds = 0.3f;
        [Tooltip("Length of the flicker that removes the figure. 0 = it just vanishes.")]
        [Min(0f)] public float flickerDarkSeconds = 0.12f;
        [Tooltip("The figure stays until the player stops looking at it (D-053a), instead of a flicker.")]
        public bool removeFigureWhenUnobserved;
        [Tooltip("If the player walks this close to the figure it vanishes behind a short flicker (0 = never).")]
        [Min(0f)] public float vanishIfCloserThan = 1.5f;

        [Header("Trigger - D-236")]
        [Tooltip("Relief: player within this distance of the exit door. Behind you: the spot is seen within this distance.")]
        [Min(0.5f)] public float triggerDistance = 5f;
        [Tooltip("Relief: full angle of the view cone for 'facing the door' (shared ViewCone, D-132). 'Seen' for Behind you " +
                 "means on screen with a clear line of sight.")]
        [Range(10f, 120f)] public float viewConeDegrees = 60f;
        [Tooltip("Behind you: the spot must be unobserved this long before the figure appears there.")]
        [Min(0f)] public float unobservedSeconds = 3f;
        [Tooltip("Relief: how far in front of the player the figure stands (between player and door).")]
        [Min(0.5f)] public float figureDistance = 3f;

        /// <summary>Id, or the asset name if the id is empty.</summary>
        public string DisplayId => string.IsNullOrEmpty(id) ? name : id;

        /// <summary>Dark periods the scare makes (pre-hit darkness, removal flicker). The proximity flicker is extra.</summary>
        public int FlashCount => (preHitDarkSeconds > 0f ? 1 : 0) + (RemovalFlicker ? 1 : 0);

        /// <summary>Is the figure removed by a flicker (not by looking away)?</summary>
        public bool RemovalFlicker => !removeFigureWhenUnobserved && flickerDarkSeconds > 0f;

        /// <summary>Time from the first dark moment to the last light one (D-238: about 1 s at most).</summary>
        public float FlickerSeconds => preHitDarkSeconds + (RemovalFlicker ? figureVisibleSeconds + flickerDarkSeconds : 0f);

        /// <summary>Start-to-start time between the two dark periods (D-238: at most 3 flashes per second), or +inf.</summary>
        public float FlashInterval => FlashCount >= 2 ? preHitDarkSeconds + figureVisibleSeconds : float.PositiveInfinity;

        /// <summary>D-238: scares never live in silent rooms, the fork or boss arenas.</summary>
        public static bool TemplateAllowed(SectionType type) =>
            type != SectionType.Silent && type != SectionType.Fork && type != SectionType.BossArena;

        void OnValidate()
        {
            if (!TemplateAllowed(template))
                Debug.LogWarning($"[TWR] Scare '{name}' is set to the {template} template; D-238 forbids scares there.", this);
        }
    }
}
