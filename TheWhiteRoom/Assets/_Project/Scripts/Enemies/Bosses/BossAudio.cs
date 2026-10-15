using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>
    /// Plays a boss's sounds (D-078, D-138) from its BossSoundSet through one 3D AudioSource on the boss: an idle loop and
    /// one-shots (telegraphs, charge, impact, stomp, hurt, death, teleport, volley). PlayAt plays on ANOTHER source, e.g.
    /// the Preacher's destination telegraph (D-184: the sound comes from where it will appear). Empty slots use the
    /// generated placeholders if the set allows it. Boss sounds are audio only, never NoiseSystem events (D-138).
    /// Clips are looked up once in Awake; playing a sound allocates nothing (D-100).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class BossAudio : MonoBehaviour
    {
        [SerializeField] BossSoundSet sounds;

        static readonly int SoundCount = System.Enum.GetValues(typeof(BossSound)).Length;

        AudioSource source;
        AudioClip idleClip;
        AudioClip[] clips;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            bool placeholders = sounds == null || sounds.usePlaceholdersWhenEmpty;
            clips = new AudioClip[SoundCount];
            for (int i = 0; i < SoundCount; i++)
            {
                AudioClip clip = sounds != null ? sounds.Get((BossSound)i) : null;
                if (clip == null && placeholders) clip = PlaceholderBossSounds.Get((BossSound)i);
                clips[i] = clip;
            }
            idleClip = sounds != null ? sounds.idleLoop : null;
            if (idleClip == null && placeholders) idleClip = PlaceholderBossSounds.Idle;
        }

        void Start()
        {
            if (idleClip == null || source == null) return;
            source.clip = idleClip;
            source.loop = true;
            source.volume = sounds != null ? sounds.idleVolume : 0.4f;
            source.Play();
        }

        float EffectsVolume => sounds != null ? sounds.effectsVolume : 1f;

        /// <summary>One-shot on the boss itself.</summary>
        public void Play(BossSound sound)
        {
            AudioClip clip = clips != null ? clips[(int)sound] : null;
            if (clip != null && source != null) source.PlayOneShot(clip, EffectsVolume);
        }

        /// <summary>One-shot on another 3D source (e.g. at the teleport destination). Pitch lets one clip sound different.</summary>
        public void PlayAt(AudioSource other, BossSound sound, float pitch = 1f)
        {
            AudioClip clip = clips != null ? clips[(int)sound] : null;
            if (clip == null || other == null) return;
            other.pitch = pitch;
            other.PlayOneShot(clip, EffectsVolume);
        }

        /// <summary>Stops the idle loop (death).</summary>
        public void StopIdle()
        {
            if (source != null && source.loop && source.isPlaying) source.Stop();
        }
    }
}
