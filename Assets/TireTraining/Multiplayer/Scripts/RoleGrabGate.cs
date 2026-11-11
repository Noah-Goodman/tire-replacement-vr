using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRBuilder.Core.Properties;

namespace TireTraining
{
    /// <summary>
    /// Select and hover filter on a shared item's root GameObject that decides which role's hand may grab it.
    /// It applies to hand interactors only; <see cref="XRSocketInteractor"/>s (including VR Builder snap zones) always
    /// pass, so storage sockets, car snap zones and the nut tray keep working for either role.
    /// Rules, per local role (everything is allowed with no network session or no role yet):
    /// the Fetcher may grab the item while it is <see cref="SessionState.Needed"/> or while the Fetcher already holds it;
    /// the Assembler may grab it while the Fetcher or the Assembler holds it, or when it is at the car, meaning its last
    /// recorded handoff went to the Assembler or, with no handoff yet, <see cref="startsAtCar"/>.
    /// An item that isn't spawned yet can't be grabbed by either role.
    /// The filter is re-checked every frame for an existing grab, so a grab that stops being allowed is dropped.
    /// On the Fetcher's device, where the VR Builder process doesn't run, the item's lockable properties are unlocked
    /// about once a second so the filter alone decides and the item can be stored in the garage sockets.
    /// The Assembler's device leaves the locks to the process.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoleGrabGate : MonoBehaviour, IXRSelectFilter, IXRHoverFilter
    {
        private const float UnlockIntervalSeconds = 1f;

        [Tooltip("True for an item that begins at the car (the old wheel), so the Assembler may grab it before any handoff.")]
        [SerializeField] private bool startsAtCar;

        private XRBaseInteractable interactable;
        private FetchItemTag itemTag;
        private float nextUnlockTime;
        private bool filtersAdded;

        public bool canProcess => isActiveAndEnabled;

        private void OnEnable()
        {
            interactable = GetComponent<XRBaseInteractable>();
            itemTag = GetComponent<FetchItemTag>();
            nextUnlockTime = 0f;

            if (interactable == null)
            {
                Debug.LogError($"RoleGrabGate: '{name}' has no XRBaseInteractable.", this);
                return;
            }

            interactable.selectFilters.Add(this);
            interactable.hoverFilters.Add(this);
            filtersAdded = true;
        }

        private void OnDisable()
        {
            if (filtersAdded && interactable != null)
            {
                interactable.selectFilters.Remove(this);
                interactable.hoverFilters.Remove(this);
            }

            filtersAdded = false;
        }

        private void Update()
        {
            if (RoleBootstrap.LocalRole != Role.Fetcher || Time.unscaledTime < nextUnlockTime)
            {
                return;
            }

            nextUnlockTime = Time.unscaledTime + UnlockIntervalSeconds;
            foreach (LockableProperty lockable in GetComponents<LockableProperty>())
            {
                if (lockable.IsLocked)
                {
                    lockable.SetLocked(false);
                }
            }
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable selectable)
        {
            return Allows(interactor);
        }

        public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable hoverable)
        {
            return Allows(interactor);
        }

        private bool Allows(object interactor)
        {
            if (interactor is XRSocketInteractor)
            {
                return true;
            }

            Role local = RoleBootstrap.LocalRole;
            if (local == Role.None)
            {
                return true;
            }

            NetworkGrabbable grabbable = itemTag != null ? itemTag.Grabbable : GetComponent<NetworkGrabbable>();
            if (grabbable == null || !grabbable.IsSpawned)
            {
                return false;
            }

            if (local == Role.Fetcher)
            {
                if (grabbable.Holder == Role.Fetcher)
                {
                    return true;
                }

                SessionState state = SessionState.Instance;
                return state != null && itemTag != null && itemTag.Item != FetchItem.None && state.Needed == itemTag.Item;
            }

            if (grabbable.Holder == Role.Fetcher || grabbable.Holder == Role.Assembler)
            {
                return true;
            }

            return grabbable.HandoffCount > 0 ? grabbable.LastHandoffTo == Role.Assembler : startsAtCar;
        }
    }
}
