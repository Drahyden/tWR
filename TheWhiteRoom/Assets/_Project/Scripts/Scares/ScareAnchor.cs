using UnityEngine;
using TWR.Portals;
using TWR.Puzzles;

namespace TWR.Scares
{
    /// <summary>
    /// Puts one jump scare (ScareData) into a section scene (D-236, D-239) and watches for its trigger; the ScareDirector in
    /// Core decides whether it may fire (D-238) and plays it.
    /// - "Relief" (FacingExitAfterSolve): armed by PuzzleEvents.Solved of a main (not optional) puzzle; fires when the player
    ///   is within triggerDistance of Target (the exit door) and facing it.
    /// - "Behind you" (SeenAfterUnobserved): armed when the player crosses the portal armOnPortalId. This object's position is
    ///   the spot. Once the spot has been off screen for unobservedSeconds and the player is within triggerDistance, the
    ///   figure is put there unseen (D-053a); the moment it is on screen with a clear line of sight, the scare fires.
    /// The figure is a Seeker-look prop (no AI, no collider, not in RunStats), hidden until needed. Ticked by the director
    /// at ~10 Hz; no per-frame work of its own.
    /// </summary>
    public class ScareAnchor : MonoBehaviour
    {
        [SerializeField] ScareData data;
        [Tooltip("The Seeker-look prop instance (hidden until the scare).")]
        [SerializeField] GameObject figure;
        [Tooltip("Relief: a floor point in the exit doorway.")]
        [SerializeField] Transform target;
        [Tooltip("Behind you: arms after the player crosses this portal (its PortalEvents id), e.g. 'ig.portal'.")]
        [SerializeField] string armOnPortalId;

        bool armed;
        bool prepared;
        float unobservedFor;

        public ScareData Data => data;
        public GameObject Figure => figure;
        public Transform Target => target;
        public string ArmOnPortalId => armOnPortalId;
        public bool Armed => armed;
        public bool FigurePrepared => prepared;
        public float UnobservedFor => unobservedFor;

        void Awake()
        {
            if (figure != null) figure.SetActive(false);
        }

        void OnEnable()
        {
            PuzzleEvents.Solved += HandleSolved;
            PortalEvents.Crossed += HandleCrossed;
            ScareDirector.Register(this);
        }

        void OnDisable()
        {
            PuzzleEvents.Solved -= HandleSolved;
            PortalEvents.Crossed -= HandleCrossed;
            ScareDirector.Unregister(this);
        }

        void HandleSolved(string puzzleId, bool isOptional)
        {
            if (data != null && data.trigger == ScareTrigger.FacingExitAfterSolve && !isOptional) armed = true;
        }

        void HandleCrossed(string portalId, Transform from, Transform to)
        {
            if (data != null && data.trigger == ScareTrigger.SeenAfterUnobserved && portalId == armOnPortalId) armed = true;
        }

        /// <summary>Called by the ScareDirector about 10 times a second (scaled time).</summary>
        public void Tick(ScareDirector director, float elapsed)
        {
            if (!armed || data == null || director.HasFired(data)) return;
            if (data.trigger == ScareTrigger.FacingExitAfterSolve) TickRelief(director);
            else TickBehindYou(director, elapsed);
        }

        void TickRelief(ScareDirector director)
        {
            if (target == null || director.IsPlaying) return;
            if (ScareDirector.FlatDistance(director.PlayerFeet, target.position) > data.triggerDistance) return;
            Vector3 doorCentre = target.position + Vector3.up * 1.3f;
            if (!director.PlayerFaces(doorCentre, data.viewConeDegrees, data.triggerDistance + 2f)) return;
            director.TryFire(this);
        }

        void TickBehindYou(ScareDirector director, float elapsed)
        {
            Vector3 spot = transform.position;
            Vector3 feet = director.PlayerFeet;
            bool onScreen = director.PlayerObserves(spot);
            bool near = ScareDirector.FlatDistance(feet, spot) <= data.triggerDistance;

            if (!prepared)
            {
                unobservedFor = onScreen ? 0f : unobservedFor + elapsed;
                if (onScreen || !near || unobservedFor < data.unobservedSeconds) return;
                if (director.Prepare(data) != ScareGate.Allowed) return;
                prepared = true; // put it there while nobody is looking (D-053a)
                FacePlayer(feet);
                if (figure != null) figure.SetActive(true);
                return;
            }

            if (!onScreen)
            {
                // Still unseen: keep it turned towards the player, or take it away again if it can't fire anyway.
                if (!near || director.Prepare(data) != ScareGate.Allowed) Unprepare();
                else FacePlayer(feet);
                return;
            }
            if (near) director.TryFire(this); // seen up close: the hit
        }

        void FacePlayer(Vector3 feet)
        {
            if (figure == null) return;
            Vector3 look = feet - transform.position;
            look.y = 0f;
            Quaternion rotation = look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look) : transform.rotation;
            figure.transform.SetPositionAndRotation(transform.position, rotation);
        }

        void Unprepare()
        {
            prepared = false;
            if (figure != null) figure.SetActive(false);
        }

        /// <summary>New run or F1 reset: forget the waiting state (keeps "armed", e.g. a puzzle that is still solved).</summary>
        public void ResetForRun()
        {
            Unprepare();
            unobservedFor = 0f;
        }
    }
}
