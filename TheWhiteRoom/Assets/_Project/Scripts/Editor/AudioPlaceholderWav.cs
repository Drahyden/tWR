using System;
using System.IO;
using UnityEngine;

namespace TWR.EditorTools
{
    /// <summary>
    /// Writes the A1 placeholder sounds as real .wav files (16-bit mono PCM) into Assets/_Project/Audio/Placeholder (D-223,
    /// D-227): fluorescent hum loop, distant phone ring, printer, keyboard clicks and a UI click. Simple synthesized tones and
    /// noise, made with a fixed random seed, so every run of the builder writes the same sound. A file that already exists
    /// is never overwritten (re-runnable, no reimport churn). They are own work and listed in docs/audio/LICENSES.md (D-218).
    /// To use a real sound, don't overwrite these: drop the new clip into the AudioLibrary asset instead.
    /// </summary>
    static class AudioPlaceholderWav
    {
        const int SampleRate = 44100;

        public struct Placeholder
        {
            public string fileName;
            public string description;
            public Func<float[]> make;
        }

        public const string HumFile = "amb_hum_loop_placeholder.wav";
        public const string PhoneFile = "amb_office_phone_placeholder.wav";
        public const string PrinterFile = "amb_office_printer_placeholder.wav";
        public const string KeyboardFile = "amb_office_keyboard_placeholder.wav";
        public const string UiClickFile = "ui_click_placeholder.wav";

        public static readonly Placeholder[] All =
        {
            new Placeholder { fileName = HumFile, description = "fluorescent hum loop (2 s)", make = Hum },
            new Placeholder { fileName = PhoneFile, description = "distant phone ring", make = Phone },
            new Placeholder { fileName = PrinterFile, description = "distant printer", make = Printer },
            new Placeholder { fileName = KeyboardFile, description = "distant keyboard clicks", make = Keyboard },
            new Placeholder { fileName = UiClickFile, description = "UI click", make = UiClick },
        };

        /// <summary>Write the file if it doesn't exist yet. True if a new file was written.</summary>
        public static bool WriteIfMissing(string assetFolder, Placeholder placeholder)
        {
            string fullPath = Path.GetFullPath(Path.Combine(assetFolder, placeholder.fileName));
            if (File.Exists(fullPath)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            WriteWav(fullPath, placeholder.make(), SampleRate);
            return true;
        }

        // ---------- the sounds ----------

        /// 2 s of mains hum: 60 Hz + buzzy harmonics, whole cycles only, so the loop is seamless.
        static float[] Hum()
        {
            int count = SampleRate * 2;
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float buzz = 0.26f * Sin(60f, t) + 0.32f * Sin(120f, t) + 0.12f * Sin(180f, t) + 0.08f * Sin(240f, t)
                           + 0.04f * Sin(360f, t) + 0.02f * Sin(480f, t);
                float flutter = 1f + 0.06f * Sin(1f, t); // a slow 1 Hz wobble (2 whole cycles in 2 s)
                data[i] = buzz * flutter * 0.7f;
            }
            return data;
        }

        /// Two old-fashioned rings (two tones, 20 Hz warble), with a soft start and end.
        static float[] Phone()
        {
            float seconds = 2.6f;
            int count = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                bool on = (t > 0.05f && t < 0.45f) || (t > 0.65f && t < 1.05f) || (t > 1.85f && t < 2.25f);
                if (!on) continue;
                float warble = 0.5f + 0.5f * Sin(20f, t);
                float tone = 0.45f * Sin(400f, t) + 0.35f * Sin(450f, t);
                data[i] = tone * warble * 0.6f;
            }
            Smooth(data, 0.25f);
            return Fade(data, 0.01f, 0.05f);
        }

        /// A printer: a rhythmic mechanical chatter (filtered noise at 11 Hz) over a low motor whine.
        static float[] Printer()
        {
            var random = new System.Random(4242);
            float seconds = 3f;
            int count = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[count];
            float low = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.08f;
                float chatterPhase = (t * 11f) % 1f;
                float chatter = Mathf.Exp(-chatterPhase * 9f);
                float motor = 0.18f * Sin(95f, t) + 0.08f * Sin(190f, t);
                float body = t < 0.25f ? t / 0.25f : (t > seconds - 0.4f ? (seconds - t) / 0.4f : 1f);
                data[i] = (low * 1.6f * chatter + motor) * body * 0.7f;
            }
            return Fade(data, 0.02f, 0.1f);
        }

        /// Ten to fourteen short keyboard clicks at uneven gaps.
        static float[] Keyboard()
        {
            var random = new System.Random(777);
            float seconds = 2f;
            int count = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[count];
            float time = 0.05f;
            while (time < seconds - 0.1f)
            {
                int start = Mathf.FloorToInt(time * SampleRate);
                int length = Mathf.FloorToInt(0.018f * SampleRate);
                float pitch = 1800f + (float)random.NextDouble() * 900f;
                float level = 0.35f + (float)random.NextDouble() * 0.3f;
                for (int k = 0; k < length && start + k < count; k++)
                {
                    float kt = k / (float)SampleRate;
                    float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                    data[start + k] += (noise * 0.6f + Sin(pitch, kt) * 0.4f) * level * Mathf.Exp(-kt * 260f);
                }
                time += 0.07f + (float)random.NextDouble() * 0.18f;
            }
            Smooth(data, 0.5f);
            return Fade(data, 0.005f, 0.05f);
        }

        /// 40 ms tick: a short 2 kHz blip with a fast decay.
        static float[] UiClick()
        {
            int count = Mathf.CeilToInt(0.04f * SampleRate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                data[i] = (0.6f * Sin(2000f, t) + 0.25f * Sin(3200f, t)) * Mathf.Exp(-t * 140f);
            }
            return Fade(data, 0.001f, 0.005f);
        }

        // ---------- helpers ----------

        static float Sin(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);

        static void Smooth(float[] data, float amount)
        {
            float value = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                value += (data[i] - value) * amount;
                data[i] = value;
            }
        }

        static float[] Fade(float[] data, float inSeconds, float outSeconds)
        {
            int fadeIn = Mathf.Max(1, Mathf.FloorToInt(inSeconds * SampleRate));
            int fadeOut = Mathf.Max(1, Mathf.FloorToInt(outSeconds * SampleRate));
            for (int i = 0; i < data.Length && i < fadeIn; i++) data[i] *= i / (float)fadeIn;
            for (int i = 0; i < fadeOut && i < data.Length; i++) data[data.Length - 1 - i] *= i / (float)fadeOut;
            return data;
        }

        /// 16-bit PCM mono RIFF/WAVE file.
        static void WriteWav(string fullPath, float[] samples, int sampleRate)
        {
            using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                const short channels = 1, bitsPerSample = 16;
                int byteRate = sampleRate * channels * bitsPerSample / 8;
                int dataBytes = samples.Length * channels * bitsPerSample / 8;

                writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                writer.Write(36 + dataBytes);
                writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(16);                      // fmt chunk size
                writer.Write((short)1);                // PCM
                writer.Write(channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write((short)(channels * bitsPerSample / 8));
                writer.Write(bitsPerSample);
                writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                writer.Write(dataBytes);
                for (int i = 0; i < samples.Length; i++)
                    writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(samples[i], -1f, 1f) * 32000f));
            }
        }
    }
}
