using System.Globalization;
using TWR.Stats;

namespace TWR.Run
{
    /// <summary>
    /// The frozen snapshot of a finished run (D-204): path, kills / enemies spawned, optional puzzles solved /
    /// available, score and Good/Poor (D-152, Good at 60+), run time (D-206) and seed (D-153). Captured once when the
    /// player crosses the boss arena exit; ending scenes never change it. The ending is chosen from it (D-203) and the
    /// end-of-run screen shows it (D-200). Plain C#, immutable, so it can be tested and passed around safely.
    /// </summary>
    public sealed class RunResult
    {
        public RunPath Path { get; }
        public int Kills { get; }
        public int EnemiesSpawned { get; }
        public int OptionalSolved { get; }
        public int OptionalAvailable { get; }
        public float Score { get; }
        public bool IsGood { get; }
        /// <summary>Unpaused play time from the lobby run door to the boss arena exit, seconds (D-206).</summary>
        public float RunTimeSeconds { get; }
        public uint Seed { get; }
        public bool IsCurated { get; }
        /// <summary>Made up by the F1 debug overlay (D-203), not played.</summary>
        public bool IsDebug { get; }

        /// <summary>The kill half of the score, 0-50 (end screen breakdown, D-200).</summary>
        public float KillPoints => RunStats.KillPoints(Kills, EnemiesSpawned);
        /// <summary>The optional-puzzle half of the score, 0-50.</summary>
        public float PuzzlePoints => RunStats.PuzzlePoints(OptionalSolved, OptionalAvailable);

        public RunResult(RunPath path, int kills, int enemiesSpawned, int optionalSolved, int optionalAvailable,
                         float runTimeSeconds, uint seed, bool isCurated, bool isDebug)
        {
            Path = path;
            Kills = kills;
            EnemiesSpawned = enemiesSpawned;
            OptionalSolved = optionalSolved;
            OptionalAvailable = optionalAvailable;
            Score = RunStats.ComputeScore(kills, enemiesSpawned, optionalSolved, optionalAvailable);
            IsGood = Score >= RunStats.GoodScoreThreshold;
            RunTimeSeconds = runTimeSeconds < 0f ? 0f : runTimeSeconds;
            Seed = seed;
            IsCurated = isCurated;
            IsDebug = isDebug;
        }

        /// <summary>Freeze the live RunStats into a result (D-204).</summary>
        public static RunResult Capture(RunPath path, RunStats stats, float runTimeSeconds, uint seed, bool isCurated)
        {
            if (stats == null) return new RunResult(path, 0, 0, 0, 0, runTimeSeconds, seed, isCurated, false);
            return new RunResult(path, stats.PlayerKills, stats.EnemiesRegistered, stats.OptionalPuzzlesSolved,
                                 stats.OptionalPuzzlesTotal, runTimeSeconds, seed, isCurated, false);
        }

        /// <summary>
        /// A believable fake result for the F1 "trigger ending" buttons (D-203): Good = 3/4 kills + 1/1 optional
        /// (87.5, or 4/4 for 100 if minScore asks for more), Poor = 1/4 kills + 0/1 optional (12.5).
        /// </summary>
        public static RunResult CreateDebug(RunPath path, bool good, float minScore, float runTimeSeconds, uint seed, bool isCurated)
        {
            if (!good) return new RunResult(path, 1, 4, 0, 1, runTimeSeconds, seed, isCurated, true);
            int kills = minScore > 87.5f ? 4 : 3;
            return new RunResult(path, kills, 4, 1, 1, runTimeSeconds, seed, isCurated, true);
        }

        /// <summary>D-206 display: m:ss, or h:mm:ss from one hour on. Allocates; end screen only.</summary>
        public static string FormatTime(float seconds)
        {
            int total = seconds <= 0f ? 0 : (int)seconds;
            int hours = total / 3600, minutes = (total / 60) % 60, secs = total % 60;
            return hours > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, secs)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", total / 60, secs);
        }

        /// <summary>One line for logs. Allocates.</summary>
        public string Describe() =>
            $"path {Path} | kills {Kills}/{EnemiesSpawned} | optional {OptionalSolved}/{OptionalAvailable} | " +
            $"score {Score:0.#} ({(IsGood ? "Good" : "Poor")}) | time {FormatTime(RunTimeSeconds)} | seed {RunSeed.Format(Seed)}" +
            (IsDebug ? " | DEBUG" : "");
    }
}
