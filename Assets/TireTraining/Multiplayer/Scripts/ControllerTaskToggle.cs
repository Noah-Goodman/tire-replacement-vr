using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TireTraining
{
    /// <summary>
    /// Lets the local Fetcher press X (the left controller's primary button) to hide or show the task on their
    /// controller board. Hidden, the board keeps only what lies outside <see cref="taskBody"/> (the show/hide hint)
    /// and its background is turned off; shown, both come back.
    ///
    /// The component owns one button action bound to <c>&lt;XRController&gt;{LeftHand}/{PrimaryButton}</c>. The action
    /// is enabled only while <see cref="RoleBootstrap.LocalRole"/> is <see cref="Role.Fetcher"/> and this component is
    /// enabled; each press toggles once, and holding the button doesn't repeat. No shared action map is touched.
    ///
    /// The task starts shown on every MainScene load and keeps its state through component disable and enable and
    /// through task text changes. Which GameObject <see cref="RoleBoard"/> turns on and off is left alone. When either
    /// reference is missing, the component logs one warning and does nothing.
    /// </summary>
    public sealed class ControllerTaskToggle : MonoBehaviour
    {
        private const string ToggleBinding = "<XRController>{LeftHand}/{PrimaryButton}";

        [Tooltip("The group holding the task's accent, label and text. Hidden while the task is hidden.")]
        [SerializeField] private GameObject taskBody;

        [Tooltip("The board panel's opaque background. Turned off while the task is hidden.")]
        [SerializeField] private Image panelBackground;

        private InputAction toggleAction;
        private bool shown = true;
        private bool warnedMissing;

        private void Awake()
        {
            toggleAction = new InputAction("Toggle Controller Task", InputActionType.Button, ToggleBinding);
            toggleAction.performed += OnTogglePerformed;
        }

        private void OnEnable()
        {
            RoleBootstrap.LocalRoleReady += OnLocalRoleReady;
            if (HasReferences())
            {
                Apply();
            }

            UpdateActionEnabled();
        }

        private void OnDisable()
        {
            RoleBootstrap.LocalRoleReady -= OnLocalRoleReady;
            toggleAction?.Disable();
        }

        private void OnDestroy()
        {
            if (toggleAction != null)
            {
                toggleAction.performed -= OnTogglePerformed;
                toggleAction.Dispose();
                toggleAction = null;
            }
        }

        private void Update()
        {
            // RoleBootstrap clears the role without raising an event, so follow it here as well.
            UpdateActionEnabled();
        }

        private void OnLocalRoleReady(Role ready)
        {
            UpdateActionEnabled();
        }

        private void UpdateActionEnabled()
        {
            if (toggleAction == null)
            {
                return;
            }

            bool wanted = isActiveAndEnabled && RoleBootstrap.LocalRole == Role.Fetcher && HasReferences();
            if (wanted && !toggleAction.enabled)
            {
                toggleAction.Enable();
            }
            else if (!wanted && toggleAction.enabled)
            {
                toggleAction.Disable();
            }
        }

        private void OnTogglePerformed(InputAction.CallbackContext context)
        {
            if (RoleBootstrap.LocalRole != Role.Fetcher || !HasReferences())
            {
                return;
            }

            shown = !shown;
            Apply();
        }

        private void Apply()
        {
            taskBody.SetActive(shown);
            panelBackground.enabled = shown;
        }

        private bool HasReferences()
        {
            if (taskBody != null && panelBackground != null)
            {
                return true;
            }

            if (!warnedMissing)
            {
                warnedMissing = true;
                Debug.LogWarning($"ControllerTaskToggle '{name}': assign both taskBody and panelBackground; the task toggle does nothing until then.", this);
            }

            return false;
        }
    }
}
