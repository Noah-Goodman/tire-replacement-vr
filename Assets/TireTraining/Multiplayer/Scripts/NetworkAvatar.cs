using Fusion;
using HUDIndicator;
using TMPro;
using UnityEngine;
using VRBuilder.XRInteraction.Animation;

namespace TireTraining
{
    /// <summary>
    /// A player's avatar: a head, two hands, a waypoint above the head and an edge-of-screen arrow. The owner
    /// (state authority) copies its rig's poses onto the head and hands, and the NetworkTransforms on them send the
    /// poses. The owner also networks each hand's grip and trigger values, read from the
    /// <see cref="HandAnimatorController"/> under its own controller, and every other device applies them to the
    /// Animator under the avatar's hand. Grip and trigger (Select, Activate) travel as bytes, 0 to 255 for 0 to 1;
    /// the UI and teleport modes travel as <see cref="NetworkBool"/>.
    ///
    /// On other devices the waypoint is placed <see cref="waypointHeight"/> above the head every frame, turned
    /// about the vertical axis to face the local camera, and shows the role's name, icon and color. The
    /// <see cref="indicator"/> shows the role's name, icon and color at the screen edge while the avatar is off
    /// screen. On the owner's device the waypoint root is deactivated, the indicator is hidden and every renderer is
    /// disabled, so each player sees only the other player's avatar.
    ///
    /// Every serialized reference is optional. Any <see cref="HandAnimatorController"/> under the avatar's hands is
    /// disabled on every device: it would drive the hand from the local controller's input.
    /// </summary>
    public sealed class NetworkAvatar : NetworkBehaviour
    {
        private const float ControllerSearchInterval = 1f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private Renderer[] roleRenderers;

        [Header("Hand animation")]
        [Tooltip("Animator float parameter for the grip value.")]
        [SerializeField] private string selectParameter = "Select";

        [Tooltip("Animator float parameter for the trigger value.")]
        [SerializeField] private string activateParameter = "Activate";

        [Tooltip("Animator bool parameter for the UI mode.")]
        [SerializeField] private string uiParameter = "UIEnabled";

        [Tooltip("Animator bool parameter for the teleport mode.")]
        [SerializeField] private string teleportParameter = "TeleportEnabled";

        [Header("Waypoint")]
        [Tooltip("Root of the waypoint shown above the head. Deactivated on the owner's device.")]
        [SerializeField] private Transform waypointRoot;

        [Tooltip("Shows the role's icon. A SpriteRenderer because the icon is a flat picture drawn in the world.")]
        [SerializeField] private SpriteRenderer waypointIcon;

        [Tooltip("Shows the role's display name.")]
        [SerializeField] private TMP_Text waypointLabel;

        [Tooltip("The downward chevron under the label.")]
        [SerializeField] private Renderer waypointChevron;

        [Tooltip("Metres above the head, along world up.")]
        [SerializeField] private float waypointHeight = 0.35f;

        [Tooltip("Icon for the Fetcher, used by the waypoint and the edge arrow.")]
        [SerializeField] private Sprite fetcherIcon;

        [Tooltip("Icon for the Assembler, used by the waypoint and the edge arrow.")]
        [SerializeField] private Sprite assemblerIcon;

        [Header("Edge arrow")]
        [Tooltip("HUD Indicator on the waypoint. Shows the avatar at the screen edge while it is off screen.")]
        [SerializeField] private Indicator indicator;

        [Networked, OnChangedRender(nameof(ApplyRole))] public Role AvatarRole { get; set; }

        [Networked] private byte LeftSelect { get; set; }
        [Networked] private byte LeftActivate { get; set; }
        [Networked] private NetworkBool LeftUI { get; set; }
        [Networked] private NetworkBool LeftTeleport { get; set; }
        [Networked] private byte RightSelect { get; set; }
        [Networked] private byte RightActivate { get; set; }
        [Networked] private NetworkBool RightUI { get; set; }
        [Networked] private NetworkBool RightTeleport { get; set; }

        private Role initialRole;
        private Transform headSource;
        private Transform leftSource;
        private Transform rightSource;
        private MaterialPropertyBlock block;
        private bool spawned;
        private bool isOwner;

