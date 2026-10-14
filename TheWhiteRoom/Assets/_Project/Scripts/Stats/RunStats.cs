using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TWR.Combat;
using TWR.Enemies;
using TWR.Player;
using TWR.Puzzles;

namespace TWR.Stats
{
    /// <summary>
    /// Run statistics, run-scoped (D-158, supersedes "scene-scoped" in D-137). Lives on the RunManager object,
    /// which survives scene loads; the RunManager calls ResetForNewRun() at every run start and
    /// AddLoadedScene() whenever a section scene loads. Counts enemies that appeared (EnemyEvents.Spawned),
    /// enemies the PLAYER killed (avoided enemies never count, D-052), and puzzles solved / available,
    /// split into optional ones (D-104). Bosses (BossMarker) are left out of both kill numbers (D-158).
    /// Score = kills/spawned x 50 + optional solved/available x 50, Good at 60+ (D-152); a part whose
    /// denominator is 0 scores its full 50 (D-158). Nothing is saved (D-039).
    /// </summary>
    public class RunStats : MonoBehaviour
    {
        public const float GoodScoreThreshold = 60f;

        [SerializeField] bool logChanges = true;

        readonly HashSet<GameObject> enemies = new HashSet<GameObject>();
        static readonly Predicate<GameObject> IsDestroyed = go => go == null;

        public int EnemiesRegistered { get; private set; }
        public int PlayerKills { get; private set; }
        public int PuzzlesTotal { get; private set; }
        public int PuzzlesSolved { get; private set; }
        public int OptionalPuzzlesTotal { get; private set; }
        public int OptionalPuzzlesSolved { get; private set; }

        /// <summary>Live performance score 0-100 (D-152, D-158).</summary>
        public float Score => ComputeScore(PlayerKills, EnemiesRegistered, OptionalPuzzlesSolved, OptionalPuzzlesTotal);
        /// <summary>Good (true) or Poor (false) for the ending matrix (D-028).</summary>
        public bool IsGood => Score >= GoodScoreThreshold;

        /// <summary>
        /// The D-152 formula, kept pure so it can be tested. Each part is worth 50; a part with nothing to
        /// count (0 enemies, or 0 optional puzzles) gives its full 50 (D-158). Ratios are clamped to 0..1.
        /// Examples: 2/4 kills + 1/2 puzzles = 25 + 25 = 50 (Poor); 0/0 + 0/0 = 100 (Good); 0/3 + 0/0 = 50 (Poor).
        /// </summary>
        public static float ComputeScore(int kills, int spawned, int optionalSolved, int optionalAvailable)
        {
            float killPart = spawned > 0 ? Mathf.Clamp01((float)kills / spawned) : 1f;
            float puzzlePart = optionalAvailable > 0 ? Mathf.Clamp01((float)optionalSolved / optionalAvailable) : 1f;
            return killPart * 50f + puzzlePart * 50f;
        }

        void OnEnable()
        {
            EnemyEvents.Spawned += HandleEnemySpawned;
            CombatEvents.Killed += HandleKilled;
            PuzzleEvents.Solved += HandlePuzzleSolved;
        }

        void OnDisable()
        {
            EnemyEvents.Spawned -= HandleEnemySpawned;
            CombatEvents.Killed -= HandleKilled;
            PuzzleEvents.Solved -= HandlePuzzleSolved;
        }

        /// <summary>Zero everything. Called by the RunManager when a run starts (D-158).</summary>
        public void ResetForNewRun()
        {
            enemies.Clear();
            EnemiesRegistered = 0;
            PlayerKills = 0;
            PuzzlesTotal = 0;
            PuzzlesSolved = 0;
            OptionalPuzzlesTotal = 0;
            OptionalPuzzlesSolved = 0;
            Log("run started, stats reset");
        }

        /// <summary>
        /// The persistent Core scene (D-161) holds no section content and is never counted. Set by the RunManager in
        /// the run (Core + additive sections); empty in the sandbox test scene, where the scene itself is the section.
        /// </summary>
        public string IgnoredSceneName { get; set; }

        /// <summary>
        /// A section scene finished loading: add its puzzles to the "available" totals (D-104, D-158).
        /// Called once per load by the RunManager (from SceneManager.sceneLoaded), so a section loaded twice counts twice.
        /// Enemies from the section that was just unloaded are dropped first (their objects are destroyed).
        /// </summary>
        public void AddLoadedScene(Scene scene)
        {
            enemies.RemoveWhere(IsDestroyed); // enemies from the unloaded scene are gone
            if (!string.IsNullOrEmpty(IgnoredSceneName) && scene.name == IgnoredSceneName) return;
            int added = 0, addedOptional = 0;
            foreach (PuzzleController puzzle in FindObjectsByType<PuzzleController>(FindObjectsSortMode.None))
            {
                if (puzzle.gameObject.scene != scene) continue;
                added++;
                if (puzzle.Data != null && puzzle.Data.isOptional) addedOptional++;
            }
            PuzzlesTotal += added;
            OptionalPuzzlesTotal += addedOptional;
            Log($"scene '{scene.name}' loaded: +{added} puzzles (+{addedOptional} optional)");
        }

        void HandleEnemySpawned(GameObject enemy)
        {
            if (enemy == null) return;
            if (enemy.TryGetComponent(out BossMarker _))
            {
                Log("boss appeared (not counted, D-158): " + enemy.name);
                return;
            }
            if (!enemies.Add(enemy)) return;
            EnemiesRegistered++;
            Log("enemy registered: " + enemy.name);
        }

        void HandleKilled(GameObject victim, DamageInfo killingBlow)
        {
            if (victim == null) return;
            if (victim.TryGetComponent(out PlayerController _))
            {
                Log("player died");
                return;
            }
            if (!enemies.Contains(victim)) return; // test dummies and bosses are not counted

            GameObject attacker = killingBlow.Attacker;
            bool byPlayer = attacker != null && attacker.TryGetComponent(out PlayerController _);
            if (byPlayer) PlayerKills++;
            Log($"{victim.name} killed by {(attacker != null ? attacker.name : "nothing")}{(byPlayer ? " (counts)" : " (not the player, doesn't count)")}");
        }

        void HandlePuzzleSolved(string puzzleId, bool isOptional)
        {
            PuzzlesSolved++;
            if (isOptional) OptionalPuzzlesSolved++;
            Log("puzzle solved: " + puzzleId);
        }

        /// <summary>One line for logs: kills, puzzles, score. Allocates; not for every frame.</summary>
        public string Summary() =>
            $"kills by player {PlayerKills} / enemies {EnemiesRegistered} | puzzles {PuzzlesSolved}/{PuzzlesTotal} " +
            $"(optional {OptionalPuzzlesSolved}/{OptionalPuzzlesTotal}) | score {Score:0.#} ({(IsGood ? "Good" : "Poor")})";

        void Log(string reason)
        {
            if (!logChanges) return;
            Debug.Log($"[RunStats] {reason} | {Summary()}", this);
        }
    }
}
