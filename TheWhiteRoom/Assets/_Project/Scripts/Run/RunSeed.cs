using System;
using System.Globalization;

namespace TWR.Run
{
    /// <summary>
    /// Seed helpers (D-159): seeds are 32-bit unsigned, shown as 8 hex characters; 0 = the curated first run.
    /// Parse accepts hex ("0x1A2B", "#1A2B", anything with A-F, or exactly 8 characters like the display)
    /// or plain decimal ("12345"). Only the debug overlay enters seeds (D-153).
    /// </summary>
    public static class RunSeed
    {
        public const uint Curated = 0;

        static readonly Random random = new Random();

        public static string Format(uint seed) => seed.ToString("X8", CultureInfo.InvariantCulture);

        /// <summary>A random non-zero seed (never the curated 0).</summary>
        public static uint NewRandom()
        {
            uint seed;
            do seed = ((uint)random.Next(1 << 16) << 16) | (uint)random.Next(1 << 16);
            while (seed == Curated);
            return seed;
        }

        public static bool TryParse(string text, out uint seed)
        {
            seed = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();

            bool hex = false;
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { t = t.Substring(2); hex = true; }
            else if (t.StartsWith("#")) { t = t.Substring(1); hex = true; }
            if (t.Length == 0) return false;

            if (!hex)
            {
                if (t.Length == 8) hex = true; // same format as the overlay shows
                else
                    foreach (char c in t)
                        if ((c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')) { hex = true; break; }
            }

            return hex
                ? uint.TryParse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out seed)
                : uint.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out seed);
        }
    }
}
