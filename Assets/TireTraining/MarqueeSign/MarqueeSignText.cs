using TMPro;
using UnityEngine;

namespace TireTraining
{
    /// <summary>One editable label shared by the two faces of a marquee sign.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class MarqueeSignText : MonoBehaviour
    {
        [SerializeField, TextArea(1, 5)] private string text = "YOUR TEXT";
        [SerializeField] private TextMeshPro frontText;
        [SerializeField] private TextMeshPro backText;

        public string Text
        {
            get => text;
            set => SetText(value);
        }

        private void OnEnable() => ApplyText();
        private void OnValidate() => ApplyText();

        public void SetText(string value)
        {
            text = value ?? string.Empty;
            ApplyText();
        }

        private void ApplyText()
        {
            if (frontText != null && frontText.text != text) frontText.text = text;
            if (backText != null && backText.text != text) backText.text = text;
        }
    }
}
