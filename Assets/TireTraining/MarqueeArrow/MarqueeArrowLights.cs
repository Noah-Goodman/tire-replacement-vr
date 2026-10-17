using UnityEngine;

namespace TireTraining
{
    /// <summary>Independent marquee lighting. Connect these methods to your own events.</summary>
    [DisallowMultipleComponent]
    public sealed class MarqueeArrowLights : MonoBehaviour
    {
        [SerializeField, Tooltip("Lighting state applied when this instance awakens.")]
        private bool lightsOn;
        [SerializeField] private Renderer[] bulbRenderers;
        [SerializeField] private Material litMaterial;
        [SerializeField] private Material unlitMaterial;

        public bool IsOn => lightsOn;
        private void Awake() => ApplyState();
        private void OnValidate() => ApplyState();

        public void SetLightsOn(bool value)
        {
            lightsOn = value;
            ApplyState();
        }

        [ContextMenu("Lights/Turn On")]
        public void TurnOn() => SetLightsOn(true);
        [ContextMenu("Lights/Turn Off")]
        public void TurnOff() => SetLightsOn(false);
        public void ToggleLights() => SetLightsOn(!lightsOn);

        private void ApplyState()
        {
            var material = lightsOn ? litMaterial : unlitMaterial;
            if (material == null || bulbRenderers == null) return;
            foreach (var bulbRenderer in bulbRenderers)
                if (bulbRenderer != null) bulbRenderer.sharedMaterial = material;
        }
    }
}
