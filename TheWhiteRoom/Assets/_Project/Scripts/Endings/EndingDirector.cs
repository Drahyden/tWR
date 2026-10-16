using System;
using System.Collections;
using UnityEngine;
using TWR.Core;
using TWR.Player;
using TWR.Run;
using TWR.UI;

namespace TWR.Endings
{
    /// <summary>One timed camera move of a scripted ending: move the (frozen) player to target's position and view.</summary>
    [Serializable]
    public struct EndingCameraStep
    {
        [Tooltip("Feet position + view (Y rotation = facing, X rotation = looking up/down).")]
        public Transform target;
        [Tooltip("Seconds after the ending starts.")]
        [Min(0f)] public float startSeconds;
        [Tooltip("How long the move takes.")]
        [Min(0.01f)] public float seconds;
        [Tooltip("Camera field of view at the end of the move. 0 = leave it.")]
        [Min(0f)] public float fieldOfView;
    }

    /// <summary>
    /// The scene-side half of an ending (D-199, D-196). One per ending scene; knows its EndingData.
    /// Scripted endings (D-199): a 10-30 s timeline that moves the frozen player through camera steps (placeholder
    /// "cinematography"), raises the one inner-voice line through VoiceEvents with its localized subtitle key (D-080,
    /// D-084), can tint the screen (Absorbed, D-198), then asks the RunManager to finish (fade to white, end screen).
    /// Interactive endings (Overtime, D-196): nothing timed; the OvertimeButton calls Finish(). The ending scene never
    /// touches the RunResult (D-204). No per-frame allocations; finds the player once via RunManager.Instance.
    /// </summary>
    [DisallowMultipleComponent]
    public class EndingDirector : MonoBehaviour
    {
        [SerializeField] EndingData ending;

        [Header("Scripted timeline - D-199 (10-30 s)")]
        [SerializeField] [Range(10f, 30f)] float durationSeconds = 15f;
        [Tooltip("When the inner-voice line is raised (seconds after the start).")]
        [SerializeField] [Min(0f)] float voiceLineAtSeconds = 4f;
        [Tooltip("Camera moves, in time order, not overlapping.")]
        [SerializeField] EndingCameraStep[] cameraSteps = new EndingCameraStep[0];

        [Header("Screen tint (optional, e.g. Absorbed D-198)")]
        [SerializeField] Color tintColour = new Color(0.93f, 0.86f, 0.78f);
        [Tooltip("0 = no tint.")]
        [SerializeField] [Range(0f, 1f)] float tintAlpha;
        [SerializeField] [Min(0f)] float tintStartSeconds = 8f;
        [SerializeField] [Min(0f)] float tintSeconds = 4f;

        PlayerController player;
        bool finished;

        public EndingData Ending => ending;
        public float DurationSeconds => durationSeconds;
        public float VoiceLineAtSeconds => voiceLineAtSeconds;
        public float TintStartSeconds => tintStartSeconds;
        public float TintAlpha => tintAlpha;
        public EndingCameraStep[] CameraSteps => cameraSteps;
        public bool IsScripted => ending == null || ending.presentation == EndingPresentation.Scripted;

        void Start()
        {
            RunManager manager = RunManager.Instance;
            if (manager == null || !manager.IsInEnding)
            {
                Debug.LogWarning($"[Endings] '{name}': no ending is running (play from Core or MainMenu; F1 can trigger endings).", this);
                return;
            }
            player = manager.Loader != null ? manager.Loader.Player : null;
            if (IsScripted) StartCoroutine(Timeline(manager));
            else if (ending != null && ending.voiceLine != null && !ending.voiceLine.IsEmpty)
                StartCoroutine(LineOnly());
        }

        /// <summary>End the ending now (scripted: when the timer runs out; Overtime: the button). Once.</summary>
        public void Finish()
        {
            if (finished) return;
            RunManager manager = RunManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[Endings] Finish: no RunManager.", this);
                return;
            }
            finished = manager.FinishEnding();
        }

        IEnumerator LineOnly()
        {
            float t = 0f;
            while (t < voiceLineAtSeconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
            RaiseLine();
        }

        IEnumerator Timeline(RunManager manager)
        {
            float t = 0f;
            bool lineDone = false, tintDone = tintAlpha <= 0f;
            int next = 0, active = -1;
            Vector3 fromPosition = Vector3.zero;
            float fromYaw = 0f, fromPitch = 0f, fromFov = 0f;

            while (t < durationSeconds)
            {
                if (!lineDone && t >= voiceLineAtSeconds)
                {
                    lineDone = true;
                    RaiseLine();
                }
                if (!tintDone && t >= tintStartSeconds)
                {
                    tintDone = true;
                    ScreenFader fader = manager.Loader != null ? manager.Loader.Fader : null;
                    if (fader != null) StartCoroutine(fader.FadeTo(tintAlpha, tintColour, tintSeconds));
                }

                // Next camera step: finish the previous one exactly, then remember where this one starts.
                if (player != null && next < cameraSteps.Length && t >= cameraSteps[next].startSeconds)
                {
                    if (active >= 0) ApplyStep(active, 1f, fromPosition, fromYaw, fromPitch, fromFov);
                    active = next++;
                    fromPosition = player.transform.position;
                    fromYaw = player.Look.Yaw;
                    fromPitch = player.Look.Pitch;
                    fromFov = player.PlayerCamera != null ? player.PlayerCamera.fieldOfView : 0f;
                }
                if (active >= 0)
                {
                    EndingCameraStep step = cameraSteps[active];
                    float u = Mathf.Clamp01((t - step.startSeconds) / Mathf.Max(0.01f, step.seconds));
                    ApplyStep(active, u, fromPosition, fromYaw, fromPitch, fromFov);
                }

                yield return null;
                t += Time.deltaTime;
            }
            if (!lineDone) RaiseLine();
            Finish();
        }

        void ApplyStep(int index, float u, Vector3 fromPosition, float fromYaw, float fromPitch, float fromFov)
        {
            EndingCameraStep step = cameraSteps[index];
            if (step.target == null || player == null) return;
            float s = Mathf.SmoothStep(0f, 1f, u);
            Vector3 euler = step.target.eulerAngles;
            float yaw = Mathf.LerpAngle(fromYaw, euler.y, s);
            float pitch = Mathf.Lerp(fromPitch, Mathf.DeltaAngle(0f, euler.x), s);
            // Locomotion is frozen, so this only moves the body + view (through the motor, D-113).
            player.Teleport(Vector3.Lerp(fromPosition, step.target.position, s), Quaternion.Euler(pitch, yaw, 0f));
            if (step.fieldOfView > 0f && player.PlayerCamera != null)
                player.PlayerCamera.fieldOfView = Mathf.Lerp(fromFov, step.fieldOfView, s);
        }

        void RaiseLine()
        {
            if (ending == null || ending.voiceLine == null || ending.voiceLine.IsEmpty) return;
            VoiceEvents.RaiseEndingLine(ending.id, ending.voiceLine); // subtitle shown by SubtitleUI (D-089)
        }
    }
}
