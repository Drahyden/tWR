using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using TWR.Audio;
using TWR.Combat;
using TWR.Core;
using TWR.Core.Perception;
using TWR.Enemies;
using TWR.Player;
using TWR.Run;

namespace TWR.Scares
{
    /// <summary>What the director says to a scare that wants to happen (D-238).</summary>
    public enum ScareGate
    {
        /// <summary>Go.</summary>
        Allowed,
        /// <summary>Not now (combat, a voice line, the 120 s gap, another scare): ask again later.</summary>
        Held,
        /// <summary>Never here / never again this run (wrong section, silent room, ending, already fired or skipped).</summary>
        Refused,
    }

    /// <summary>
    /// Plays the jump scares (D-236 to D-239). Lives in Core (D-161), so it survives section loads; the ScareAnchors in the
    /// section scenes register with it and ask it to fire. It owns the rules (D-238): each scare once per run (reset when a
    /// new run starts), only in its own section template, never in silent rooms / endings / transitions, held while a Seeker
    /// chases or a voice line plays, and at least 120 s of run time between scares; a scare still held when its section
    /// unloads is skipped for the run. A hit = lights back on (or no blackout), figure shown, stinger on the 2D SFX source
    /// (D-237), ScareDip on music / ambience, VoiceEvents.HoldNonPriority, ScareEvents.Fired. The blackout is a global
    /// post-exposure Volume of its own, only enabled while dark (D-238). Checks run at ~10 Hz on scaled time, so a paused
    /// game freezes them (D-100). The F1 menu (ScareDebugPanel) can fire each scare and reset them.
    /// </summary>
    public class ScareDirector : MonoBehaviour
    {
        public static ScareDirector Instance { get; private set; }

        static readonly List<ScareAnchor> anchors = new List<ScareAnchor>(4);

        [SerializeField] ScareSettings settings;
        [Tooltip("2D source on SFXState, reverbZoneMix ~0.3 (D-237).")]
        [SerializeField] AudioSource stingerSource;
        [Tooltip("Global Volume with a dark post-exposure override, weight 0 and disabled until a scare needs the dark (D-238).")]
        [SerializeField] Volume blackout;

        readonly HashSet<string> fired = new HashSet<string>();   // fired or skipped this run
        readonly HashSet<string> skipped = new HashSet<string>();
        readonly HashSet<string> held = new HashSet<string>();
        readonly List<GameObject> hostiles = new List<GameObject>(16);

        RunManager manager;
        Camera viewer;
        Transform playerRoot;
        int sightMask;
        float nextCheck, lastCheck;
        float lastHitTime = float.NegativeInfinity; // run clock
        string lastFiredId;
        ScareData playing;
        Coroutine sequence;
        GameObject sequenceFigure;
        GameObject watchedFigure;
        ScareData watchedData;
        GameObject debugFigure;
        bool warnedNoBlackout, warnedNoStinger;

        // ---------- setup ----------

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            sightMask = GameLayers.WithoutActors(settings != null ? (int)settings.sightBlockers : 1);
            AudioRouting.RouteIfUnassigned(stingerSource, AudioBus.SFX); // D-237
            SetDark(false);
            if (settings == null) Debug.LogWarning("[Scares] ScareDirector has no ScareSettings: no jump scares. Run TWR > Run > Build Run Scenes.", this);
        }

        void OnEnable()
        {
            EnemyEvents.AwarenessChanged += HandleAwarenessChanged;
            CombatEvents.Killed += HandleKilled;
        }

        void OnDisable()
        {
            EnemyEvents.AwarenessChanged -= HandleAwarenessChanged;
            CombatEvents.Killed -= HandleKilled;
            StopSequence();
        }

        void Start()
        {
            lastCheck = Time.time;
            EnsureManager();
        }

        void OnDestroy()
        {
            if (manager != null) manager.RunStarted -= HandleRunStarted;
            if (Instance == this) Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            anchors.Clear();
        }

        void EnsureManager()
        {
            if (manager != null) return;
            manager = RunManager.Instance;
            if (manager != null) manager.RunStarted += HandleRunStarted;
        }

        // ---------- anchors ----------

        public static void Register(ScareAnchor anchor)
        {
            if (anchor != null && !anchors.Contains(anchor)) anchors.Add(anchor);
        }

        /// <summary>The anchor's section unloaded. A scare that was held until now is skipped for this run (D-238).</summary>
        public static void Unregister(ScareAnchor anchor)
        {
            anchors.Remove(anchor);
            if (Instance != null) Instance.HandleAnchorGone(anchor);
        }

