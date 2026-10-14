namespace TWR.Run
{
    /// <summary>
    /// Small deterministic random number generator for run plans (D-153, D-159). Same seed + same stream
    /// = same numbers on every PC and Unity version (unlike UnityEngine.Random, which is global and shared).
    /// "stream" gives independent sequences from one seed: the plan uses stream 0 for the pre-fork sections,
    /// 1 for the Escape branch and 2 for the Discover branch, so picking a door never changes the other numbers.
    /// Algorithm: SplitMix64 (tiny, fast, well distributed).
    /// </summary>
    public class SeededRandom
    {
        ulong state;

        public SeededRandom(uint seed, uint stream)
        {
            // Seed in the high bits, stream mixed in with a large odd constant: streams never overlap for nearby seeds.
            state = ((ulong)seed << 32) ^ ((ulong)stream * 0xD1B54A32D192ED03UL);
        }

        public ulong NextULong()
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Integer in [minInclusive, maxExclusive). Returns minInclusive if the range is empty.</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            ulong span = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextULong() % span);
        }
    }
}
