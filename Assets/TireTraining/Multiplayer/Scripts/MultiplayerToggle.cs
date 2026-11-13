using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TireTraining
{
    /// <summary>
    /// Connects a uGUI <see cref="Toggle"/> in RoleSelectScene to <see cref="SessionLauncher.SoloMode"/>.
    /// The toggle on means multiplayer on, so it shows the inverse of SoloMode and sets SoloMode to the
    /// inverse of its value. It can't be changed while the launcher is joining.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MultiplayerToggle : MonoBehaviour
    {
        private const string OnText = "Multiplayer: ON";
        private const string OffText = "Multiplayer: OFF (solo)";

        [Tooltip("On means multiplayer on. Falls back to a Toggle on this GameObject when unset.")]
        [SerializeField] private Toggle toggle;
        [Tooltip("Optional. Reads Multiplayer: ON or Multiplayer: OFF (solo).")]
        [SerializeField] private TMP_Text stateLabel;
        [Tooltip("Optional. Falls back to the SessionLauncher found in the scene when unset.")]
        [SerializeField] private SessionLauncher launcher;

        private void OnEnable()
        {
            if (toggle == null)
            {
                toggle = GetComponent<Toggle>();
            }

            if (toggle != null)
            {
                toggle.onValueChanged.AddListener(OnToggleChanged);
            }

            SessionLauncher.SoloModeChanged += Refresh;
            Refresh();
            UpdateInteractable();
        }

        private void OnDisable()
        {
            if (toggle != null)
            {
                toggle.onValueChanged.RemoveListener(OnToggleChanged);
            }

            SessionLauncher.SoloModeChanged -= Refresh;
        }

        private void Update()
        {
            UpdateInteractable();
        }

        private void OnToggleChanged(bool isOn)
        {
            SessionLauncher.SoloMode = !isOn;
            Refresh();
        }

        private void Refresh()
        {
            bool multiplayer = !SessionLauncher.SoloMode;
            if (toggle != null)
            {
                toggle.SetIsOnWithoutNotify(multiplayer);
            }

            if (stateLabel != null)
            {
                stateLabel.text = multiplayer ? OnText : OffText;
            }
        }

        private void UpdateInteractable()
        {
            if (toggle == null)
            {
                return;
            }

            if (launcher == null)
            {
                launcher = FindFirstObjectByType<SessionLauncher>();
            }

            bool interactable = launcher == null || !launcher.IsJoining;
            if (toggle.interactable != interactable)
            {
                toggle.interactable = interactable;
            }
        }
    }
}