        private HandAnimatorController leftSourceController;
        private HandAnimatorController rightSourceController;
        private bool leftWarned;
        private bool rightWarned;
        private float nextControllerSearch;

        private HandRig leftRig;
        private HandRig rightRig;

        /// <summary>An Animator under an avatar hand, with the parameter hashes it actually has (0 when missing).</summary>
        private sealed class HandRig
        {
            public Animator Animator;
            public int Select;
            public int Activate;
            public int UI;
            public int Teleport;
        }

        /// <summary>Called on the owner's device before the object spawns (from Runner.Spawn's onBeforeSpawned).</summary>
        public void Initialize(Role role, Transform headSource, Transform leftSource, Transform rightSource)
        {
            initialRole = role;
            this.headSource = headSource;
            this.leftSource = leftSource;
            this.rightSource = rightSource;
        }

        private void Awake()
        {
            // Disabled here so the controller's Start and Update never run on this copy.
            DisableHandControllers(leftHand);
            DisableHandControllers(rightHand);
        }

        public override void Spawned()
        {
            spawned = true;
            isOwner = Object.HasStateAuthority;
            leftRig = FindRig(leftHand);
            rightRig = FindRig(rightHand);

            if (isOwner)
            {
                AvatarRole = initialRole;

                // Renderers are disabled rather than the objects deactivated: the NetworkTransforms on the
                // head and hands must keep running to send their poses.
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = false;
                }
            }

            if (waypointRoot != null)
            {
                waypointRoot.gameObject.SetActive(!isOwner);
            }

            ConfigureIndicator();
            ApplyRole();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            spawned = false;
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority)
            {
                return;
            }

