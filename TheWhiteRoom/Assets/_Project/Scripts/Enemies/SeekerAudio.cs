using UnityEngine;
using UnityEngine.AI;

namespace TWR.Enemies
{
    /// <summary>
    /// Plays the Seeker's own sounds (D-078, D-138) from its SeekerSoundSet through one 3D AudioSource:
    /// an idle loop, footsteps spaced by distance walked, and one-shots for alert, attack wind-up, hurt and death.
    /// Seeker footsteps are ONLY audio for the player's ears; they are never NoiseSystem events, so enemies don't
    /// hear each other. Empty slots stay silent, or use generated placeholders if the sound set allows it.
    /// The source goes to the SFXState mixer group (D-219).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class SeekerAudio : MonoBehaviour
    {
        [SerializeField] SeekerSoundSet sounds;
        [SerializeField] SeekerData data;
        [SerializeField] NavMeshAgent agent;

        AudioSource source;
        AudioClip idleClip, alertClip, windUpClip, hurtClip, deathClip, placeholderStep;
        float strideProgress;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            TWR.Audio.AudioRouting.RouteIfUnassigned(source, TWR.Audio.AudioBus.SFX); // enemies -> SFXState (D-219)
            if (agent == null) agent = GetComponent<NavMeshAgent>();

            bool hasSet = sounds != null;
            if (hasSet)
            {
                idleClip = sounds.idleLoop;
                alertClip = sounds.alert;
                windUpClip = sounds.attackWindUp;
                hurtClip = sounds.hurt;
                deathClip = sounds.death;
            }

            // Generated placeholders only for slots that are still empty (made once, shared by all Seekers).
            if (hasSet && !sounds.usePlaceholdersWhenEmpty) return;
            if (idleClip == null) idleClip = PlaceholderSeekerSounds.Idle;
            if (alertClip == null) alertClip = PlaceholderSeekerSounds.Alert;
            if (windUpClip == null) windUpClip = PlaceholderSeekerSounds.WindUp;
            if (hurtClip == null) hurtClip = PlaceholderSeekerSounds.Hurt;
            if (deathClip == null) deathClip = PlaceholderSeekerSounds.Death;
            bool hasSteps = hasSet && sounds.footsteps != null && sounds.footsteps.Length > 0;
            if (!hasSteps) placeholderStep = PlaceholderSeekerSounds.Footstep;
        }

        void Start()
        {
            if (idleClip == null || source == null) return;
            source.clip = idleClip;
            source.loop = true;
            source.volume = sounds != null ? sounds.idleVolume : 0.35f;
            source.Play();
        }

        void Update()
        {
            if (agent == null || !agent.enabled || data == null) return;

            float speed = agent.velocity.magnitude;
            if (speed < 0.2f) return;
            strideProgress += speed * Time.deltaTime / Mathf.Max(0.1f, data.footstepStride);
            if (strideProgress < 1f) return;
            strideProgress -= 1f;
            PlayFootstep(); // 3D sound only, NO NoiseSystem.Emit here (D-138)
        }

        void PlayFootstep()
        {
            AudioClip clip = placeholderStep;
            if (sounds != null && sounds.footsteps != null && sounds.footsteps.Length > 0)
                clip = sounds.footsteps[Random.Range(0, sounds.footsteps.Length)];
            Play(clip, sounds != null ? sounds.footstepVolume : 0.5f);
        }

        public void PlayAlert() => Play(alertClip, EffectsVolume);
        public void PlayWindUp() => Play(windUpClip, EffectsVolume);
        public void PlayHurt() => Play(hurtClip, EffectsVolume);
        /// <summary>D-243: take-off and landing of a leap between platforms (a heavier footstep). Audio only, no noise event.</summary>
        public void PlayLeap()
        {
            AudioClip clip = placeholderStep;
            if (sounds != null && sounds.footsteps != null && sounds.footsteps.Length > 0)
                clip = sounds.footsteps[Random.Range(0, sounds.footsteps.Length)];
            Play(clip, EffectsVolume);
        }

        /// <summary>Stops the idle loop and plays the death sound. Footsteps stop because the agent is off.</summary>
        public void PlayDeath()
        {
            if (source != null && source.isPlaying && source.loop) source.Stop();
            Play(deathClip, EffectsVolume);
        }

        float EffectsVolume => sounds != null ? sounds.effectsVolume : 1f;

        void Play(AudioClip clip, float volume)
        {
            if (clip != null && source != null) source.PlayOneShot(clip, volume);
        }
    }
}
