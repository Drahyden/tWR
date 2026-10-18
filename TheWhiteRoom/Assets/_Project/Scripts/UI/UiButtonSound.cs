using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TWR.Audio;

namespace TWR.UI
{
    /// <summary>
    /// Plays a UI sound (AudioLibrary ID, default "ui.click") when the Button on this object is clicked. The sound plays on
    /// the audio bootstrap's 2D source in the UIState group (D-219), so it keeps playing while the menu loads another
    /// scene. The builders put it on every menu and end-screen button. No per-frame work.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class UiButtonSound : MonoBehaviour
    {
        [Tooltip("An ID from TWR.Audio.AudioIds, e.g. 'ui.click' or 'ui.back'.")]
        [SerializeField] string soundId = AudioIds.UiClick;

        Button button;
        UnityAction onClick;

        public string SoundId { get => soundId; set => soundId = value; }

        void Awake()
        {
            button = GetComponent<Button>();
            onClick = Play; // cached: no allocation on enable
        }

        void OnEnable() => button.onClick.AddListener(onClick);
        void OnDisable() => button.onClick.RemoveListener(onClick);

        void Play() => AudioBootstrap.PlayUi(soundId);
    }
}
