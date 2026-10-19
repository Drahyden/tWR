using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TWR.Combat;
using TWR.Core;
using TWR.Endings;
using TWR.Enemies;
using TWR.Puzzles;
using TWR.Run;

namespace TWR.Audio
{
    /// <summary>
    /// The one adaptive music director (D-075, D-213, D-214, D-230 to D-235). Created by code at startup like the audio
    /// bootstrap (D-225) and kept across scene loads (DontDestroyOnLoad), so it works in MainMenu, Core, the lobby, every
    /// section and the endings. It only LISTENS to existing events and picks a state by priority (D-233):
    ///   Ending > Boss > Combat > Puzzle > Exploration > Lobby (main menu = Lobby track, menu stem only, D-230).
    /// - Lobby / Exploration: VoiceEvents.SectionStarted ("lobby" = Lobby, anything else = Exploration).
    /// - Puzzle: PuzzleEvents.Started until Solved or SectionEnded; drums after drumsDelaySeconds, timed only while
    ///   Puzzle is the playing state (D-232).
    /// - Combat: EnemyEvents.AwarenessChanged, every enemy in Hostile (Seeker Chase/Attack) is tracked; dropping out,
    ///   CombatEvents.Killed, being disabled/destroyed or a new section removes it. When none is left the drums fade
    ///   out first and drumsOutDelaySeconds later the state goes back (D-232). Bosses are left to the Boss state.
    /// - Boss: BossEvents.Started picks the track by boss id; the first HP loss (BossHealth.Changed) brings the drums,
    ///   PhaseChanged (phase 2) brings every layer; Defeated / Despawned end it (D-213, D-232).
    /// - Ending: while the RunManager is in an ending, the loops fade out; a scripted ending's EndingData.music plays
    ///   ONCE when its scene is ready (AudioState.IsEnding, the Ending snapshot). Overtime has no music (D-221, D-234).
    /// - Silent rooms (SoundscapeState.IsSilenced): the music fades to nothing and stops (D-168, D-233). It never asks
    ///   for a snapshot itself: AudioState stays the only snapshot controller (D-220).
    /// Playback: two MusicBanks (crossfade between states, D-214); stems start together via PlayScheduled and switch by
    /// volume (D-231). Event handlers only set flags; Update evaluates them, no per-frame allocations (D-100).
    /// The F1 debug menu shows all of this and can force each state (D-235, MusicDebugPanel).
    /// </summary>
    public class MusicDirector : MonoBehaviour
    {
        const float ChaserCheckInterval = 0.25f;

        static MusicDirector instance;

        /// <summary>The running director (null before startup or if the library is missing at startup).</summary>
        public static MusicDirector Instance => instance;

        MusicLibrary library;
        MusicBank bankA, bankB, active;

        // What the game told us (set by event handlers, read in Update).
        bool inMainMenu, inLobby, inSection, sandbox;
        bool puzzleActive;
        string puzzleId;
        float puzzleSeconds;
        readonly List<GameObject> chasers = new List<GameObject>(16);
        bool combatReleasing;
        float combatReleaseLeft;
        GameObject boss;
        BossHealth bossHealth;
        MusicData bossTrack;
        bool bossHit, bossPhaseTwo;
        EndingData ending;
        bool endingStingerDone;
        float silenceFade = 1.5f;
        float nextChaserCheck;

        // Debug force (F1).
        bool hasForce;
        MusicStateKind forced;
        MusicData forcedBoss;
        bool forcedStingerDone;

        // What is playing now.
        MusicStateKind playing;
        MusicData playingTrack;
        AudioClip playingStinger;

        // ---------- startup ----------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateOnStartup()
        {
            if (instance != null) return;
            var go = new GameObject("TWR Music (director, D-233)");
            DontDestroyOnLoad(go);
            go.AddComponent<MusicDirector>();
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            library = MusicLibrary.Shared;
            if (library != null) silenceFade = library.silenceFadeSeconds;
            bankA = new MusicBank(transform, "A");
            bankB = new MusicBank(transform, "B");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            gameObject.AddComponent<MusicDebugOverlay>(); // F1 music-only view where no RunDebugOverlay exists (D-235)
#endif
            CheckScene(SceneManager.GetActiveScene()); // the first scene loaded before we existed
        }

