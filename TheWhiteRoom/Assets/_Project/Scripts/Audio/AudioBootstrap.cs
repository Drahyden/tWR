using UnityEngine;

namespace TWR.Audio
{
    /// <summary>
    /// Startup audio object (D-225), created by code before the first scene's Start, outside Core, so it works from the
    /// MainMenu and when Core is played directly in the editor. It survives scene loads (DontDestroyOnLoad) and:
    /// - in Start (AudioMixer.SetFloat is ignored in Awake) applies the saved sound volumes and the current snapshot;
    /// - owns the one 2D UI AudioSource (UIState group, D-219) that menus and the end screen play clicks on (PlayUi);
    /// - saves changed sound settings when the game quits, as a safety net.
    /// </summary>
    public class AudioBootstrap : MonoBehaviour
    {
        static AudioBootstrap instance;

        AudioSource uiSource;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateOnStartup()
        {
            if (instance != null) return;
            var go = new GameObject("TWR Audio (bootstrap, D-225)");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<AudioBootstrap>();
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            uiSource = gameObject.AddComponent<AudioSource>();
            uiSource.playOnAwake = false;
            uiSource.spatialBlend = 0f;
            uiSource.reverbZoneMix = 0f;
            uiSource.ignoreListenerPause = true; // menus keep clicking while the game is paused
            AudioRouting.Route(uiSource, AudioBus.UI);
        }

        void Start()
        {
            SoundSettings.ApplyAll(); // after Awake, or the mixer ignores it
            AudioState.Reapply();
        }

        void OnApplicationQuit() => SoundSettings.SaveIfChanged();

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>Play a UI sound from the AudioLibrary (e.g. AudioIds.UiClick) on the 2D UI source.</summary>
        public static void PlayUi(string soundId)
        {
            if (instance == null || instance.uiSource == null) return;
            AudioLibrary.PlayOneShot(instance.uiSource, soundId, 1f, true);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;
    }
}
