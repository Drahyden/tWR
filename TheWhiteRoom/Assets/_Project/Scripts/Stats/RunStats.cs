using System.Collections.Generic;
using UnityEngine;
using TWR.Combat;
using TWR.Enemies;
using TWR.Player;
using TWR.Puzzles;

namespace TWR.Stats
{
    /// <summary>
    /// Minimal run statistics (D-137; feeds D-029 performance later). Counts enemies that appeared
    /// (EnemyEvents.Spawned), enemies the PLAYER killed (CombatEvents.Killed where the attacker is the player;
    /// avoided enemies never count, D-052) and puzzles solved / total, split into optional ones (D-104).
    /// It lives in the scene, so a reload (death, D-034) or quit (D-093) resets it; nothing is saved (D-129).
    /// Debug log only for now: no HUD, no score formula (the end screen D-092 comes later).
    /// </summary>
    public class RunStats : MonoBehaviour
    {
        [SerializeField] bool logChanges = true;

        readonly HashSet<GameObject> enemies = new HashSet<GameObject>();

        public int EnemiesRegistered { get; private set; }
        public int PlayerKills { get; private set; }
        public int PuzzlesTotal { get; private set; }
        public int PuzzlesSolved { get; private set; }
        public int OptionalPuzzlesTotal { get; private set; }
        public int OptionalPuzzlesSolved { get; private set; }

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

        void Start()
        {
            // Once per scene load: how many puzzles this scene has (D-104 needs the optional total).
            foreach (PuzzleController puzzle in FindObjectsByType<PuzzleController>(FindObjectsSortMode.None))
            {
                PuzzlesTotal++;
                if (puzzle.Data != null && puzzle.Data.isOptional) OptionalPuzzlesTotal++;
            }
            Log("run started");
        }

        void HandleEnemySpawned(GameObject enemy)
        {
            if (enemy == null || !enemies.Add(enemy)) return;
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
            if (!enemies.Contains(victim)) return; // test dummies etc. are not enemies

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

        void Log(string reason)
        {
            if (!logChanges) return;
            Debug.Log($"[RunStats] {reason} | kills by player {PlayerKills} / enemies {EnemiesRegistered} | " +
                      $"puzzles {PuzzlesSolved}/{PuzzlesTotal} (optional {OptionalPuzzlesSolved}/{OptionalPuzzlesTotal})", this);
        }
    }
}