        void OnEnable()
        {
            if (instance != this) return;
            VoiceEvents.SectionStarted += HandleSectionStarted;
            VoiceEvents.SectionEnded += HandleSectionEnded;
            PuzzleEvents.Started += HandlePuzzleStarted;
            PuzzleEvents.Solved += HandlePuzzleSolved;
            EnemyEvents.AwarenessChanged += HandleAwarenessChanged;
            CombatEvents.Killed += HandleKilled;
            BossEvents.Started += HandleBossStarted;
            BossEvents.PhaseChanged += HandleBossPhaseChanged;
            BossEvents.Defeated += HandleBossDefeated;
            BossEvents.Despawned += HandleBossDespawned;
            SoundscapeState.Changed += HandleSilenceChanged;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        void OnDisable()
        {
            if (instance != this) return;
            VoiceEvents.SectionStarted -= HandleSectionStarted;
            VoiceEvents.SectionEnded -= HandleSectionEnded;
            PuzzleEvents.Started -= HandlePuzzleStarted;
            PuzzleEvents.Solved -= HandlePuzzleSolved;
            EnemyEvents.AwarenessChanged -= HandleAwarenessChanged;
            CombatEvents.Killed -= HandleKilled;
            BossEvents.Started -= HandleBossStarted;
            BossEvents.PhaseChanged -= HandleBossPhaseChanged;
            BossEvents.Defeated -= HandleBossDefeated;
            BossEvents.Despawned -= HandleBossDespawned;
            SoundscapeState.Changed -= HandleSilenceChanged;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            ClearBoss();
        }

        void OnDestroy()
        {
            if (instance != this) return;
            instance = null;
            bankA?.StopNow();
            bankB?.StopNow();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        // ---------- event handlers (flags only) ----------

        void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) CheckScene(scene);
        }

        /// MainMenu loaded alone = main menu state. Core loaded from the menu keeps the menu music until the lobby
        /// starts (same track, the other stems simply fade in, D-230).
        void CheckScene(Scene scene)
        {
            string menuScene = library != null ? library.mainMenuSceneName : "MainMenu";
            if (scene.name == menuScene)
            {
                ResetRunInputs();
                inMainMenu = true;
                return;
            }
            RunManager manager = RunManager.Instance;
            if (inMainMenu && manager != null && manager.IsCoreMode) return;
            inMainMenu = false;
        }

        void HandleSectionStarted(string sectionId)
        {
            ResetRunInputs();
            string lobbyId = library != null ? library.lobbySectionId : "lobby";
            inLobby = sectionId == lobbyId;
            inSection = !inLobby;
        }

        void HandleSectionEnded(string sectionId) => puzzleActive = false; // D-232: SectionEnded ends Puzzle

        void HandlePuzzleStarted(string id)
        {
            puzzleActive = true;
            puzzleId = id;
            puzzleSeconds = 0f;
        }

        void HandlePuzzleSolved(string id, bool isOptional)
        {
            if (puzzleActive && id == puzzleId) puzzleActive = false;
        }

        void HandleAwarenessChanged(GameObject enemy, EnemyAwareness from, EnemyAwareness to)
        {
            if (enemy == null || enemy.TryGetComponent<BossMarker>(out _)) return; // bosses use the Boss state (D-213)
            if (to == EnemyAwareness.Hostile)
            {
                if (!chasers.Contains(enemy)) chasers.Add(enemy);
            }
            else chasers.Remove(enemy);
        }

        void HandleKilled(GameObject victim, DamageInfo killingBlow)
        {
            if (victim != null) chasers.Remove(victim);
        }

        void HandleBossStarted(GameObject bossObject, string bossId)
        {
            ClearBoss();
            boss = bossObject;
            bossTrack = library != null ? library.FindBoss(bossId) : null;
            bossHealth = bossObject != null ? bossObject.GetComponent<BossHealth>() : null;
            if (bossHealth == null) return;
            bossHealth.Changed += HandleBossHealthChanged;
            // The fight may have started BECAUSE of the first shot (HP already lower): drums at once (D-232).
            bossHit = bossHealth.Health < bossHealth.MaxHealth;
            bossPhaseTwo = bossHealth.Phase >= 2;
        }

