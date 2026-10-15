using TWR.Run;

namespace TWR.Enemies
{
    /// <summary>
    /// Decides which Bull charge is a FAST charge (D-182, D-189). Plain C# (no Unity calls), so the editor determinism
    /// check runs exactly this code.
    /// Rules: charges and stomps both count as attacks. A target count between min and max (inclusive, D-189: 2-5) is
    /// drawn from the boss stream; once that many attacks have happened, the NEXT charge is fast. The fast charge resets
    /// the counter (it does not count towards the next target) and a new target is drawn. Without a stream (seed 0 /
    /// sandbox) the authored sequence is used in order and repeats (e.g. 3, 4, 2, 5).
    /// ForceNext (F1 debug, D-185) makes the next charge fast WITHOUT touching the count or the stream, so a seed's
    /// sequence stays the same.
    /// </summary>
    public sealed class FastChargeSchedule
    {
        readonly SeededRandom stream;
        readonly int min, max;
        readonly int[] authored;
        int authoredIndex;

        /// <summary>Attacks (charges + stomps) since the last natural fast charge.</summary>
        public int AttacksSinceFast { get; private set; }
        /// <summary>How many attacks must happen before the next charge is fast.</summary>
        public int Target { get; private set; }
        /// <summary>Natural fast charges so far.</summary>
        public int FastCharges { get; private set; }
        /// <summary>Debug: make the next charge fast once, without changing the count or the stream.</summary>
        public bool ForceNext { get; set; }
        /// <summary>True when the counts come from the seed; false = authored sequence.</summary>
        public bool UsesStream => stream != null;

        /// <param name="stream">The D-189 boss stream, or null for the authored sequence (seed 0, sandbox).</param>
        public FastChargeSchedule(SeededRandom stream, int min, int max, int[] authoredCounts)
        {
            this.stream = stream;
            this.min = min < 1 ? 1 : min;
            this.max = max < this.min ? this.min : max;
            authored = authoredCounts;
            Target = DrawTarget();
        }

        /// <summary>Would the next charge be fast?</summary>
        public bool NextChargeIsFast => ForceNext || AttacksSinceFast >= Target;

        /// <summary>A charge starts: returns true if it is a fast charge and updates the counters.</summary>
        public bool BeginCharge()
        {
            if (AttacksSinceFast >= Target)
            {
                AttacksSinceFast = 0;
                FastCharges++;
                Target = DrawTarget(); // re-rolled after each fast charge (D-189)
                ForceNext = false;
                return true;
            }
            if (ForceNext)
            {
                ForceNext = false;
                AttacksSinceFast++; // a forced fast charge still counts as a normal attack for the schedule
                return true;
            }
            AttacksSinceFast++;
            return false;
        }

        /// <summary>A stomp happened (counts as an attack, D-189).</summary>
        public void RegisterStomp() => AttacksSinceFast++;

        int DrawTarget()
        {
            if (stream != null) return stream.Range(min, max + 1);
            if (authored != null && authored.Length > 0)
            {
                int value = authored[authoredIndex % authored.Length];
                authoredIndex++;
                return value < min ? min : value > max ? max : value;
            }
            return (min + max) / 2;
        }
    }
}
