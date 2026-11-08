using TMPro;
using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// A role's portal in RoleSelectScene. Shows whether the role is free and calls
    /// <see cref="SessionLauncher.Join"/> once when the player's head enters the ring.
    /// Entry is the head's horizontal (XZ) distance to <see cref="center"/> being less than <see cref="radius"/>;
    /// no colliders are involved.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RolePortal : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly Color TakenRingColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        [SerializeField] private Role role = Role.Fetcher;
        [Tooltip("Falls back to the SessionLauncher found in the scene when unset.")]
        [SerializeField] private SessionLauncher launcher;
        [Tooltip("Ring radius in metres.")]
        [SerializeField] private float radius = 1.1f;
        [Tooltip("Center of the ring. Defaults to this transform.")]
        [SerializeField] private Transform center;
        [Tooltip("The player's head. Defaults to the main camera.")]
        [SerializeField] private Transform head;
        [SerializeField] private TMP_Text label;
        [Tooltip("Optional. Tinted with the role color, or grey when the role is taken.")]
        [SerializeField] private Renderer ringRenderer;

        private MaterialPropertyBlock block;
        private bool inside;

        public Role Role => role;

        private void Update()
        {
            if (launcher == null)
            {
                launcher = FindFirstObjectByType<SessionLauncher>();
            }

            bool connecting = launcher == null || !launcher.IsLobbyReady || launcher.IsJoining;
            bool available = !connecting && launcher.IsRoleAvailable(role);

            if (connecting)
            {
                ShowState("Connecting…", TakenRingColor);
            }
            else if (!available)
            {
                ShowState("TAKEN", TakenRingColor);
            }
            else
            {
                ShowState(role.GetDisplayName().ToUpperInvariant(), role.GetColor());
            }

            UpdateEntry(available);
        }

        private void UpdateEntry(bool available)
        {
            if (head == null && Camera.main != null)
            {
                head = Camera.main.transform;
            }

            if (head == null)
            {
                return;
            }

            Vector3 c = (center != null ? center : transform).position;
            Vector3 h = head.position;
            float dx = h.x - c.x;
            float dz = h.z - c.z;
            bool nowInside = dx * dx + dz * dz < radius * radius;

            if (!nowInside)
            {
                inside = false;
                return;
            }

            if (inside || !available)
            {
                return;
            }

            inside = true;
            launcher.Join(role);
        }

        private void ShowState(string text, Color ringColor)
        {
            if (label != null && label.text != text)
            {
                label.text = text;
            }

            if (ringRenderer != null)
            {
                block ??= new MaterialPropertyBlock();
                ringRenderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, ringColor);
                block.SetColor(ColorId, ringColor);
                ringRenderer.SetPropertyBlock(block);
            }
        }
    }
}