        void HandleBossHealthChanged(float current, float max)
        {
            if (current < max) bossHit = true; // the first hit on the boss brings the drums (D-232)
        }

        void HandleBossPhaseChanged(GameObject bossObject, string bossId, int phase)
        {
            if (bossObject == boss && phase >= 2) bossPhaseTwo = true; // every layer (D-213)
        }

        void HandleBossDefeated(GameObject bossObject, string bossId)
        {
            if (bossObject == boss) ClearBoss();
        }

        void HandleBossDespawned(GameObject bossObject)
        {
            if (bossObject == boss) ClearBoss();
        }

        void HandleSilenceChanged(bool silenced, float fadeSeconds) => silenceFade = fadeSeconds;

        void ClearBoss()
        {
            if (bossHealth != null) bossHealth.Changed -= HandleBossHealthChanged;
            boss = null;
            bossHealth = null;
            bossTrack = null;
            bossHit = false;
            bossPhaseTwo = false;
        }

        /// A new section, the main menu, or an ending: forget everything the last scene told us.
        void ResetRunInputs()
        {
            inMainMenu = false;
            inLobby = false;
            inSection = false;
            puzzleActive = false;
            puzzleSeconds = 0f;
            chasers.Clear();
            combatReleasing = false;
            ClearBoss();
        }

        // ---------- every frame ----------

        void Update()
        {
            if (instance != this) return; // a duplicate waiting to be destroyed
            float gameDelta = Time.deltaTime;      // gameplay timers stop while the game is paused
            float audioDelta = Time.unscaledDeltaTime; // fades always run

            PollRun();
            if (Time.unscaledTime >= nextChaserCheck)
            {
                nextChaserCheck = Time.unscaledTime + ChaserCheckInterval;
                RemoveGoneChasers();
            }
            UpdateCombatRelease(gameDelta);

            MusicStateKind wanted = WantedState();
            MusicData track = TrackFor(wanted);
            AudioClip stinger = wanted == MusicStateKind.Ending ? WantedStinger() : null;
            if (wanted != playing || track != playingTrack || stinger != playingStinger) SwitchTo(wanted, track, stinger);

            if (playing == MusicStateKind.Puzzle) puzzleSeconds += gameDelta; // D-232: only while Puzzle plays
            ApplyLayerRules();
            bankA.Tick(audioDelta);
            bankB.Tick(audioDelta);
        }

        /// Endings and sandbox mode come from the RunManager (a static property read, no search).
        void PollRun()
        {
            RunManager manager = RunManager.Instance;
            EndingData now = manager != null && manager.IsInEnding ? manager.CurrentEnding : null;
            if (now != ending)
            {
                ending = now;
                endingStingerDone = false;
                if (now != null) ResetRunInputs(); // after the ending the lobby starts fresh
            }
            // The sandbox test scene has a RunManager without sections: treat it as one big section.
            sandbox = manager != null && !manager.IsCoreMode && !inMainMenu;
        }

        void RemoveGoneChasers()
        {
            for (int i = chasers.Count - 1; i >= 0; i--)
            {
                GameObject chaser = chasers[i];
                if (chaser == null || !chaser.activeInHierarchy) chasers.RemoveAt(i); // destroyed, despawned, unloaded
            }
        }

        void UpdateCombatRelease(float deltaTime)
        {
            if (chasers.Count > 0)
            {
                combatReleasing = false;
                return;
            }
            if (combatReleasing)
            {
                combatReleaseLeft -= deltaTime;
                if (combatReleaseLeft <= 0f) combatReleasing = false;
                return;
            }
            if (playing == MusicStateKind.Combat && !hasForce)
            {
                // Last chaser gone: drums fade out first, the state goes back after the delay (D-232).
                combatReleasing = true;
                combatReleaseLeft = library != null && library.combat != null ? library.combat.drumsOutDelaySeconds : 2.5f;
            }
        }

