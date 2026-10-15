using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>The boss sound slots (D-078). Not every boss uses every slot.</summary>
    public enum BossSound
    {
        Telegraph,
        FastTelegraph,
        Charge,
        Impact,
        Stomp,
        Hurt,
        Death,
        TeleportTelegraph,
        TeleportArrive,
        Volley
    }

    /// <summary>
    /// A boss's own sound effects (D-078, D-138, D-185: placeholder sounds are fine). Every slot may be empty; with
    /// usePlaceholdersWhenEmpty on, BossAudio fills empty slots with generated tones (PlaceholderBossSounds), so the
    /// telegraphs are audible from day one. The fast-charge telegraph is a different, higher roar (D-182).
    /// Replace with real clips later (D-083). Create via: Create > TWR > Bosses > Boss Sound Set.
    /// </summary>
    [CreateAssetMenu(fileName = "BossSounds_", menuName = "TWR/Bosses/Boss Sound Set")]
    public class BossSoundSet : ScriptableObject
    {
        [Tooltip("Looping breath/drone while alive.")]
        public AudioClip idleLoop;
        [Tooltip("Bull: normal charge telegraph (paw + roar).")]
        public AudioClip telegraph;
        [Tooltip("Bull: FAST charge telegraph, must sound different (D-182).")]
        public AudioClip fastTelegraph;
        [Tooltip("Bull: the rush starts.")]
        public AudioClip charge;
        [Tooltip("Bull: crash into a wall or column (stun).")]
        public AudioClip impact;
        [Tooltip("Bull: stomp lands.")]
        public AudioClip stomp;
        public AudioClip hurt;
        public AudioClip death;
        [Tooltip("Preacher: plays AT THE DESTINATION during the teleport telegraph (D-184).")]
        public AudioClip teleportTelegraph;
        [Tooltip("Preacher: appears.")]
        public AudioClip teleportArrive;
        [Tooltip("Preacher: each projectile of a volley.")]
        public AudioClip volley;

        [Header("Volumes")]
        [Range(0f, 1f)] public float idleVolume = 0.4f;
        [Range(0f, 1f)] public float effectsVolume = 1f;

        [Tooltip("Use generated tones for empty slots (test builds). Turn off once real clips exist.")]
        public bool usePlaceholdersWhenEmpty = true;

        /// <summary>The authored clip for a slot (may be null).</summary>
        public AudioClip Get(BossSound sound)
        {
            switch (sound)
            {
                case BossSound.Telegraph: return telegraph;
                case BossSound.FastTelegraph: return fastTelegraph;
                case BossSound.Charge: return charge;
                case BossSound.Impact: return impact;
                case BossSound.Stomp: return stomp;
                case BossSound.Hurt: return hurt;
                case BossSound.Death: return death;
                case BossSound.TeleportTelegraph: return teleportTelegraph;
                case BossSound.TeleportArrive: return teleportArrive;
                case BossSound.Volley: return volley;
                default: return null;
            }
        }
    }
}
