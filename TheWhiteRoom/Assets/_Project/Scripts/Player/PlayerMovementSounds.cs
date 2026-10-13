using UnityEngine;
using TWR.Core;

namespace TWR.Player
{
    /// <summary>
    /// Player footstep, jump and landing sounds + matching noise events (D-079, D-043, D-052).
    /// A footstep fires every "stride" metres walked (works like a timer that speeds up when you run);
    /// the audio hook and the noise always fire at the same moment. Crouch is quiet, sprint is loud.
    /// Clip arrays may be empty for now: noises still fire, you just hear nothing.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class PlayerMovementSounds : MonoBehaviour
    {
        [SerializeField] PlayerTuning tuning;
        [SerializeField] PlayerLocomotion locomotion;
        [Header("Placeholder audio (optional, can be empty)")]
        [SerializeField] AudioClip[] footstepClips;
        [SerializeField] AudioClip[] jumpClips;
        [SerializeField] AudioClip[] landClips;

        AudioSource audioSource;
        float strideProgress;

        /// <summary>Raised on every footstep with the noise radius used. Hook for camera effects etc.</summary>
        public event System.Action<float> FootstepTaken;

        void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        }

        void OnEnable()
        {
            if (locomotion == null) return;
            locomotion.Jumped += HandleJumped;
            locomotion.Landed += HandleLanded;
        }

        void OnDisable()
        {
            if (locomotion == null) return;
            locomotion.Jumped -= HandleJumped;
            locomotion.Landed -= HandleLanded;
        }

        void Update()
        {
            if (tuning == null || locomotion == null) return;

            if (!locomotion.IsGrounded || locomotion.HorizontalSpeed < 0.1f)
            {
                // Standing still: the next step happens half a stride after starting to move.
                strideProgress = Mathf.Min(strideProgress, 0.5f);
                return;
            }

            float stride = locomotion.IsCrouching ? tuning.crouchStride : locomotion.IsSprinting ? tuning.sprintStride : tuning.walkStride;
            strideProgress += locomotion.HorizontalSpeed * Time.deltaTime / Mathf.Max(0.05f, stride);
            if (strideProgress >= 1f)
            {
                strideProgress -= 1f;
                TakeFootstep();
            }
        }

        void TakeFootstep()
        {
            float radius, volume;
            if (locomotion.IsCrouching)      { radius = tuning.crouchStepNoise;  volume = tuning.crouchStepVolume; }
            else if (locomotion.IsSprinting) { radius = tuning.sprintStepNoise;  volume = tuning.sprintStepVolume; }
            else                        { radius = tuning.walkStepNoise;    volume = tuning.walkStepVolume; }

            PlayRandom(footstepClips, volume);
            NoiseSystem.Emit(transform.position, radius, NoiseType.Footstep, gameObject);
            FootstepTaken?.Invoke(radius);
        }

        void HandleJumped()
        {
            if (tuning == null) return;
            PlayRandom(jumpClips, 1f);
            NoiseSystem.Emit(transform.position, tuning.jumpNoise, NoiseType.Jump, gameObject);
        }

        void HandleLanded(float fallSpeed)
        {
            if (tuning == null || fallSpeed < tuning.landMinSpeed) return;

            float radius = Mathf.Min(tuning.landNoiseBase + fallSpeed * tuning.landNoisePerSpeed, tuning.landNoiseMax);
            if (locomotion.IsCrouching) radius *= 0.5f; // landing crouched is softer
            PlayRandom(landClips, Mathf.Clamp01(fallSpeed / 15f + 0.3f));
            NoiseSystem.Emit(transform.position, radius, NoiseType.Land, gameObject);
            strideProgress = 0f;
        }

        void PlayRandom(AudioClip[] clips, float volume)
        {
            if (clips == null || clips.Length == 0 || audioSource == null) return;
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip != null) audioSource.PlayOneShot(clip, volume);
        }
    }
}
