using System;
using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TireTraining
{
    /// <summary>
    /// Carried by every shared grabbable item. Moves state authority to the player who grabs the item, records who
    /// holds it and every hand-to-hand handoff, keeps the Rigidbody kinematic on devices without state authority,
    /// and hides the item from local snap zones while the other player holds it.
    /// Needs <b>Allow State Authority Override</b> on the NetworkObject. When the object is not spawned (no session),
    /// it does nothing at all.
    /// The grab interactable is found lazily: the serialized field, else one on this GameObject, checked every
    /// frame until one exists, because some items get theirs at runtime.
    /// </summary>
    public sealed class NetworkGrabbable : NetworkBehaviour, IStateAuthorityChanged
    {
        [Tooltip("Optional. The grab interactable to follow. When empty, the XRGrabInteractable on this GameObject is used, once it exists.")]
        [SerializeField] private XRGrabInteractable interactable;

        /// <summary>Role of the player holding the item with a hand; <see cref="Role.None"/> when nobody does.</summary>
        [Networked] public Role Holder { get; set; }

        /// <summary>Role that gave the item in the last hand-to-hand handoff.</summary>
        [Networked] public Role LastHandoffFrom { get; set; }

        /// <summary>Role that took the item in the last hand-to-hand handoff.</summary>
        [Networked] public Role LastHandoffTo { get; set; }

        /// <summary>Number of hand-to-hand handoffs so far. Raises <see cref="HandoffRecorded"/> when it changes.</summary>
        [Networked, OnChangedRender(nameof(OnHandoffChanged))] public int HandoffCount { get; set; }

        /// <summary>True while the item rests in a storage zone: a <see cref="StorageZone"/> sets it, and a hand grab clears it.</summary>
        [Networked] public NetworkBool Stored { get; set; }

        /// <summary>
        /// Raised on the taking device at the moment of a local hand grab while <see cref="Holder"/> is the other role.
        /// Arguments: (from = the holder's role, to = the local role). Does not wait for the network.
        /// </summary>
        public event Action<Role, Role> TakenFromOtherPlayer;

        /// <summary>
        /// Raised on every device whenever the networked handoff record changes, and once when the object spawns.
        /// Arguments: (<see cref="LastHandoffFrom"/>, <see cref="LastHandoffTo"/>, <see cref="HandoffCount"/>).
        /// A listener that cares only about handoffs after some moment compares the count with the one it saw then.
        /// </summary>
        public event Action<Role, Role, int> HandoffRecorded;

        /// <summary>
        /// Raised on the giving device when its hand grab is cancelled because the other player took the item.
        /// Arguments: (from = the local role, to = the role that took it).
        /// </summary>
        public event Action<Role, Role> GrabCancelledByHandoff;

        // Local state; not networked.
        private bool spawned;
        private XRGrabInteractable bound;
        private Rigidbody body;
        private bool bodyDefaultKinematic;
        private bool kinematicForced;
        private InteractionLayerMask savedLayers;
        private bool layersDropped;
        private bool pendingRequest;

        private bool HasState => spawned && Object != null && Object.IsValid;

        /// <summary>
        /// True between <see cref="Spawned"/> and <see cref="Despawned"/>, while the networked properties can be read.
        /// </summary>
        public bool IsSpawned => HasState;

        public override void Spawned()
        {
            spawned = true;
            TryBind();
            ApplyLocalRules();
            HandoffRecorded?.Invoke(LastHandoffFrom, LastHandoffTo, HandoffCount);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            Unbind(true);
            spawned = false;
            pendingRequest = false;
        }

        private void OnDestroy()
        {
            Unbind(true);
        }

        private void Update()
        {
            if (spawned && bound == null)
            {
                TryBind();
            }
        }

        // XRI and VR Builder rewrite Rigidbody.isKinematic on every select enter and exit
        // (XRGrabInteractable.SetupRigidbodyGrab/Drop and InteractableObject.OnSelectExiting), so the rule is
        // re-applied after they have run, every frame.
        private void LateUpdate()
        {
            if (spawned)
            {
                ApplyLocalRules();
            }
        }

        private void TryBind()
        {
            XRGrabInteractable found = interactable != null ? interactable : GetComponent<XRGrabInteractable>();
            if (found == null)
            {
                return;
            }

            bound = found;
            body = found.GetComponent<Rigidbody>();
            if (body != null)
            {
                bodyDefaultKinematic = body.isKinematic;
            }

            layersDropped = false;
            kinematicForced = false;
            found.selectEntered.AddListener(OnSelectEntered);
            found.selectExited.AddListener(OnSelectExited);
        }

        /// <summary>Stops following the interactable. With <paramref name="restore"/>, puts back the layers and kinematic value this component changed.</summary>
        private void Unbind(bool restore)
        {
            if (bound == null)
            {
                return;
            }

            bound.selectEntered.RemoveListener(OnSelectEntered);
            bound.selectExited.RemoveListener(OnSelectExited);

            if (restore)
            {
                if (layersDropped)
                {
                    bound.interactionLayers = savedLayers;
                }

                if (body != null && kinematicForced)
                {
                    body.isKinematic = bodyDefaultKinematic;
                }
            }

            bound = null;
            body = null;
            kinematicForced = false;
            layersDropped = false;
        }

        private static bool IsHand(IXRSelectInteractor interactor)
        {
            return interactor != null && !(interactor is XRSocketInteractor);
        }

        private bool IsHeldByLocalHand()
        {
            if (bound == null)
            {
                return false;
            }

            foreach (IXRSelectInteractor interactor in bound.interactorsSelecting)
            {
                if (IsHand(interactor))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsOtherRole(Role role, Role local)
        {
            return role != Role.None && local != Role.None && role != local;
        }

        /// <summary>Applies the kinematic rule every call. The layer rule writes only when "the other role holds the item" turns on (saves the current mask, sets Default only) or off (restores the saved mask).</summary>
        private void ApplyLocalRules()
        {
            if (!HasState || bound == null)
            {
                return;
            }

            if (body == null)
            {
                body = bound.GetComponent<Rigidbody>();
                if (body != null)
                {
                    bodyDefaultKinematic = body.isKinematic;
                }
            }

            if (body != null)
            {
                if (!Object.HasStateAuthority)
                {
                    if (!body.isKinematic)
                    {
                        body.isKinematic = true;
                    }

                    kinematicForced = true;
                }
                else if (kinematicForced && !bound.isSelected)
                {
                    // A grab begun while kinematic is forced makes XRI restore the forced value on release.
                    body.isKinematic = bodyDefaultKinematic;
                    kinematicForced = false;
                }
            }

            bool hide = IsOtherRole(Holder, RoleBootstrap.LocalRole);
            if (hide && !layersDropped)
            {
                savedLayers = bound.interactionLayers;
                bound.interactionLayers = (InteractionLayerMask)1;
                layersDropped = true;
            }
            else if (!hide && layersDropped)
            {
                bound.interactionLayers = savedLayers;
                layersDropped = false;
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (!HasState || !IsHand(args.interactorObject))
            {
                return;
            }

            Role local = RoleBootstrap.LocalRole;
            if (local == Role.None)
            {
                return;
            }

            Role holder = Holder;
            if (IsOtherRole(holder, local))
            {
                TakenFromOtherPlayer?.Invoke(holder, local);
            }

            if (Object.HasStateAuthority)
            {
                TakeAsHolder(local);
            }
            else
            {
                pendingRequest = true;
                Object.RequestStateAuthority();
            }
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (!HasState || !IsHand(args.interactorObject))
            {
                return;
            }

            if (Object.HasStateAuthority && !IsHeldByLocalHand() && RoleBootstrap.LocalRole != Role.None)
            {
                Holder = Role.None;
            }
        }

        /// <summary>Makes <paramref name="local"/> the holder, clears <see cref="Stored"/> and records a handoff if the item came straight from the other role. State authority only.</summary>
        private void TakeAsHolder(Role local)
        {
            Stored = false;
            Role previous = Holder;
            if (IsOtherRole(previous, local))
            {
                LastHandoffFrom = previous;
                LastHandoffTo = local;
                HandoffCount++;
            }

            Holder = local;
        }

        public void StateAuthorityChanged()
        {
            if (!HasState)
            {
                return;
            }

            Role local = RoleBootstrap.LocalRole;

            if (Object.HasStateAuthority)
            {
                bool wasRequested = pendingRequest;
                pendingRequest = false;
                if (local != Role.None)
                {
                    if (IsHeldByLocalHand())
                    {
                        TakeAsHolder(local);
                    }
                    else if (wasRequested && Holder != Role.None)
                    {
                        // The grab ended before authority arrived; the previous holder's grab was cancelled.
                        Holder = Role.None;
                    }
                }
            }
            else
            {
                pendingRequest = false;
                if (bound != null && IsHeldByLocalHand())
                {
                    CancelLocalGrab(local);
                }
            }

            ApplyLocalRules();
        }

        private void CancelLocalGrab(Role local)
        {
            XRInteractionManager manager = bound.interactionManager;
            if (manager == null)
            {
                return;
            }

            manager.CancelInteractableSelection((IXRSelectInteractable)bound);
            Role taker = Holder;
            GrabCancelledByHandoff?.Invoke(local, IsOtherRole(taker, local) ? taker : (local == Role.Fetcher ? Role.Assembler : Role.Fetcher));
        }

        private void OnHandoffChanged()
        {
            HandoffRecorded?.Invoke(LastHandoffFrom, LastHandoffTo, HandoffCount);
        }

        /// <summary>Sets <see cref="Stored"/>. Only the state authority writes it; on any other device this logs a warning and changes nothing.</summary>
        public void SetStored(bool stored)
        {
            if (!HasState)
            {
                Debug.LogWarning($"NetworkGrabbable '{name}': SetStored ignored because the object is not spawned.", this);
                return;
            }

            if (!Object.HasStateAuthority)
            {
                Debug.LogWarning($"NetworkGrabbable '{name}': SetStored ignored because this device has no state authority.", this);
                return;
            }

            Stored = stored;
        }
    }
}
