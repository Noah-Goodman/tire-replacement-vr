using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// Mirrors whether <see cref="target"/> is active from the Assembler's device to the Fetcher's device through
    /// <see cref="SessionState.MirrorFlags"/>. The Assembler's device, where the training process toggles the target,
    /// reports changes. The Fetcher's device copies the flag. With no role or no session it does nothing.
    /// Put this component on an always-active GameObject, never on the target: an inactive target would stop the
    /// component's Update and LateUpdate, so the Fetcher could never see it activate.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActiveStateMirror : MonoBehaviour
    {
        private const float ResendIntervalSeconds = 0.5f;

        [SerializeField]
        private GameObject target;

        [SerializeField, Range(0, 31)]
        [Tooltip("Bit of SessionState.MirrorFlags used for this target. Unique per mirrored object.")]
        private int flagIndex;

        private bool started;
        private bool subscribedToRole;
        private Role role = Role.None;
        private SessionState bound;
        private float lastSendTime = float.NegativeInfinity;

        private void Start()
        {
            started = true;
            CheckRole();
        }

        private void OnEnable()
        {
            if (started)
            {
                CheckRole();
            }
        }

        private void OnDisable()
        {
            UnsubscribeRole();
            Unbind();
        }

        private void OnDestroy()
        {
            UnsubscribeRole();
            Unbind();
        }

        private void CheckRole()
        {
            if (role != Role.None || subscribedToRole)
            {
                return;
            }

            Role current = RoleBootstrap.LocalRole;
            if (current == Role.None)
            {
                RoleBootstrap.LocalRoleReady += OnLocalRoleReady;
                subscribedToRole = true;
            }
            else
            {
                role = current;
            }
        }

        private void OnLocalRoleReady(Role ready)
        {
            UnsubscribeRole();
            role = ready;
        }

        private void UnsubscribeRole()
        {
            if (subscribedToRole)
            {
                RoleBootstrap.LocalRoleReady -= OnLocalRoleReady;
                subscribedToRole = false;
            }
        }

        private void Update()
        {
            if (role == Role.None || target == null)
            {
                return;
            }

            if (bound != SessionState.Instance)
            {
                Bind();
            }
        }

        private void LateUpdate()
        {
            if (role != Role.Assembler || target == null || bound == null || !bound.Object.IsValid)
            {
                return;
            }

            bool active = target.activeSelf;
            if (active == bound.GetMirrorFlag(flagIndex))
            {
                return;
            }

            if (Time.unscaledTime - lastSendTime >= ResendIntervalSeconds)
            {
                lastSendTime = Time.unscaledTime;
                bound.RPC_SetMirrorFlag(flagIndex, active);
            }
        }

        private void Bind()
        {
            Unbind();
            bound = SessionState.Instance;
            if (bound != null)
            {
                bound.MirrorFlagsChanged += ApplyFlag;
                ApplyFlag();
            }
        }

        private void Unbind()
        {
            if (bound != null)
            {
                bound.MirrorFlagsChanged -= ApplyFlag;
                bound = null;
            }
        }

        private void ApplyFlag()
        {
            if (role == Role.Fetcher && bound != null && target != null)
            {
                target.SetActive(bound.GetMirrorFlag(flagIndex));
            }
        }
    }
}
