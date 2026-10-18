using System.Collections;
using UnityEngine;
using TWR.Audio;
using TWR.Combat;
using TWR.Core;
using TWR.Player;
using TWR.Run;
using TWR.UI;

namespace TWR.Endings
{
    /// <summary>
    /// The Core-side half of an ending (D-199, D-200, D-207, D-209). Lives on the RunManager object in Core.
    /// Enter: the player becomes invulnerable, the gun is holstered and the HP/ammo HUD is hidden (D-207). Scripted endings
    /// (D-199) also hide the whole HUD, keep the player frozen after the scene loads (no input at all, Esc included via
    /// PauseLock) and hide the cursor. Overtime (D-196) keeps move/look/crouch/interact (PlayerInputReader.LimitedControls),
    /// the crosshair and the interaction prompt.
    /// Finish: freeze the player, fade to the ending's colour (white, or black for Overtime), then show the end-of-run
    /// screen with a free, visible cursor (D-209). Leave: undo all of it (New run / Main menu / another debug ending).
    /// A1 audio (D-221): scripted endings switch the mixer to the Ending snapshot when their scene is ready; Overtime stays
    /// on Default until the button, then Ending with the ambient bed off; the end screen stays on Ending; Leave resets
    /// to Default (D-220).
    /// </summary>
    [DisallowMultipleComponent]
    public class EndingSequence : MonoBehaviour
    {
        [Tooltip("End-of-run screen in Core (D-200).")]
        [SerializeField] EndOfRunScreen endScreen;
        [Tooltip("The HUD canvas: fully hidden during scripted endings and on the end screen.")]
        [SerializeField] Canvas hudCanvas;
        [Tooltip("HUD parts hidden in every ending (HP bar, ammo, gun name; D-207). The boss bar hides itself.")]
        [SerializeField] CanvasGroup[] gameplayHud = new CanvasGroup[0];

        RunManager manager;
        SectionLoader loader;
        PlayerHealth health;
        WeaponInventory weapons;
        PlayerInputReader input;
        PlayerLook look;
        bool finishing;

        /// <summary>An ending is running (from Enter until Leave).</summary>
        public bool IsActive { get; private set; }
        /// <summary>The running ending is a scripted one (D-199).</summary>
        public bool IsScripted { get; private set; }
        public bool EndScreenShown { get; private set; }
        /// <summary>Player input must stay off (D-207, D-209).</summary>
        public bool BlocksPlayerInput => IsActive && (IsScripted || finishing || EndScreenShown);

        void Awake()
        {
            manager = GetComponent<RunManager>();
            loader = GetComponent<SectionLoader>();
            PlayerController player = loader != null ? loader.Player : null;
            if (player != null)
            {
                health = player.GetComponent<PlayerHealth>();
                weapons = player.GetComponent<WeaponInventory>();
                input = player.GetComponent<PlayerInputReader>();
                look = player.GetComponent<PlayerLook>();
            }
            if (endScreen == null) Debug.LogError("[Endings] EndingSequence has no end-of-run screen. Run TWR > Run > Build Endings + Main Menu.", this);
        }

        void OnEnable()
        {
            if (endScreen == null) return;
            endScreen.NewRunRequested += HandleNewRun;
            endScreen.MainMenuRequested += HandleMainMenu;
        }

        void OnDisable()
        {
            if (endScreen == null) return;
            endScreen.NewRunRequested -= HandleNewRun;
            endScreen.MainMenuRequested -= HandleMainMenu;
        }

        /// <summary>Before the ending scene loads: set up the player for this ending (D-207).</summary>
        public void Enter(EndingData ending)
        {
            StopFinishing();
            if (endScreen != null) endScreen.Hide();
            EndScreenShown = false;
            IsActive = true;
            IsScripted = ending == null || ending.presentation == EndingPresentation.Scripted;

            if (health != null) health.Invulnerable = true;
            if (weapons != null) weapons.SetHolstered(true);
            SetGameplayHudVisible(false);
            if (hudCanvas != null) hudCanvas.enabled = !IsScripted; // Overtime keeps crosshair + prompt
            if (input != null) input.LimitedControls = !IsScripted;
            if (loader != null) loader.HoldFreezeAfterLoad = IsScripted;
            PauseLock.SetBlocked(IsScripted);
        }

        /// <summary>The ending scene is in and the player stands at its entry (screen still white).</summary>
        public void HandleSceneReady()
        {
            if (!IsScripted) return; // Overtime stays on Default (hum + footsteps) until the button (D-221)
            HideCursor(); // the frozen input reader freed it; a cutscene shows no cursor
            AudioState.EnterEnding(false); // D-221: scripted endings switch to Ending on scene start
        }

        /// <summary>Fade out and show the end-of-run screen (D-199, D-200). False if already finishing / shown.</summary>
        public bool Finish(EndingData ending, RunResult result, EndingCatalog catalog, IEndingsRecord record)
        {
            if (!IsActive || finishing || EndScreenShown || ending == null) return false;
            StartCoroutine(FinishRoutine(ending, result, catalog, record));
            return true;
        }

        IEnumerator FinishRoutine(EndingData ending, RunResult result, EndingCatalog catalog, IEndingsRecord record)
        {
            finishing = true;
            PauseLock.SetBlocked(true);
            // D-221: Overtime's button -> Ending with the ambience off; scripted endings are already on Ending.
            // The end-of-run screen then stays on Ending.
            AudioState.EnterEnding(!IsScripted);
            if (loader != null) loader.Freeze();
            HideCursor();

            ScreenFader fader = loader != null ? loader.Fader : null;
            if (fader != null) yield return fader.FadeTo(1f, ending.finishFadeColour, ending.finishFadeSeconds);

            if (hudCanvas != null) hudCanvas.enabled = false;
            finishing = false;
            EndScreenShown = true;
            if (endScreen != null)
            {
                endScreen.Show(result, ending, catalog, record);
                Cursor.lockState = CursorLockMode.None; // mouse-driven screen (D-209)
                Cursor.visible = true;
            }
            else if (manager != null)
            {
                manager.StartNewRunAfterEnding(); // nothing to show: don't get stuck
            }
        }

        /// <summary>Undo the ending setup. The screen stays fully covered (white) for the following scene load.</summary>
        public void Leave()
        {
            StopFinishing();
            if (endScreen != null) endScreen.Hide();
            EndScreenShown = false;

            if (loader != null)
            {
                loader.HoldFreezeAfterLoad = false;
                if (loader.Fader != null) loader.Fader.ResetColour();
            }
            if (health != null) health.Invulnerable = false;
            if (weapons != null) weapons.SetHolstered(false);
            if (input != null) input.LimitedControls = false;
            if (look != null) look.ApplyFieldOfView(); // endings may have changed the FOV
            SetGameplayHudVisible(true);
            if (hudCanvas != null) hudCanvas.enabled = true;
            PauseLock.SetBlocked(false);
            IsActive = false;
            IsScripted = false;
            AudioState.ResetToDefault(); // D-220: New run / Main menu / another ending start from Default
        }

        void StopFinishing()
        {
            if (!finishing) return;
            StopAllCoroutines();
            finishing = false;
        }

        void SetGameplayHudVisible(bool visible)
        {
            if (gameplayHud == null) return;
            for (int i = 0; i < gameplayHud.Length; i++)
                if (gameplayHud[i] != null) gameplayHud[i].alpha = visible ? 1f : 0f;
        }

        static void HideCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void HandleNewRun()
        {
            if (manager != null) manager.StartNewRunAfterEnding();
        }

        void HandleMainMenu()
        {
            if (manager != null) manager.ReturnToMainMenu();
        }
    }
}
