using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>
    /// TEST PLACEHOLDERS (D-138): builds six tiny synthetic clips (drone, step thud, alert screech,
    /// wind-up hiss, hurt squeal, death groan) the first time a Seeker needs them, then shares them between all
    /// Seekers for the session. No asset files, no licences. SeekerAudio only uses them for EMPTY slots in the
    /// SeekerSoundSet, so real clips (D-078, D-083) replace them without code changes.
    /// </summary>
    public static class PlaceholderSeekerSounds
    {
        const int SampleRate = 22050;

        static AudioClip idle, footstep, alert, windUp, hurt, death;
        static System.Random random;

        public static AudioClip Idle => idle != null ? idle : (idle = Make("Placeholder Seeker Idle", 2f, IdleSample));
        public static AudioClip Footstep => footstep != null ? footstep : (footstep = Make("Placeholder Seeker Step", 0.09f, StepSample));
        public static AudioClip Alert => alert != null ? alert : (alert = Make("Placeholder Seeker Alert", 0.5f, AlertSample));
        public static AudioClip WindUp => windUp != null ? windUp : (windUp = Make("Placeholder Seeker WindUp", 0.4f, WindUpSample));
        public static AudioClip Hurt => hurt != null ? hurt : (hurt = Make("Placeholder Seeker Hurt", 0.2f, HurtSample));
        public static AudioClip Death => death != null ? death : (death = Make("Placeholder Seeker Death", 0.9f, DeathSample));

        delegate float SampleFunction(float t, float length);

        // Made once per clip, so the float[] here is a one-time cost, not per frame.
        static AudioClip Make(string name, float seconds, SampleFunction sample)
        {
            if (random == null) random = new System.Random(1234);
            int count = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[count];
            float lowPass = 0f;
            for (int i = 0; i < count; i++)
            {
                float value = sample(i / (float)SampleRate, seconds);
                lowPass += (value - lowPass) * 0.35f; // soften the harsh edges a little
                data[i] = Mathf.Clamp(lowPass, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Noise() => (float)(random.NextDouble() * 2.0 - 1.0);
        static float Sine(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);

        // Whole numbers of cycles in 2 s, so the loop has no click.
        static float IdleSample(float t, float length) =>
            (Sine(55f, t) * 0.6f + Sine(82f, t) * 0.4f) * (0.18f + 0.1f * Sine(0.5f, t)) + Noise() * 0.02f;

        static float StepSample(float t, float length) => Noise() * 0.7f * Mathf.Exp(-t * 45f);

        static float AlertSample(float t, float length)
        {
            float k = t / length;
            float hz = Mathf.Lerp(300f, 900f, k);
            return (Sine(hz, t) * 0.5f + Noise() * 0.25f) * Mathf.Sin(Mathf.PI * k);
        }

        static float WindUpSample(float t, float length)
        {
            float k = t / length;
            return (Noise() * 0.45f + Sine(120f, t) * 0.3f) * k;
        }

        static float HurtSample(float t, float length)
        {
            float k = t / length;
            return Sine(Mathf.Lerp(700f, 400f, k), t) * 0.5f * (1f - k);
        }

        static float DeathSample(float t, float length)
        {
            float k = t / length;
            return (Sine(Mathf.Lerp(200f, 60f, k), t) * 0.5f + Noise() * 0.2f) * (1f - k);
        }
    }
}