        /// The priority rule (D-233). Pure: reads flags only.
        MusicStateKind WantedState()
        {
            if (hasForce) return forced;
            if (ending != null) return MusicStateKind.Ending;
            if (SoundscapeState.IsSilenced) return MusicStateKind.None;
            if (boss != null && bossTrack != null) return MusicStateKind.Boss;
            if (chasers.Count > 0 || combatReleasing) return MusicStateKind.Combat;
            if (puzzleActive) return MusicStateKind.Puzzle;
            if (inSection || sandbox) return MusicStateKind.Exploration;
            if (inLobby) return MusicStateKind.Lobby;
            if (inMainMenu) return MusicStateKind.MainMenu;
            return MusicStateKind.None;
        }

        MusicData TrackFor(MusicStateKind state)
        {
            if (library == null) return null;
            switch (state)
            {
                case MusicStateKind.MainMenu:
                case MusicStateKind.Lobby: return library.lobby;
                case MusicStateKind.Exploration: return library.exploration;
                case MusicStateKind.Puzzle: return library.puzzle;
                case MusicStateKind.Combat: return library.combat;
                case MusicStateKind.Boss: return hasForce && forcedBoss != null ? forcedBoss : bossTrack;
                default: return null;
            }
        }

        /// The ending stinger to play now, or null (D-234): scripted endings only, once their scene is ready.
        AudioClip WantedStinger()
        {
            if (hasForce)
            {
                AudioClip clip = ending != null && ending.music != null ? ending.music : null;
                return clip != null ? clip : library != null ? library.debugEndingClip : null;
            }
            if (ending == null || ending.presentation != EndingPresentation.Scripted) return null; // Overtime: D-221
            if (!AudioState.IsEnding) return null; // the ending scene isn't ready yet (screen still white)
            return ending.music;
        }

        // ---------- switching ----------

        void SwitchTo(MusicStateKind state, MusicData track, AudioClip stinger)
        {
            MusicData previousTrack = playingTrack;
            playing = state;
            playingTrack = track;
            playingStinger = stinger;

            float seconds;
            if (state == MusicStateKind.None)
                seconds = SoundscapeState.IsSilenced ? silenceFade : previousTrack != null ? previousTrack.crossfadeSeconds : 2f;
            else if (state == MusicStateKind.Ending)
                seconds = library != null ? library.endingFadeOutSeconds : 1.5f;
            else
                seconds = track != null ? track.crossfadeSeconds : 2f;

            // 1. The track already plays (Lobby <-> main menu, Boss with the same boss): keep it, only the layers change.
            if (track != null && active != null && active.Data == track)
            {
                active.FadeTo(1f, seconds);
                Log(state, track.DisplayName + " (continues)");
                return;
            }
            // 2. The track is still fading out on the other bank (quick back and forth): bring it back, still in sync.
            MusicBank other = active == bankA ? bankB : bankA;
            if (track != null && other.Data == track)
            {
                if (active != null) active.FadeTo(0f, seconds);
                active = other;
                active.FadeTo(1f, seconds);
                Log(state, track.DisplayName + " (back)");
                return;
            }

            // 3. Fade out whatever plays; start the new track (or the stinger) on a free bank.
            if (bankA.IsPlaying) bankA.FadeTo(0f, seconds);
            if (bankB.IsPlaying) bankB.FadeTo(0f, seconds);
            active = null;

            bool playStinger = stinger != null && !(hasForce ? forcedStingerDone : endingStingerDone);
            if (track == null && !playStinger)
            {
                Log(state, "no music");
                return;
            }

            MusicBank bank = !bankA.IsPlaying ? bankA : !bankB.IsPlaying ? bankB : bankA.Volume <= bankB.Volume ? bankA : bankB;
            double startAt = AudioSettings.dspTime + (library != null ? library.scheduleLeadSeconds : 0.2f);
            if (track != null)
            {
                bank.Begin(track, startAt);
                bank.FadeTo(1f, track.crossfadeSeconds);
                Log(state, $"{track.DisplayName} ({track.StemCount} stems, bank {bank.Label})");
            }
            else
            {
                bank.BeginOneShot(stinger, library != null ? library.endingVolume : 0.9f, startAt);
                bank.FadeTo(1f, library != null ? library.endingFadeInSeconds : 0.3f);
                if (hasForce) forcedStingerDone = true;
                else endingStingerDone = true;
                Log(state, $"stinger {stinger.name} (once, bank {bank.Label})");
            }
            active = bank;
        }

