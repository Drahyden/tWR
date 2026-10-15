using System.Collections.Generic;

namespace TWR.Run
{
    /// <summary>
    /// Small deterministic random number generator for run plans and section rolls (D-153, D-159, D-172). Same seed +
    /// same stream = same numbers on every PC and Unity version (unlike UnityEngine.Random, which is global and shared).
    /// "stream" gives independent sequences from one seed: the plan uses stream 0 for the pre-fork sections,
    /// 1 for the Escape branch and 2 for the Discover branch, so picking a door never changes the other numbers.
    /// Section rolls (R3, D-172) use their own streams from 0x100 up (SectionRollContext.StreamFor), so they never
    /// touch the plan streams and existing seeds keep their plans.
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

        /// <summary>
        /// Picks pickCount different indices out of 0..candidateCount-1 (without replacement, D-176) and appends them to
        /// result in the order they were drawn (that order is meaningful, e.g. a Sequence puzzle's switch order, D-173).
        /// If pickCount is larger than candidateCount, every index is returned (shuffled). Partial Fisher-Yates.
        /// </summary>
        public void PickIndices(int candidateCount, int pickCount, List<int> result)
        {
            if (candidateCount <= 0 || pickCount <= 0) return;
            if (pickCount > candidateCount) pickCount = candidateCount;
            var indices = new int[candidateCount]; // section load only, never per frame
            for (int i = 0; i < candidateCount; i++) indices[i] = i;
            for (int i = 0; i < pickCount; i++)
            {
                int j = Range(i, candidateCount);
                int swap = indices[i];
                indices[i] = indices[j];
                indices[j] = swap;
                result.Add(indices[i]);
            }
        }
    }
}
