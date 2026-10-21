using UnityEngine;

namespace TWR.Level
{
    /// <summary>
    /// Tunable values for one kind of moving platform (D-241): top speed, how gently it starts and stops, how long it
    /// waits at each end, and which bodies make it wait instead of pushing them. The builder makes two assets in
    /// Data/Level: MovingPlatform_Horizontal (2 m/s) and MovingPlatform_Lift (1.5 m/s, longer wait at the bottom).
    /// </summary>
    [CreateAssetMenu(menuName = "TWR/Level/Moving Platform Data", fileName = "MovingPlatform_New")]
    public class MovingPlatformData : ScriptableObject
    {
        [Tooltip("Top speed along the path (m/s). D-241: 2.0 horizontal, 1.5 lifts.")]
        [Min(0.1f)] public float speed = 2f;
        [Tooltip("Speeding up and slowing down near the ends (m/s²). Gentle, so riders never get thrown.")]
        [Min(0.1f)] public float acceleration = 2f;
        [Tooltip("How hard it brakes when something is in its way (m/s²). Sets how far ahead it looks.")]
        [Min(0.5f)] public float brakeDeceleration = 4f;
        [Tooltip("Slowest speed while still travelling, so it always reaches the end (m/s).")]
        [Min(0.01f)] public float minSpeed = 0.1f;
        [Tooltip("Wait at the start point A (lifts: the bottom), seconds.")]
        [Min(0f)] public float pauseAtA = 1.5f;
        [Tooltip("Wait at the end point B, seconds.")]
        [Min(0f)] public float pauseAtB = 1.5f;
        [Tooltip("Extra look-ahead past the braking distance (m).")]
        [Min(0f)] public float clearanceMargin = 0.1f;
        [Tooltip("Bodies it never pushes or crushes: it waits while one is in its path (riders on top don't count).")]
        public LayerMask blockers;
    }
}
