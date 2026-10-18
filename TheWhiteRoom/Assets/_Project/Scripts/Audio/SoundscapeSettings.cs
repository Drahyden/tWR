using UnityEngine;

namespace TWR.Audio
{
    /// <summary>
    /// Tunable fade times for silent rooms (D-168, D-082): how fast the Silent snapshot fades music and the ambient
    /// bed out on entry, and how fast they come back after leaving (D-220). Create via: Project window > right-click > Create > TWR > Soundscape Settings.
    /// </summary>
    [CreateAssetMenu(fileName = "SoundscapeSettings", menuName = "TWR/Soundscape Settings")]
    public class SoundscapeSettings : ScriptableObject
    {
        [Tooltip("Seconds for the hum (and later music) to fade to silence when a silent room loads.")]
        [Min(0f)] public float silenceFadeOutSeconds = 1.5f;
        [Tooltip("Seconds for the hum to come back after leaving a silent room.")]
        [Min(0f)] public float restoreFadeInSeconds = 1f;
    }
}
