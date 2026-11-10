using Fusion;
using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// Shares the screw jack's lift height (which moves the car) and crank angle. Goes on the same GameObject as
    /// <see cref="ScrewJackController"/> and <see cref="NetworkGrabbable"/>; grab authority and the holder are
    /// <see cref="NetworkGrabbable"/>'s job. The state authority writes both values from the controller. Every other
    /// device applies them through <see cref="ScrewJackController.SetHeightNormalized"/> and
    /// <see cref="ScrewJackController.SetCrankAngle"/>. While the Assembler's device has cranking enabled and no state
    /// authority, it requests authority; it keeps its own local values until authority arrives.
    /// Needs <b>Allow State Authority Override</b> on the NetworkObject. When not spawned (no session), it does nothing.
    /// </summary>
    [RequireComponent(typeof(ScrewJackController))]
    public sealed class NetworkJack : NetworkBehaviour
    {
        private const float RequestIntervalSeconds = 1f;
        private const float WriteTolerance = 0.0005f;

        [SerializeField] private ScrewJackController controller;

        /// <summary>Lift height from 0 (on the ground) to 1 (fully raised).</summary>
        [Networked, OnChangedRender(nameof(OnHeightChanged))] public float HeightNormalized { get; set; }

        /// <summary>Visual crank angle in degrees.</summary>
        [Networked, OnChangedRender(nameof(OnCrankChanged))] public float CrankAngle { get; set; }

        private bool spawned;
        private float nextRequestTime;

        private bool HasState => spawned && Object != null && Object.IsValid;

        public override void Spawned()
        {
            spawned = true;
            if (controller == null)
            {
                controller = GetComponent<ScrewJackController>();
            }

            if (Object.HasStateAuthority)
            {
                // A device that already holds authority on spawn publishes its own values (the first device in the session).
                WriteFromController();
            }
            else
            {
                ApplyToController();
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            spawned = false;
        }

        private void Update()
        {
            if (!HasState || controller == null)
            {
                return;
            }

            if (Object.HasStateAuthority)
            {
                WriteFromController();
            }
            else if (IsLocalCranker && Time.unscaledTime >= nextRequestTime)
            {
                nextRequestTime = Time.unscaledTime + RequestIntervalSeconds;
                Object.RequestStateAuthority();
            }
        }

        public override void Render()
        {
            if (HasState && !Object.HasStateAuthority)
            {
                ApplyToController();
            }
        }

        private bool IsLocalCranker => controller != null && controller.CrankingEnabled
            && RoleBootstrap.LocalRole == Role.Assembler;

        private void WriteFromController()
        {
            if (controller == null)
            {
                return;
            }

            float height = controller.HeightNormalized;
            if (Mathf.Abs(height - HeightNormalized) > WriteTolerance)
            {
                HeightNormalized = height;
            }

            float angle = controller.CrankAngle;
            if (Mathf.Abs(angle - CrankAngle) > WriteTolerance)
            {
                CrankAngle = angle;
            }
        }

        /// <summary>Applies the networked values to the controller. A device that is cranking keeps its own values.</summary>
        private void ApplyToController()
        {
            if (!HasState || controller == null || Object.HasStateAuthority || IsLocalCranker)
            {
                return;
            }

            if (Mathf.Abs(controller.HeightNormalized - HeightNormalized) > WriteTolerance)
            {
                controller.SetHeightNormalized(HeightNormalized);
            }

            if (Mathf.Abs(controller.CrankAngle - CrankAngle) > WriteTolerance)
            {
                controller.SetCrankAngle(CrankAngle);
            }
        }

        private void OnHeightChanged()
        {
            ApplyToController();
        }

        private void OnCrankChanged()
        {
            ApplyToController();
        }
    }
}
