using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TWR.Audio;
using TWR.Core;
using TWR.Run;

namespace TWR.UI
{
    /// <summary>
    /// The minimal classic main menu (D-085, D-202) in its own scene, build index 0, loaded alone without Core (D-210).
    /// New run loads the Core scene (single), whose RunManager then loads the hub lobby additively and starts a run
    /// (curated first run while no endings are saved, D-154). Settings opens the settings panel with Back; for now it holds
    /// the Sound settings (AudioSettingsPanel, D-225), which save when the panel is left. Quit exits the application
    /// (D-211; in the editor it stops Play mode). Credits (D-229) opens the scrolling credits list with its own Back. A1 (D-220): the menu resets the mixer to the Default snapshot.
    /// Mouse-driven with a free, visible cursor (D-209); the cursor re-locks when the player's input turns on in the run.
    /// Labels are localized through LocalizedWorldText on each TMP label (D-084).
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        [Tooltip("Gives the Core scene name.")]
        [SerializeField] RunConfig config;
        [SerializeField] GameObject mainPanel;
        [SerializeField] GameObject settingsPanel;
        [SerializeField] GameObject creditsPanel;
        [SerializeField] Button newRunButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button creditsButton;
        [SerializeField] Button quitButton;
        [SerializeField] Button backButton;
        [SerializeField] Button creditsBackButton;

        bool loading;

        string CoreScene => config != null && !string.IsNullOrEmpty(config.coreSceneName) ? config.coreSceneName : "Core";

        void Awake()
        {
            if (newRunButton != null) newRunButton.onClick.AddListener(HandleNewRun);
            if (settingsButton != null) settingsButton.onClick.AddListener(ShowSettings);
            if (creditsButton != null) creditsButton.onClick.AddListener(ShowCredits);
            if (quitButton != null) quitButton.onClick.AddListener(HandleQuit);
            if (backButton != null) backButton.onClick.AddListener(ShowMain);
            if (creditsBackButton != null) creditsBackButton.onClick.AddListener(ShowMain);
        }

        void Start()
        {
            Time.timeScale = 1f;
            PauseLock.SetBlocked(false);
            AudioState.ResetToDefault(); // D-220: coming back from an ending (Ending snapshot) -> Default
            Cursor.lockState = CursorLockMode.None; // mouse-driven menu (D-209)
            Cursor.visible = true;
            ShowMain();
        }

        void HandleNewRun()
        {
            if (loading) return;
            if (!Application.CanStreamedLevelBeLoaded(CoreScene))
            {
                Debug.LogError($"[Menu] Core scene '{CoreScene}' is not in Build Settings. Run TWR > Run > Build Endings + Main Menu.", this);
                return;
            }
            loading = true;
            SetInteractable(false);
            Debug.Log("[Menu] New run: loading Core (it loads the lobby next, D-210).", this);
            SceneManager.LoadSceneAsync(CoreScene, LoadSceneMode.Single);
        }

        void ShowSettings() => ShowOnly(settingsPanel);
        void ShowCredits() => ShowOnly(creditsPanel); // D-229
        void ShowMain() => ShowOnly(mainPanel);

        void ShowOnly(GameObject panel)
        {
            if (mainPanel != null) mainPanel.SetActive(panel == mainPanel);
            if (settingsPanel != null) settingsPanel.SetActive(panel == settingsPanel);
            if (creditsPanel != null) creditsPanel.SetActive(panel == creditsPanel);
        }

        void HandleQuit()
        {
            Debug.Log("[Menu] Quit (D-211).", this);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void SetInteractable(bool value)
        {
            if (newRunButton != null) newRunButton.interactable = value;
            if (settingsButton != null) settingsButton.interactable = value;
            if (creditsButton != null) creditsButton.interactable = value;
            if (quitButton != null) quitButton.interactable = value;
        }
    }
}
