using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HindiHarfBuzzDemo
{
    public sealed class HindiToggleDemoBootstrap : MonoBehaviour
    {
        private static readonly string[] kHindiSamples =
        {
            "डॉक्टर श्रृंगारिका ने पूज्य धृतराष्ट्र को ऊँचे स्वर में क्षमा-याचना की।",
            "विज्ञान की सूक्ष्म वृत्तियाँ ढूँढ़ने को कहा, ताकि वे ज़िंदगी की कड़वाहट भूल सकें।",
            "ज्ञानी छात्र ने त्रुटि सुधारी, श्रम किया, और स्वरों में प्रार्थना दोहराई।"
        };

        private HarfBuzzTextMeshProUGUI m_Text;
        private FieldInfo m_EnableHarfBuzzField;
        private bool m_HarfBuzzEnabled = true;
        private int m_SampleIndex;
        private Text m_ButtonLabel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Object.FindAnyObjectByType<HindiToggleDemoBootstrap>() != null)
            {
                return;
            }

            var go = new GameObject("__HindiToggleDemoBootstrap");
            DontDestroyOnLoad(go);
            go.AddComponent<HindiToggleDemoBootstrap>();
        }

        private void Start()
        {
            m_Text = Object.FindAnyObjectByType<HarfBuzzTextMeshProUGUI>();
            if (m_Text == null)
            {
                Debug.LogWarning("Demo bootstrap could not find HarfBuzzTextMeshProUGUI in scene.");
                return;
            }

            m_EnableHarfBuzzField = typeof(HarfBuzzTextMeshProUGUI)
                .GetField("m_EnableHarfBuzz", BindingFlags.Instance | BindingFlags.NonPublic);

            ApplySample(0);
            SetHarfBuzzMode(true);
            CreateToggleButton();
            RefreshButtonLabel();
        }

        private void CreateToggleButton()
        {
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                return;
            }

            var buttonGo = new GameObject("ModeToggleButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(canvas.transform, false);

            var rect = (RectTransform)buttonGo.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -24f);
            rect.sizeDelta = new Vector2(360f, 64f);

            var image = buttonGo.GetComponent<Image>();
            image.color = new Color(0.1f, 0.18f, 0.3f, 0.92f);

            var button = buttonGo.GetComponent<Button>();
            button.onClick.AddListener(OnToggleClicked);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(buttonGo.transform, false);

            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 8f);
            labelRect.offsetMax = new Vector2(-8f, -8f);

            m_ButtonLabel = labelGo.GetComponent<Text>();
            m_ButtonLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            m_ButtonLabel.fontSize = 15;
            m_ButtonLabel.alignment = TextAnchor.MiddleCenter;
            m_ButtonLabel.color = Color.white;
        }

        private void OnToggleClicked()
        {
            m_HarfBuzzEnabled = !m_HarfBuzzEnabled;
            m_SampleIndex = (m_SampleIndex + 1) % kHindiSamples.Length;

            ApplySample(m_SampleIndex);
            SetHarfBuzzMode(m_HarfBuzzEnabled);
            RefreshButtonLabel();
        }

        private void ApplySample(int index)
        {
            m_Text.text = kHindiSamples[index];
            m_Text.ForceMeshUpdate();
        }

        private void SetHarfBuzzMode(bool enable)
        {
            if (m_EnableHarfBuzzField != null)
            {
                m_EnableHarfBuzzField.SetValue(m_Text, enable);
            }

            m_Text.SetVerticesDirty();
            m_Text.SetLayoutDirty();
            m_Text.ForceMeshUpdate();
        }

        private void RefreshButtonLabel()
        {
            if (m_ButtonLabel == null)
            {
                return;
            }

            var mode = m_HarfBuzzEnabled ? "HarfBuzzTMP" : "TMP";
            m_ButtonLabel.text = $"Mode: {mode} | Tap: toggle + next sample";
        }
    }
}
