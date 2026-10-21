using UnityEngine;

namespace TWR.Scares
{
    /// <summary>
    /// The rules every jump scare shares (D-238) and the list of scares (D-239), used by the ScareDirector in Core, the
    /// F1 menu and the [TWR] validation. Created by TWR > Run > Build Run Scenes (Data/Scares/ScareSettings); your edits
    /// are kept.
    /// </summary>
    [CreateAssetMenu(fileName = "ScareSettings", menuName = "TWR/Scares/Scare Settings")]
    public class ScareSettings : ScriptableObject
    {
        [Tooltip("Every scare in the game (D-236). The F1 menu shows one 'Fire' button per entry.")]
        public ScareData[] scares = new ScareData[0];

        [Header("Rules - D-238")]
        [Tooltip("Run time (pause excluded) between two scares; a later scare waits.")]
        [Min(0f)] public float minSecondsBetweenScares = 120f;
        [Tooltip("Non-priority voice lines are held this long after a hit.")]
        [Min(0f)] public float voiceHoldSeconds = 3f;
        [Tooltip("Flicker safety, checked by the [TWR] validation: at most this many flashes per second ...")]
        [Range(1f, 10f)] public float maxFlashesPerSecond = 3f;
        [Tooltip("... and about this long of flicker in total.")]
        [Min(0f)] public float maxFlickerSeconds = 1.2f;
        [Tooltip("Length of the extra flicker when the player walks into a figure.")]
        [Min(0f)] public float proximityFlickerSeconds = 0.12f;

        [Header("Checks")]
        [Tooltip("How often triggers and figures are checked (seconds). ~10 Hz is plenty (D-100).")]
        [Min(0.02f)] public float checkInterval = 0.1f;
        [Tooltip("What blocks the player's view of a figure (walls, props). Actor layers are always removed.")]
        public LayerMask sightBlockers = 1;
        [Tooltip("Chest height of the figure, used for 'is it seen'.")]
        [Min(0.2f)] public float figureChestHeight = 1.4f;

        [Header("Figure + debug - D-239")]
        [Tooltip("The Seeker-look prop (no AI, no collider, not counted in RunStats). Used for the F1 debug fire.")]
        public GameObject figurePrefab;
        [Tooltip("F1 'Fire' places the figure this far in front of the player.")]
        [Min(1f)] public float debugFigureDistance = 4f;
    }
}