        /// The layer rule of the playing state (D-230, D-232). Cheap: at most MusicData.MaxStems checks per frame.
        void ApplyLayerRules()
        {
            if (active == null || active.Data == null) return;
            MusicData data = active.Data;
            for (int i = 0; i < active.Count; i++) active.SetLayer(i, LayerOn(data, i));
        }

        bool LayerOn(MusicData data, int index)
        {
            bool drums = data.IsDrums(index);
            bool entry = data.OnAtEntry(index);
            switch (playing)
            {
                case MusicStateKind.MainMenu: return data.IsMenuStem(index);                 // D-230
                case MusicStateKind.Puzzle: return entry || (drums && PuzzleDrumsOn);           // D-232
                case MusicStateKind.Combat: return entry && !(drums && combatReleasing);        // D-232
                case MusicStateKind.Boss: return bossPhaseTwo || entry || (drums && bossHit);   // D-213, D-232
                default: return entry;                                                          // Lobby, Exploration
            }
        }

        bool PuzzleDrumsOn => puzzleSeconds >= PuzzleDrumsDelay;

        void Log(MusicStateKind state, string what)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Audio] Music -> {state}: {what}{(hasForce ? " [forced, F1]" : "")} (D-233).", this);
#endif
        }

        // ---------- debug (F1 menu, D-235) ----------

        public MusicLibrary Library => library;
        public MusicStateKind Playing => playing;
        public MusicStateKind Wanted => WantedState();
        public bool IsForced => hasForce;
        public MusicBank ActiveBank => active;
        public MusicBank BankA => bankA;
        public MusicBank BankB => bankB;
        public bool PuzzleActive => puzzleActive;
        public string PuzzleId => puzzleId;
        public float PuzzleSeconds => puzzleSeconds;
        public float PuzzleDrumsDelay => library != null && library.puzzle != null ? library.puzzle.drumsDelaySeconds : 30f;
        public int ChaserCount => chasers.Count;
        public bool CombatReleasing => combatReleasing;
        public float CombatReleaseLeft => combatReleaseLeft;
        public string BossTrackName => bossTrack != null ? bossTrack.DisplayName : null;
        public bool BossHit => bossHit;
        public bool BossPhaseTwo => bossPhaseTwo;
        public string EndingId => ending != null ? ending.DisplayId : null;
        public bool EndingStingerDone => endingStingerDone;

        /// <summary>Debug: play this state regardless of the game (boss = which boss track for Boss).</summary>
        public void DebugForce(MusicStateKind state, MusicData bossData)
        {
            hasForce = true;
            forced = state;
            forcedBoss = null;
            if (state == MusicStateKind.Boss)
            {
                forcedBoss = bossData;
                if (forcedBoss == null && library != null && library.bosses != null && library.bosses.Length > 0) forcedBoss = library.bosses[0];
            }
            if (state == MusicStateKind.Ending) forcedStingerDone = false;
            if (state == MusicStateKind.Puzzle && !puzzleActive) puzzleSeconds = 0f;
        }

        /// <summary>Debug: back to the game's own state.</summary>
        public void DebugClearForce()
        {
            hasForce = false;
            forcedBoss = null;
        }

        /// <summary>Debug: pretend the boss was hit (drums) / reached phase 2 (all layers). Works on a forced Boss too.</summary>
        public void DebugBossHit() => bossHit = true;
        public void DebugBossPhaseTwo() => bossPhaseTwo = true;
        /// <summary>Debug: skip the puzzle drums timer.</summary>
        public void DebugPuzzleDrumsNow() => puzzleSeconds = Mathf.Max(puzzleSeconds, PuzzleDrumsDelay);
    }
}
