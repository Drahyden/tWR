using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.SceneManagement;
using TWR.Player;
using TWR.Portals;
using TWR.UI;

namespace TWR.Run
{
    /// <summary>
    /// Swaps the one content scene that sits next to the persistent Core scene (D-161, D-167). A transition is:
    /// freeze the player (input, movement, portal crossing), fade to white (~0.4 s), unload the current content scene,
    /// load the next one additively, keep Core as the active scene (its lighting and global Bloom volume apply),
    /// teleport the player to the new scene's SectionEntry through PlayerController.Teleport (IPlayerMotor, D-113),
    /// wait one frame so the new scene's Start runs (Seekers spawn, exit gates read the section), fade in, unfreeze.
    /// The ambient bed lives in Core and keeps playing. Each section scene brings its own baked NavMesh (NavMeshSurface),
    /// which Unity adds and removes with the scene. Also plays the placeholder "run complete" screen (D-092), now only a
    /// fallback when no ending can be loaded (D-199 supersedes it).
    /// R5 (D-199, D-207): HoldFreezeAfterLoad keeps the player frozen after a load (scripted endings: no control at all);
    /// Freeze/Unfreeze are public so the ending sequence can stop the player before the end-of-run screen.
    /// Lives on the RunManager object in Core; its presence switches the RunManager into "Core mode".
    /// </summary>
    [DefaultExecutionOrder(-210)]
    [DisallowMultipleComponent]
    public class SectionLoader : MonoBehaviour
    {
        [Tooltip("The persistent player in the Core scene.")]
        [SerializeField] PlayerController player;
        [Tooltip("White overlay in the Core scene.")]
        [SerializeField] ScreenFader fader;

        readonly List<Scene> toUnload = new List<Scene>(4);
        RunManager manager;
        Scene coreScene;
        PlayerInputReader input;
        PlayerLocomotion locomotion;
        PortalTraveller traveller;
        PlayerHealth health;
        bool frozen;

        // One waiting request (the newest wins), e.g. the run ends while a section is still fading in.
        bool hasQueued;
        string queuedScene;
        Action queuedLoaded;

        public PlayerController Player => player;
        public ScreenFader Fader => fader;
        /// <summary>A transition (or the end screen) is running.</summary>
        public bool IsBusy { get; private set; }
        /// <summary>Name of the loaded content scene, null while none is loaded.</summary>
        public string CurrentContentScene { get; private set; }
        public Scene CoreScene => coreScene;
        /// <summary>True = the next loads leave the player frozen (scripted endings, D-199). Set by the ending sequence.</summary>
        public bool HoldFreezeAfterLoad { get; set; }

        /// <summary>The screen starts fading to white.</summary>
        public event Action TransitionStarted;
        /// <summary>The new section is visible and the player can move again.</summary>
        public event Action TransitionFinished;

        float FadeOutSeconds => manager != null && manager.Config != null ? manager.Config.fadeOutSeconds : 0.4f;
        float FadeInSeconds => manager != null && manager.Config != null ? manager.Config.fadeInSeconds : 0.4f;

        void Awake()
        {
            manager = GetComponent<RunManager>();
            coreScene = gameObject.scene;
            if (player == null)
            {
                Debug.LogError("[Run] SectionLoader has no player. Run TWR > Run > Build Run Scenes.", this);
                return;
            }
            // GetComponent, not the PlayerController properties: this Awake runs before the player's (order -210).
            input = player.GetComponent<PlayerInputReader>();
            locomotion = player.GetComponent<PlayerLocomotion>();
            traveller = player.GetComponent<PortalTraveller>();
            health = player.GetComponent<PlayerHealth>();
            if (fader == null) Debug.LogWarning("[Run] SectionLoader has no ScreenFader: transitions will cut without a fade.", this);
        }

        // ---------- section transition ----------

        /// <summary>
        /// Replace the content scene with sceneName (must be in Build Settings). onLoaded runs once the player stands
        /// at the new entry (screen still white). If a transition is running, this request waits for it.
        /// </summary>
        public void Load(string sceneName, Action onLoaded)
        {
            if (IsBusy)
            {
                hasQueued = true;
                queuedScene = sceneName;
                queuedLoaded = onLoaded;
                return;
            }
            StartCoroutine(LoadRoutine(sceneName, onLoaded));
        }