        void HandleAnchorGone(ScareAnchor anchor)
        {
            if (anchor == null || anchor.Data == null) return;
            string id = anchor.Data.DisplayId;
            if (held.Remove(id) && !fired.Contains(id))
            {
                fired.Add(id);
                skipped.Add(id);
                Log($"'{id}' was still held when its section ended: skipped for this run (D-238).");
            }
            if (anchor.Figure != null && anchor.Figure == watchedFigure) watchedFigure = null;
        }

        // ---------- every frame ----------

        void Update()
        {
            if (Time.time < nextCheck) return; // scaled: a paused game checks nothing
            float elapsed = Time.time - lastCheck;
            lastCheck = Time.time;
            nextCheck = Time.time + (settings != null ? settings.checkInterval : 0.1f);
            if (settings == null) return;

            EnsureManager();
            for (int i = hostiles.Count - 1; i >= 0; i--)
                if (hostiles[i] == null || !hostiles[i].activeInHierarchy) hostiles.RemoveAt(i); // destroyed, unloaded
            for (int i = 0; i < anchors.Count; i++)
                if (anchors[i] != null) anchors[i].Tick(this, elapsed);
            WatchFigure();
        }

        /// "Behind you": the figure stays until the player stops seeing it (D-053a), or vanishes in a flicker up close.
        void WatchFigure()
        {
            if (watchedFigure == null) return;
            if (!watchedFigure.activeInHierarchy)
            {
                watchedFigure = null;
                return;
            }
            Vector3 feet = watchedFigure.transform.position;
            if (!PlayerObserves(feet))
            {
                watchedFigure.SetActive(false);
                Log($"'{watchedData?.DisplayId}': figure removed, nobody is looking (D-236).");
                watchedFigure = null;
            }
            else if (watchedData != null && watchedData.vanishIfCloserThan > 0f && FlatDistance(PlayerFeet, feet) < watchedData.vanishIfCloserThan)
            {
                StartCoroutine(ProximityFlicker(watchedFigure));
                watchedFigure = null;
            }
        }

        IEnumerator ProximityFlicker(GameObject figure)
        {
            SetDark(true);
            Hide(figure);
            yield return new WaitForSeconds(settings != null ? settings.proximityFlickerSeconds : 0.12f);
            if (playing == null) SetDark(false);
        }

        // ---------- rules (D-238) ----------

        /// <summary>Can this scare happen right now? Never marks anything (the F1 menu uses it to show the state).</summary>
        public ScareGate Check(ScareData data, out string reason)
        {
            reason = "";
            if (data == null) { reason = "no ScareData"; return ScareGate.Refused; }
            string id = data.DisplayId;
            if (fired.Contains(id)) { reason = skipped.Contains(id) ? "skipped this run (held until its section ended)" : "already fired this run"; return ScareGate.Refused; }
            if (!InRightPlace(data, out reason)) return ScareGate.Refused;
            if (playing != null) { reason = "another scare is playing"; return ScareGate.Held; }
            if (hostiles.Count > 0) { reason = "combat: a Seeker is chasing"; return ScareGate.Held; }
            if (VoiceEvents.LinePlaying) { reason = "a voice line is playing"; return ScareGate.Held; }
            if (CooldownLeft > 0f) { reason = "less than the minimum time since the last scare"; return ScareGate.Held; }
            return ScareGate.Allowed;
        }

        /// <summary>"Behind you" asks this before it puts the figure on its spot. Held is remembered (skip at section end).</summary>
        public ScareGate Prepare(ScareData data) => Gate(data);

        /// <summary>The anchor's trigger is met: fire now if the rules allow it.</summary>
        public ScareGate TryFire(ScareAnchor anchor)
        {
            ScareData data = anchor != null ? anchor.Data : null;
            ScareGate gate = Gate(data);
            if (gate != ScareGate.Allowed) return gate;
            fired.Add(data.DisplayId);
            held.Remove(data.DisplayId);
            StartSequence(data, anchor, false);
            return gate;
        }

        ScareGate Gate(ScareData data)
        {
            ScareGate gate = Check(data, out string reason);
            if (gate == ScareGate.Held && held.Add(data.DisplayId))
                Log($"'{data.DisplayId}' is held: {reason}. It fires when that is over, or is skipped if its section ends first (D-238).");
            return gate;
        }

        bool InRightPlace(ScareData data, out string reason)
        {
            reason = "";
            if (SoundscapeState.IsSilenced) { reason = "silent room"; return false; }
            EnsureManager();
            if (manager == null || !manager.IsCoreMode) return true; // sandbox: no sections to check
            if (manager.IsInEnding) { reason = "ending"; return false; }
            if (manager.IsTransitioning) { reason = "section transition"; return false; }
            SectionData section = manager.CurrentSection;
            if (section == null || section.type != data.template)
            {
                reason = $"only in {data.template} sections";
                return false;
            }
            return true;
        }

        float RunClock => manager != null && manager.IsCoreMode ? manager.RunTimeSeconds : Time.time;

