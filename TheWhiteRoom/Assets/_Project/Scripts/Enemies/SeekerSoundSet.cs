using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>
    /// The Seeker's own sound effects (D-078, D-138). Every slot may be empty; SeekerAudio checks.
    /// When a slot is empty and usePlaceholdersWhenEmpty is on, a tiny generated placeholder tone is used
    /// instead (made once per session, no asset files), so the wind-up telegraph is audible from day one.
    /// Replace with real clips later (D-083). Create via: Create > TWR > Seeker Sound Set.
    /// </summary>
    [CreateAssetMenu(fileName = "SeekerSounds_", menuName = "TWR/Seeker Sound Set")]
    public class SeekerSoundSet : ScriptableObject
    {
        [Tooltip("Looping breath/drone while alive.")]
        public AudioClip idleLoop;
        [Tooltip("One picked at random per step. 3D audio only: Seeker steps are NOT noise events (D-138).")]
        public AudioClip[] footsteps = new AudioClip[0];
        [Tooltip("When it starts chasing.")]
        public AudioClip alert;
        [Tooltip("Melee telegraph, plays at the start of the wind-up.")]
        public AudioClip attackWindUp;
        public AudioClip hurt;
        public AudioClip death;

        [Header("Volumes")]
        [Range(0f, 1f)] public float idleVolume = 0.35f;
        [Range(0f, 1f)] public float footstepVolume = 0.5f;
        [Range(0f, 1f)] public float effectsVolume = 1f;

        [Tooltip("Use simple generated tones for empty slots (test builds). Turn off once real clips exist.")]
        public bool usePlaceholdersWhenEmpty = true;
    }
}
