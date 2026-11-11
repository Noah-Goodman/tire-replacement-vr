using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// Switches objects and components off on the device of every role except <see cref="activeFor"/>.
    /// Once <see cref="RoleBootstrap.LocalRole"/> is known (already, or when <see cref="RoleBootstrap.LocalRoleReady"/>
    /// fires) and differs from <see cref="activeFor"/>, the listed objects are deactivated and the listed behaviours
    /// are disabled, once. With no network session the local role stays None and nothing is changed.
    /// The gate may sit on one of its own objects: it applies once and stops listening before that object goes inactive.
    /// </summary>
    public sealed class RoleGate : MonoBehaviour
    {
        [Tooltip("The role whose device keeps the targets on. The other role's device switches them off.")]
        [SerializeField] private Role activeFor = Role.Fetcher;

        [Tooltip("Deactivated on the device of the role that isn't 'Active For'.")]
        [SerializeField] private GameObject[] objects;

        [Tooltip("Disabled on the device of the role that isn't 'Active For'.")]
        [SerializeField] private Behaviour[] behaviours;

        private bool applied;

        private void OnEnable()
        {
            RoleBootstrap.LocalRoleReady += OnLocalRoleReady;
            Apply(RoleBootstrap.LocalRole);
        }

        private void OnDisable()
        {
            RoleBootstrap.LocalRoleReady -= OnLocalRoleReady;
        }

        private void OnLocalRoleReady(Role role)
        {
            Apply(role);
        }

        private void Apply(Role role)
        {
            if (applied || role == Role.None || role == activeFor)
            {
                return;
            }

            applied = true;
            RoleBootstrap.LocalRoleReady -= OnLocalRoleReady;

            if (behaviours != null)
            {
                foreach (Behaviour behaviour in behaviours)
                {
                    if (behaviour != null)
                    {
                        behaviour.enabled = false;
                    }
                }
            }

            if (objects != null)
            {
                foreach (GameObject target in objects)
                {
                    if (target != null)
                    {
                        target.SetActive(false);
                    }
                }
            }
        }
    }
}