        float CooldownLeft
        {
            get
            {
                if (float.IsNegativeInfinity(lastHitTime) || settings == null) return 0f;
                return Mathf.Max(0f, lastHitTime + settings.minSecondsBetweenScares - RunClock);
            }
        }

        // ---------- the scare itself ----------

        void StartSequence(ScareData data, ScareAnchor anchor, bool debug)
        {
            StopSequence();
            sequence = StartCoroutine(Play(data, anchor, debug));
        }

        IEnumerator Play(ScareData data, ScareAnchor anchor, bool debug)
        {
            playing = data;
            if (data.musicDipDb < 0f || data.ambienceDipDb < 0f) // D-237: the dip holds through the darkness, then recovers
                ScareDip.Begin(data.musicDipDb, data.ambienceDipDb, data.dipAttackSeconds,
                               data.preHitDarkSeconds + data.dipHoldSeconds, data.dipReleaseSeconds);
            if (data.preHitDarkSeconds > 0f)
            {
                SetDark(true);
                yield return new WaitForSeconds(data.preHitDarkSeconds);
            }

            // The hit.
            GameObject figure = debug ? DebugFigure() : anchor != null ? anchor.Figure : null;
            if (figure != null)
            {
                // "Behind you" already stands on its spot (the anchor put it there unseen); everything else is placed now.
                if (debug || data.trigger == ScareTrigger.FacingExitAfterSolve) PlaceFigure(data, anchor, figure, debug);
                figure.SetActive(true);
            }
            sequenceFigure = figure;
            SetDark(false);
            PlayStinger(data);
            VoiceEvents.HoldNonPriority(settings != null ? settings.voiceHoldSeconds : 3f);
            if (!debug)
            {
                lastHitTime = RunClock;
                lastFiredId = data.DisplayId;
            }
            ScareEvents.RaiseFired(data.DisplayId);
            Log($"'{data.DisplayId}' HIT{(debug ? " [F1 debug, rules skipped]" : "")} at run time {RunClock:0.0} s (D-236).");

            if (data.removeFigureWhenUnobserved)
            {
                watchedFigure = figure;
                watchedData = data;
            }
            else
            {
                if (data.figureVisibleSeconds > 0f) yield return new WaitForSeconds(data.figureVisibleSeconds);
                if (data.flickerDarkSeconds > 0f)
                {
                    SetDark(true);
                    Hide(figure);
                    yield return new WaitForSeconds(data.flickerDarkSeconds);
                    SetDark(false);
                }
                else Hide(figure);
            }
            sequenceFigure = null;
            playing = null;
            sequence = null;
        }

