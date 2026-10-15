using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>
    /// TEST PLACEHOLDERS (D-138, D-185): tiny synthetic boss clips, built the first time a boss needs them and shared for
    /// the session, like PlaceholderSeekerSounds. No asset files, no licences. BossAudio only uses them for EMPTY slots
    /// of a BossSoundSet. The fast-charge roar is higher and harsher than the normal one, so the two telegraphs are
    /// easy to tell apart by ear (D-182).
    /// </summary>
    public static class PlaceholderBossSounds
    {
        const int SampleRate = 22050;

        static AudioClip idle, telegraph, fastTelegraph, charge, impact, stomp, hurt, death, teleportTelegraph, teleportArrive, volley;
        static System.Random random;

        public static AudioClip Idle => idle != null ? idle : (idle = Make("Placeholder Boss Idle", 2f, IdleSample));

        /// <summary>The generated clip for a slot (made once).</summary>
        public static AudioClip Get(BossSound sound)
        {
            switch (sound)
            {
                case BossSound.Telegraph: return telegraph != null ? telegraph : (telegraph = Make("Placeholder Boss Roar", 1f, RoarSample));
                case BossSound.FastTelegraph: return fastTelegraph != null ? fastTelegraph : (fastTelegraph = Make("Placeholder Boss Fast Roar", 1f, FastRoarSample));
                case BossSound.Charge: return charge != null ? charge : (charge = Make("Placeholder Boss Charge", 0.6f, ChargeSample));
                case BossSound.Impact: return impact != null ? impact : (impact = Make("Placeholder Boss Impact", 0.8f, ImpactSample));
                case BossSound.Stomp: return stomp != null ? stomp : (stomp = Make("Placeholder Boss Stomp", 0.6f, StompSample));
                case BossSound.Hurt: return hurt != null ? hurt : (hurt = Make("Placeholder Boss Hurt", 0.25f, HurtSample));
                case BossSound.Death: return death != null ? death : (death = Make("Placeholder Boss Death", 1.4f, DeathSample));
                case BossSound.TeleportTelegraph: return teleportTelegraph != null ? teleportTelegraph : (teleportTelegraph = Make("Placeholder Boss Teleport Hum", 1f, TeleportHumSample));
                case BossSound.TeleportArrive: return teleportArrive != null ? teleportArrive : (teleportArrive = Make("Placeholder Boss Arrive", 0.35f, ArriveSample));
                case BossSound.Volley: return volley != null ? volley : (volley = Make("Placeholder Boss Volley", 0.2f, VolleySample));
                default: return null;
            }
        }

        delegate float SampleFunction(float t, float length);

        // Made once per clip, so the float[] is a one-time cost, never per frame.
        static AudioClip Make(string name, float seconds, SampleFunction sample)
        {
            if (random == null) random = new System.Random(4321);
            int count = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[count];
            float lowPass = 0f;
            for (int i = 0; i < count; i++)
            {
                float value = sample(i / (float)SampleRate, seconds);
                lowPass += (value - lowPass) * 0.35f;
                data[i] = Mathf.Clamp(lowPass, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Noise() => (float)(random.NextDouble() * 2.0 - 1.0);
        static float Sine(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);

        static float IdleSample(float t, float length) =>
            (Sine(40f, t) * 0.6f + Sine(61f, t) * 0.3f) * (0.2f + 0.1f * Sine(0.5f, t)) + Noise() * 0.03f;

        static float RoarSample(float t, float length)
        {
            float k = t / length;
            return (Noise() * 0.5f + Sine(Mathf.Lerp(70f, 110f, k), t) * 0.5f) * Mathf.Sin(Mathf.PI * k);
        }

        static float FastRoarSample(float t, float length)
        {
            float k = t / length;
            float pulse = Sine(9f, t) > 0f ? 1f : 0.4f; // stuttering, alarm-like
            return (Noise() * 0.4f + Sine(Mathf.Lerp(220f, 420f, k), t) * 0.6f) * Mathf.Sin(Mathf.PI * k) * pulse;
        }

        static float ChargeSample(float t, float length)
        {
            float k = t / length;
            return Noise() * 0.6f * (1f - k) + Sine(55f, t) * 0.3f * (1f - k);
        }

        static float ImpactSample(float t, float length) => (Noise() * 0.8f + Sine(45f, t) * 0.6f) * Mathf.Exp(-t * 6f);

        static float StompSample(float t, float length) => (Sine(38f, t) * 0.9f + Noise() * 0.4f) * Mathf.Exp(-t * 8f);

        static float HurtSample(float t, float length)
        {
            float k = t / length;
            return Sine(Mathf.Lerp(260f, 150f, k), t) * 0.5f * (1f - k);
        }

        static float DeathSample(float t, float length)
        {
            float k = t / length;
            return (Sine(Mathf.Lerp(150f, 35f, k), t) * 0.6f + Noise() * 0.25f) * (1f - k);
        }

        static float TeleportHumSample(float t, float length)
        {
            float k = t / length;
            float flutter = Sine(14f, t) > 0f ? 1f : 0.2f; // flickers with the visual telegraph
            return (Sine(Mathf.Lerp(300f, 600f, k), t) * 0.4f + Sine(903f, t) * 0.15f) * flutter * Mathf.Sin(Mathf.PI * k);
        }

        static float ArriveSample(float t, float length) => (Noise() * 0.5f + Sine(180f, t) * 0.4f) * Mathf.Exp(-t * 9f);

        static float VolleySample(float t, float length)
        {
            float k = t / length;
            return (Noise() * 0.35f + Sine(Mathf.Lerp(900f, 500f, k), t) * 0.3f) * (1f - k);
        }
    }
}