            CopyPose(headSource, head);
            CopyPose(leftSource, leftHand);
            CopyPose(rightSource, rightHand);
            WriteHands();
        }

        public override void Render()
        {
            if (isOwner)
            {
                return;
            }

            Apply(leftRig, LeftSelect, LeftActivate, LeftUI, LeftTeleport);
            Apply(rightRig, RightSelect, RightActivate, RightUI, RightTeleport);
        }

        // LateUpdate runs after Render, so the head's NetworkTransform has applied its pose.
        private void LateUpdate()
        {
            if (!spawned || isOwner || waypointRoot == null || head == null)
            {
                return;
            }

            Vector3 position = head.position + Vector3.up * waypointHeight;
            Quaternion rotation = waypointRoot.rotation;

            Camera cam = Camera.main;
            if (cam != null)
            {
                // The waypoint's +Z points away from the viewer, so text and sprites read correctly from the front.
                Vector3 away = position - cam.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-6f)
                {
                    rotation = Quaternion.LookRotation(away, Vector3.up);
                }
            }

            waypointRoot.SetPositionAndRotation(position, rotation);
        }

        private static void CopyPose(Transform source, Transform target)
        {
            if (source != null && target != null)
            {
                target.SetPositionAndRotation(source.position, source.rotation);
            }
        }

        private static void DisableHandControllers(Transform hand)
        {
            if (hand == null)
            {
                return;
            }

            foreach (HandAnimatorController controller in hand.GetComponentsInChildren<HandAnimatorController>(true))
            {
                controller.enabled = false;
            }
        }

        private HandRig FindRig(Transform hand)
        {
            Animator animator = hand != null ? hand.GetComponentInChildren<Animator>(true) : null;
            if (animator == null)
            {
                return null;
            }

            return new HandRig
            {
                Animator = animator,
                Select = ResolveParameter(animator, selectParameter, AnimatorControllerParameterType.Float),
                Activate = ResolveParameter(animator, activateParameter, AnimatorControllerParameterType.Float),
                UI = ResolveParameter(animator, uiParameter, AnimatorControllerParameterType.Bool),
                Teleport = ResolveParameter(animator, teleportParameter, AnimatorControllerParameterType.Bool)
            };
        }

        // Setting a parameter the Animator lacks logs a warning on every call, so only existing ones are written.
        private int ResolveParameter(Animator animator, string parameterName, AnimatorControllerParameterType type)
        {
            if (string.IsNullOrEmpty(parameterName))
            {
                return 0;
            }

            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.name == parameterName && p.type == type)
                {
                    return p.nameHash;
                }
            }

            Debug.LogWarning($"NetworkAvatar '{name}': Animator on '{animator.name}' has no {type} parameter '{parameterName}'.", this);
            return 0;
        }

        private static void Apply(HandRig rig, byte select, byte activate, bool ui, bool teleport)
        {
            if (rig == null || rig.Animator == null)
            {
                return;
            }

            if (rig.Select != 0)
            {
                rig.Animator.SetFloat(rig.Select, select / 255f);
            }

            if (rig.Activate != 0)
            {
                rig.Animator.SetFloat(rig.Activate, activate / 255f);
            }

            if (rig.UI != 0)
            {
                rig.Animator.SetBool(rig.UI, ui);
            }

            if (rig.Teleport != 0)
            {
                rig.Animator.SetBool(rig.Teleport, teleport);
            }
        }

        private void WriteHands()
        {
            if (leftSourceController == null || rightSourceController == null)
            {
                FindSourceControllers();
            }

            if (leftSourceController != null)
            {
                LeftSelect = Quantize(leftSourceController.SelectValue);
                LeftActivate = Quantize(leftSourceController.ActivateValue);
                LeftUI = leftSourceController.IsUIMode;
                LeftTeleport = leftSourceController.IsTeleportMode;
            }

            if (rightSourceController != null)
            {
                RightSelect = Quantize(rightSourceController.SelectValue);
                RightActivate = Quantize(rightSourceController.ActivateValue);
                RightUI = rightSourceController.IsUIMode;
                RightTeleport = rightSourceController.IsTeleportMode;
            }
        }

        private void FindSourceControllers()
        {
            if (Time.unscaledTime < nextControllerSearch)
            {
                return;
            }

            nextControllerSearch = Time.unscaledTime + ControllerSearchInterval;
            Search(leftSource, ref leftSourceController, ref leftWarned, "left");
            Search(rightSource, ref rightSourceController, ref rightWarned, "right");
        }

        private void Search(Transform source, ref HandAnimatorController controller, ref bool warned, string side)
        {
            if (controller != null || source == null)
            {
                return;
            }

            controller = source.GetComponentInChildren<HandAnimatorController>(true);
            if (controller == null && !warned)
            {
                warned = true;
                Debug.LogWarning($"NetworkAvatar '{name}': no HandAnimatorController under the {side} controller '{source.name}'; the {side} hand will not animate for the other player.", this);
            }
        }

        private static byte Quantize(float value)
        {
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
        }

        private void ConfigureIndicator()
        {
            if (indicator == null)
            {
                return;
            }

            indicator.ShowOnScreen = false;
            indicator.ShowOffScreen = true;
            indicator.ShowArrow = true;
            indicator.ShowDistance = true;
            indicator.Visible = !isOwner;
        }

        private Sprite IconFor(Role role)
        {
            switch (role)
            {
                case Role.Fetcher: return fetcherIcon;
                case Role.Assembler: return assemblerIcon;
                default: return null;
            }
        }

        private void ApplyRole()
        {
            Role role = AvatarRole;
            Color color = role.GetColor();
            Sprite icon = IconFor(role);

            block ??= new MaterialPropertyBlock();
            Tint(roleRenderers, color);

            if (waypointIcon != null)
            {
                waypointIcon.sprite = icon;
                waypointIcon.color = color;
            }

            if (waypointLabel != null)
            {
                waypointLabel.text = role.GetDisplayName();
                waypointLabel.color = color;
            }

            if (waypointChevron != null)
            {
                Tint(new[] { waypointChevron }, color);
            }

            if (indicator != null)
            {
                indicator.SetLabel(role.GetDisplayName());
                indicator.Icon.texture = icon != null ? icon.texture : null;
                indicator.Icon.color = color;
                indicator.ArrowStyle.color = color;
            }
        }

        private void Tint(Renderer[] renderers, Color color)
        {
            if (renderers == null)
            {
                return;
            }

            foreach (Renderer r in renderers)
            {
                if (r == null)
                {
                    continue;
                }

                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                r.SetPropertyBlock(block);
            }
        }
    }
}
