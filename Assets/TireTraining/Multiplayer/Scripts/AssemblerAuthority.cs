using Fusion;
using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// Gives the Assembler's device state authority over a NetworkObject that only the Assembler changes and nobody
    /// grabs (the original lug nuts, which <see cref="SnappedWrenchTurn"/> moves). While spawned, the device whose
    /// <see cref="RoleBootstrap.LocalRole"/> is <see cref="Role.Assembler"/> requests state authority whenever it lacks
    /// it: on spawn, when the role becomes known, and again at most once a second. Other devices do nothing, and
    /// authority is never released. Needs <b>Allow State Authority Override</b> on the NetworkObject. When not spawned
    /// (no session), it does nothing.
    /// </summary>
    public sealed class AssemblerAuthority : NetworkBehaviour
    {
        private const float RequestIntervalSeconds = 1f;

        private bool spawned;
        private bool subscribed;
        private float nextRequestTime;

        private bool HasState => spawned && Object != null && Object.IsValid;

        public override void Spawned()
        {
            spawned = true;
            nextRequestTime = 0f;
            if (!subscribed)
            {
                RoleBootstrap.LocalRoleReady += OnLocalRoleReady;
                subscribed = true;
            }

            TryRequest();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            spawned = false;
            Unsubscribe();
        }

        private void OnDestroy()
        {
            spawned = false;
            Unsubscribe();
        }

        private void Update()
        {
            TryRequest();
        }

        private void Unsubscribe()
        {
            if (subscribed)
            {
                RoleBootstrap.LocalRoleReady -= OnLocalRoleReady;
                subscribed = false;
            }
        }

        private void OnLocalRoleReady(Role role)
        {
            TryRequest();
        }

        /// <summary>Requests authority when this is the Assembler's device and it lacks authority, at most once per interval.</summary>
        private void TryRequest()
        {
            if (!HasState || Object.HasStateAuthority || RoleBootstrap.LocalRole != Role.Assembler)
            {
                return;
            }

            if (Time.unscaledTime < nextRequestTime)
            {
                return;
            }

            nextRequestTime = Time.unscaledTime + RequestIntervalSeconds;
            Object.RequestStateAuthority();
        }
    }
}