        IEnumerator LoadRoutine(string sceneName, Action onLoaded)
        {
            IsBusy = true;
            TransitionStarted?.Invoke();
            Freeze();
            if (fader != null) yield return fader.FadeTo(1f, FadeOutSeconds);

            // 1. Unload every content scene (everything except Core). Core stays active the whole time.
            if (coreScene.IsValid() && coreScene.isLoaded) SceneManager.SetActiveScene(coreScene);
            toUnload.Clear();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != coreScene && scene.isLoaded) toUnload.Add(scene);
            }
            for (int i = 0; i < toUnload.Count; i++)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(toUnload[i]);
                if (unload != null) yield return unload;
            }
            toUnload.Clear();
            CurrentContentScene = null;

            // 2. Load the next one next to Core (additive). Its sceneLoaded callback reaches the RunManager/RunStats.
            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (load == null)
                Debug.LogError($"[Run] Could not load scene '{sceneName}'. Is it in Build Settings? Run TWR > Run > Build Run Scenes.", this);
            else
                yield return load;

            Scene loaded = SceneManager.GetSceneByName(sceneName);
            if (coreScene.IsValid() && coreScene.isLoaded) SceneManager.SetActiveScene(coreScene);

            // 3. Put the player at the new entry.
            if (loaded.IsValid() && loaded.isLoaded)
            {
                CurrentContentScene = sceneName;
                SectionEntry entry = FindEntry(loaded);
                if (entry != null) MovePlayerTo(entry.transform);
                else Debug.LogError($"[Run] Scene '{sceneName}' has no SectionEntry; the player stays where it was.", this);
            }
            onLoaded?.Invoke();

            // 4. One frame for the new scene's Start (Seekers spawn, exit gates read the section), then fade in.
            yield return null;
            if (fader != null)
            {
                fader.HideMessage();
                yield return fader.FadeTo(0f, FadeInSeconds);
            }
            if (!HoldFreezeAfterLoad) Unfreeze();
            IsBusy = false;
            TransitionFinished?.Invoke();
            RunQueued();
        }

        // ---------- run complete (placeholder end screen, D-092) ----------

        /// <summary>
        /// Fade to white, show the localized "run complete" line for holdSeconds, then call onFinished (the RunManager
        /// ends the run; its lobby load keeps the screen white and removes the line before fading in).
        /// </summary>
        public void PlayRunComplete(LocalizedString message, float holdSeconds, Action onFinished)
        {
            if (IsBusy)
            {
                Debug.LogWarning("[Run] Run complete requested during a transition; ending the run directly.", this);
                onFinished?.Invoke();
                return;
            }
            StartCoroutine(RunCompleteRoutine(message, holdSeconds, onFinished));
        }

        IEnumerator RunCompleteRoutine(LocalizedString message, float holdSeconds, Action onFinished)
        {
            IsBusy = true;
            TransitionStarted?.Invoke();
            Freeze();
            if (fader != null)
            {
                yield return fader.FadeTo(1f, FadeOutSeconds);
                fader.ShowMessage(message);
            }
            if (holdSeconds > 0f) yield return new WaitForSecondsRealtime(holdSeconds);

            IsBusy = false;
            onFinished?.Invoke(); // normally starts the lobby load (IsBusy again)
            if (IsBusy) yield break;

            // Nothing followed: give the screen and the controls back.
            if (fader != null)
            {
                fader.HideMessage();
                yield return fader.FadeTo(0f, FadeInSeconds);
            }
            Unfreeze();
            TransitionFinished?.Invoke();
        }

        // ---------- helpers ----------

        void RunQueued()
        {
            if (!hasQueued) return;
            hasQueued = false;
            string scene = queuedScene;
            Action loaded = queuedLoaded;
            queuedScene = null;
            queuedLoaded = null;
            Load(scene, loaded);
        }

        static SectionEntry FindEntry(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects(); // once per load
            for (int i = 0; i < roots.Length; i++)
            {
                SectionEntry entry = roots[i].GetComponentInChildren<SectionEntry>(true);
                if (entry != null) return entry;
            }
            return null;
        }

        void MovePlayerTo(Transform spawn)
        {
            if (player == null) return;
            // Through PlayerController.Teleport -> IPlayerMotor.Teleport (D-113). Yaw only: look straight ahead.
            player.Teleport(spawn.position, Quaternion.Euler(0f, spawn.eulerAngles.y, 0f));
            if (locomotion != null) locomotion.ResetMotion();
        }

        /// <summary>No input, no movement, no portal crossing while the screen is white (D-167).</summary>
        public void Freeze()
        {
            if (frozen) return;
            frozen = true;
            if (locomotion != null)
            {
                locomotion.ResetMotion(); // stops footsteps and falling at once
                locomotion.enabled = false;
            }
            if (traveller != null) traveller.enabled = false; // the teleport must never count as a portal crossing
            if (input != null) input.enabled = false;
        }

        /// <summary>Give movement, portal crossing and (if alive) input back.</summary>
        public void Unfreeze()
        {
            if (!frozen) return;
            frozen = false;
            if (locomotion != null) locomotion.enabled = true;
            if (traveller != null) traveller.enabled = true; // OnEnable forgets the old head position
            // A dead player stays without input; the run reset (D-162) revives it before the lobby fades in.
            if (input != null && (health == null || health.IsAlive)) input.enabled = true;
        }
    }
}
