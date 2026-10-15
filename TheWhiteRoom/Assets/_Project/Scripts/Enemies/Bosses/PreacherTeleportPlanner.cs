using System.Collections.Generic;
using TWR.Run;

namespace TWR.Enemies
{
    /// <summary>
    /// The Preacher's random choices from the boss stream (D-184, D-187, D-189), as plain C# so the editor determinism
    /// check runs the same code. The brain hands over the fair candidate cavities (already filtered by distance, height
    /// and view cone, in stable-id order); this class only draws from the stream:
    /// - Phase 2: with behindChance percent it picks among the "behind the player" cavities (if any exist).
    /// - Otherwise it picks among the normal candidates.
    /// - Volley size: between min and max, inclusive.
    /// Same stream + same candidate lists in the same order = same choices.
    /// </summary>
    public static class PreacherTeleportPlanner
    {
        /// <summary>
        /// Returns an index from candidates or behind (the chosen cavity), or -1 if both lists are empty.
        /// pickedBehind tells which list it came from.
        /// </summary>
        public static int Choose(SeededRandom stream, List<int> candidates, List<int> behind, bool phaseTwo, int behindChancePercent,
                                 out bool pickedBehind)
        {
            pickedBehind = false;
            if (phaseTwo && behind != null && behind.Count > 0)
            {
                // Always draw the chance first in phase 2, so the stream advances the same way whatever the lists hold.
                bool tryBehind = stream.Range(0, 100) < behindChancePercent;
                if (tryBehind || candidates == null || candidates.Count == 0)
                {
                    pickedBehind = true;
                    return behind[stream.Range(0, behind.Count)];
                }
            }
            if (candidates == null || candidates.Count == 0) return -1;
            return candidates[stream.Range(0, candidates.Count)];
        }

        /// <summary>Projectiles in the next volley (D-192: 3-5).</summary>
        public static int VolleySize(SeededRandom stream, int min, int max)
        {
            if (max < min) max = min;
            return stream.Range(min, max + 1);
        }
    }
}