        /// Relief: on the line from the player to the door, figureDistance ahead (never past the door). Debug: straight ahead.
        void PlaceFigure(ScareData data, ScareAnchor anchor, GameObject figure, bool debug)
        {
            Vector3 feet = PlayerFeet;
            Vector3 direction;
            Vector3 position;
            if (debug || anchor == null || anchor.Target == null)
            {
                Camera cam = Viewer;
                direction = cam != null ? cam.transform.forward : Vector3.forward;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
                direction.Normalize();
                position = feet + direction * (settings != null ? settings.debugFigureDistance : 4f);
            }
            else
            {
                Vector3 toDoor = anchor.Target.position - feet;
                toDoor.y = 0f;
                float doorDistance = toDoor.magnitude;
                direction = doorDistance > 0.01f ? toDoor / doorDistance : anchor.transform.forward;
                float distance = Mathf.Clamp(doorDistance - 0.6f, 1f, data.figureDistance);
                position = feet + direction * distance;
                position.y = anchor.Target.position.y;
            }
            figure.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-direction));
        }

        void PlayStinger(ScareData data)
        {
            if (AudioLibrary.PlayOneShot(stingerSource, data.stingerId, data.stingerVolume)) return;
            if (warnedNoStinger) return;
            warnedNoStinger = true;
            Debug.LogWarning($"[Scares] No stinger played for '{data.DisplayId}': AudioLibrary ID '{data.stingerId}' has no clip or the " +
                             "stinger source is missing. Run TWR > Audio > Validate Audio (A1).", this);
        }

        void SetDark(bool dark)
        {
            if (blackout == null)
            {
                if (dark && !warnedNoBlackout)
                {
                    warnedNoBlackout = true;
                    Debug.LogWarning("[Scares] No blackout Volume: the lights can't go out. Run TWR > Run > Build Run Scenes.", this);
                }
                return;
            }
            blackout.weight = dark ? 1f : 0f;
            blackout.enabled = dark;
        }

        static void Hide(GameObject figure)
        {
            if (figure != null) figure.SetActive(false);
        }

        GameObject DebugFigure()
        {
            if (debugFigure == null && settings != null && settings.figurePrefab != null)
            {
                debugFigure = Instantiate(settings.figurePrefab);
                debugFigure.name = "Scare Figure (F1 debug)";
                debugFigure.SetActive(false);
            }
            return debugFigure;
        }

        /// Stop whatever is running: lights back on, figures hidden, no dip.
        void StopSequence()
        {
            if (sequence != null) StopCoroutine(sequence);
            sequence = null;
            playing = null;
            Hide(sequenceFigure);
            Hide(watchedFigure);
            sequenceFigure = null;
            watchedFigure = null;
            SetDark(false);
            ScareDip.Stop();
        }

        // ---------- what the player sees ----------

        public Camera Viewer
        {
            get
            {
                if (viewer == null || !viewer.isActiveAndEnabled) FindPlayer();
                return viewer;
            }
        }

        /// <summary>The player's feet (the PlayerController root), or the camera if there is none.</summary>
        public Vector3 PlayerFeet
        {
            get
            {
                Camera cam = Viewer;
                if (playerRoot != null) return playerRoot.position;
                return cam != null ? cam.transform.position : Vector3.zero;
            }
        }

        void FindPlayer()
        {
            viewer = Camera.main;
            PlayerController player = viewer != null ? viewer.GetComponentInParent<PlayerController>() : null;
            if (player == null) player = FindAnyObjectByType<PlayerController>(); // only when the camera changed, never per frame
            playerRoot = player != null ? player.transform : null;
        }

        /// <summary>Is the player facing this point (inside the view cone, walls ignored)? Shared ViewCone (D-132).</summary>
        public bool PlayerFaces(Vector3 point, float coneDegrees, float maxDistance)
        {
            Camera cam = Viewer;
            if (cam == null) return false;
            Transform eye = cam.transform;
            return ViewCone.IsInCone(eye.position, eye.forward, point, coneDegrees, maxDistance);
        }

        /// <summary>Would a figure standing here be on screen with a clear line of sight (chest or head)?</summary>
        public bool PlayerObserves(Vector3 feet)
        {
            Camera cam = Viewer;
            if (cam == null) return false;
            float chest = settings != null ? settings.figureChestHeight : 1.4f;
            return OnScreen(cam, feet + Vector3.up * chest) || OnScreen(cam, feet + Vector3.up * (chest + 0.35f));
        }

        bool OnScreen(Camera cam, Vector3 point)
        {
            const float margin = 0.1f; // a little past the edges, so nothing pops at the border
            Vector3 viewport = cam.WorldToViewportPoint(point);
            if (viewport.z <= 0.05f || viewport.x < -margin || viewport.x > 1f + margin || viewport.y < -margin || viewport.y > 1f + margin)
                return false;
            return ViewCone.HasLineOfSight(cam.transform.position, point, sightMask);
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // ---------- events ----------

        void HandleRunStarted(RunManager runManager) => ResetScares("new run");

        void HandleAwarenessChanged(GameObject enemy, EnemyAwareness from, EnemyAwareness to)
        {
            if (enemy == null) return;
            if (to == EnemyAwareness.Hostile)
            {
                if (!hostiles.Contains(enemy)) hostiles.Add(enemy);
            }
            else hostiles.Remove(enemy);
        }

        void HandleKilled(GameObject victim, DamageInfo killingBlow)
        {
            if (victim != null) hostiles.Remove(victim);
        }

        void ResetScares(string why)
        {
            StopSequence();
            fired.Clear();
            skipped.Clear();
            held.Clear();
            lastHitTime = float.NegativeInfinity;
            lastFiredId = null;
            for (int i = 0; i < anchors.Count; i++) if (anchors[i] != null) anchors[i].ResetForRun();
            Log($"Scares reset ({why}): each can fire once this run (D-238).");
        }

        static void Log(string message)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[Scares] " + message);
#endif
        }

        // ---------- debug (F1 menu, D-239) ----------

        public ScareSettings Settings => settings;
        public static IReadOnlyList<ScareAnchor> Anchors => anchors;
        public int HostileCount => hostiles.Count;
        public float CooldownLeftSeconds => CooldownLeft;
        public string PlayingId => playing != null ? playing.DisplayId : null;
        public string LastFiredId => lastFiredId;
        public bool IsPlaying => playing != null;
        public bool HasFired(ScareData data) => data != null && fired.Contains(data.DisplayId);
        public bool IsHeld(ScareData data) => data != null && held.Contains(data.DisplayId);

        /// <summary>Debug: play this scare now with the figure in front of the player. Rules skipped, nothing marked.</summary>
        public void DebugFire(ScareData data)
        {
            if (data != null) StartSequence(data, null, true);
        }

        /// <summary>Debug: every scare may fire again this run.</summary>
        public void DebugReset() => ResetScares("F1 reset");
    }
}
