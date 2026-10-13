using UnityEngine;
using UnityEngine.Localization;

namespace TWR.Puzzles
{
    /// <summary>How a door panel moves when opened (D-127).</summary>
    public enum DoorOpenStyle
    {
        /// <summary>Panel slides sideways along the door's local X (right) axis.</summary>
        Slide,
        /// <summary>Panel turns around its own local Y axis. Put the panel's pivot on the hinge side.</summary>
        Swing
    }

    /// <summary>
    /// Tunable values shared by doors (D-127, D-128): how the panel opens, how long it takes,
    /// the localized prompts (D-084: door.locked / door.open in table 'UI') and the Mechanism noise radii
    /// for unlocking (~8 m) and opening (~10 m). Where a door LEADS is not in here on purpose: destinations
    /// come later (D-053b, D-058). Create via: Project window > right-click > Create > TWR > Door Data.
    /// </summary>
    [CreateAssetMenu(fileName = "Door_", menuName = "TWR/Door Data")]
    public class DoorData : ScriptableObject
    {
        [Header("Opening - D-127")]
        public DoorOpenStyle openStyle = DoorOpenStyle.Slide;
        [Tooltip("Slide: how far the panel moves along the door's local X axis (m). Negative = other way.")]
        public float slideDistance = 1.6f;
        [Tooltip("Swing: how far the panel turns around its local Y axis (degrees). Negative = other way.")]
        public float swingAngle = 95f;
        [Tooltip("Seconds from closed to fully open.")]
        [Min(0.05f)] public float openSeconds = 1.1f;

        [Header("Prompts - D-084, D-112")]
        public LocalizedString lockedPrompt = new LocalizedString("UI", "door.locked");
        public LocalizedString openPrompt = new LocalizedString("UI", "door.open");

        [Header("Noise radii (m) - D-128 (D-079, D-114)")]
        [Tooltip("Mechanism noise at the door when it unlocks.")]
        [Min(0f)] public float unlockNoiseRadius = 8f;
        [Tooltip("Mechanism noise at the door when it starts opening. Credited to the player who opened it.")]
        [Min(0f)] public float openNoiseRadius = 10f;

        [Header("Placeholder audio (optional, can be empty)")]
        public AudioClip lockedClip;
        public AudioClip unlockClip;
        public AudioClip openClip;
    }
}
