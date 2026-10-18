using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TWR.Audio;

namespace TWR.UI
{
    /// <summary>
    /// The Sound settings panel (D-225, part of D-090): five sliders 0..100 (Music, Ambience, Sound effects, Voice,
    /// Interface). Moving a slider changes the volume at once (SoundSettings.Set); leaving the screen (the panel is hidden
    /// or its scene unloads) saves to the D-212 save file (D-226). A self-contained panel: the main menu's Settings screen
    /// uses it now and the future pause menu (D-087) can reuse it as is. Titles and labels are localized by
    /// LocalizedWorldText on the labels (D-084); the numbers are plain digits. No per-frame work, no allocations after Awake.
    /// </summary>
    public class AudioSettingsPanel : MonoBehaviour
    {
        [Serializable]
        public struct Row
        {
            public AudioBus bus;
            public Slider slider;
            [Tooltip("Shows 0..100 next to the slider (optional).")]
            public TMP_Text valueLabel;
        }

        [SerializeField] Row[] rows = new Row[0];

        static string[] numbers; // "0".."100", made once
        UnityAction<float>[] handlers;

        void Awake()
        {
            if (numbers == null)
            {
                numbers = new string[101];
                for (int i = 0; i <= 100; i++) numbers[i] = i.ToString();
            }
            handlers = new UnityAction<float>[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                int index = i; // each slider gets its own handler, made once
                handlers[i] = value => HandleSliderChanged(index, value);
                Slider slider = rows[i].slider;
                if (slider == null) continue;
                slider.minValue = 0f;
                slider.maxValue = 100f;
                slider.wholeNumbers = true;
            }
        }

        void OnEnable()
        {
            for (int i = 0; i < rows.Length; i++)
            {
                Slider slider = rows[i].slider;
                if (slider == null) continue;
                int value = Mathf.RoundToInt(SoundSettings.Get(rows[i].bus) * 100f);
                slider.SetValueWithoutNotify(value);
                ShowValue(i, value);
                slider.onValueChanged.AddListener(handlers[i]);
            }
        }

        void OnDisable()
        {
            for (int i = 0; i < rows.Length; i++)
                if (rows[i].slider != null) rows[i].slider.onValueChanged.RemoveListener(handlers[i]);
            SoundSettings.SaveIfChanged(); // D-225: saved when leaving the screen
        }

        void HandleSliderChanged(int index, float value)
        {
            SoundSettings.Set(rows[index].bus, value / 100f); // live (D-225)
            ShowValue(index, Mathf.RoundToInt(value));
        }

        void ShowValue(int index, int value)
        {
            TMP_Text label = rows[index].valueLabel;
            if (label != null) label.text = numbers[Mathf.Clamp(value, 0, 100)];
        }
    }
}
