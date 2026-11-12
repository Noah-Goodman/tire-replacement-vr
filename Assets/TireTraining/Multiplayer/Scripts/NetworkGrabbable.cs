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
    /// it only keeps the Rigidbody interpolation rule for local hand holds.
    /// The grab interactable is found lazily: the serialized field, else one on this GameObject, checked every
    /// frame until one exists, because some items get theirs at runtime.
    /// </summary>
    public sealed class NetworkGrabbable : NetworkBehaviour, IStateAuthorityChanged
    {
        [Tooltip("Optional. The grab interactable to follow. When empty, the XRGrabInteractable on this GameObject is used, once it exists.")]
        [SerializeField] private XRGrabInteractable interactable;

        /// <summary>Role of the player holding the item with a hand; <see cref="Role.None"/> when nobody does.</summary>
        [Networked] public Role Holder { get; set; }

        /// <summary>Player whose hand set <see cref="Holder"/>; empty when nobody holds the item.</summary>
        [Networked] public PlayerRef HolderPlayer { get; set; }

        /// <summary>Room actor ID of the holding connection. Unlike PlayerRefs, actor IDs are not reused within a room.</summary>
        [Networked] public int HolderActorId { get; set; }

        /// <summary>Changes on each hand acquisition or release, independently of the handoff history.</summary>
        [Networked] public int HolderRevision { get; set; }

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
        private XRGrabInteractable handGrab;
        private Rigidbody handBody;
        private const float SocketAuthorityRequestIntervalSeconds = 1f;
        private float nextSocketAuthorityRequestTime;

        private const float RecoveryIntervalSeconds = 0.5f;
        private const float RecoveryTimeoutSeconds = 10f;
        private bool recoveryScanPending;
        private bool recoveryPending;
        private PlayerRef recoveryPlayer;
        private PlayerRef recoveryHolderPlayer;
        private int recoveryActorId;
        private int recoveryRevision;
        private Role recoveryRole;
        private TickTimer recoveryDeadline;
        private TickTimer recoveryRetry;

        private bool HasState => spawned && Object != null && Object.IsValid;

        /// <summary>
        /// True between <see cref="Spawned"/> and <see cref="Despawned"/>, while the networked properties can be read.
        /// </summary>
        public bool IsSpawned => HasState;

        public override void Spawned()
        {
            nextSocketAuthorityRequestTime = 0f;
            spawned = true;
            TryBind();
            ApplyLocalRules();
            recoveryScanPending = true;
            RequestRecoveryScan();
            HandoffRecorded?.Invoke(LastHandoffFrom, LastHandoffTo, HandoffCount);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            nextSocketAuthorityRequestTime = 0f;
            Unbind(true);
            spawned = false;
            pendingRequest = false;
            recoveryScanPending = false;
            CancelDepartureRecovery();
        }

        private void OnEnable()
        {
            // OnEnable also runs before Spawned, when the networked properties cannot be read.
            recoveryScanPending = true;
            if (HasState)
            {
                RequestRecoveryScan();
            }
        }

        private void OnDisable()
        {
            if (handBody != null)
            {
                handBody.interpolation = RigidbodyInterpolation.None;
            }

            pendingRequest = false;
            recoveryScanPending = true;
            CancelDepartureRecovery();
        }

        private void OnDestroy()
        {
            spawned = false;
            pendingRequest = false;
            CancelDepartureRecovery();
            Unbind(true);
        }

        private void Update()
        {
            if (spawned && bound == null)
            {
                TryBind();
            }

            if (HasState && recoveryScanPending)
            {
                RequestRecoveryScan();
            }
        }

        // XRI and VR Builder rewrite Rigidbody.isKinematic on every select enter and exit
        // (XRGrabInteractable.SetupRigidbodyGrab/Drop and InteractableObject.OnSelectExiting), so the rule is
        // re-applied after they have run, every frame.
        private void LateUpdate()
        {
            ApplyHandHoldInterpolation();
            if (spawned)
            {
                ApplyLocalRules();
            }
        }

        /// <summary>
        /// Interpolation smooths a Rigidbody that XRI moves on 50 Hz physics steps, which a hand-held item needs.
        /// An item carried by its parent (the car lifting a wheel) or by a socket is moved through its Transform
        /// and trails the carrier when interpolated, so only a local hand hold interpolates. XRI saves the value
        /// when the first selection begins and restores it when the last ends; this runs after both every frame,
        /// so the restored value is overwritten. It needs no session: it reads the grab interactable and
        /// Rigidbody itself, so it also applies to an item that is never spawned.
        /// </summary>
        private void ApplyHandHoldInterpolation()
        {
            XRGrabInteractable grab = bound != null ? bound : handGrab;
            if (grab == null)
            {
                grab = interactable != null ? interactable : GetComponent<XRGrabInteractable>();
                handGrab = grab;
                if (grab == null)
                {
                    return;
                }
            }

            if (handBody == null)
            {
                handBody = grab.GetComponent<Rigidbody>();
                if (handBody == null)
                {
                    return;
                }
            }

            bool held = false;
            foreach (IXRSelectInteractor selecting in grab.interactorsSelecting)
            {
                if (IsHand(selecting))
                {
                    held = true;
                    break;
                }
            }

            RigidbodyInterpolation wanted = held ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            if (handBody.interpolation != wanted)
            {
                handBody.interpolation = wanted;
            }
        }

        private void TryBind()
        {
            XRGrabInteractable found = interactable != null ? interactable : GetComponent<XRGrabInteractable>();
            if (found == null)
            {
                return;
            }

            if (found == bound)
            {
                return;
            }

            Unbind(true);

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

                if (body != null)
                {
                    body.interpolation = RigidbodyInterpolation.None;
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

            RequestLocalSocketAuthority();

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

        /// <summary>
        /// RoleGate enables car sockets only for the Assembler and garage sockets only for the Fetcher.
        /// A local socket may own an unheld item; hand acquisition and a remote holder always take precedence.
        /// </summary>
        private void RequestLocalSocketAuthority()
        {
            if (!HasState || !isActiveAndEnabled || bound == null || RoleBootstrap.LocalRole == Role.None
                || Object.HasStateAuthority || Holder != Role.None || pendingRequest || IsHeldByLocalHand()
                || Time.unscaledTime < nextSocketAuthorityRequestTime)
            {
                return;
            }

            foreach (IXRSelectInteractor selecting in bound.interactorsSelecting)
            {
                if (selecting is XRSocketInteractor socket && socket.isActiveAndEnabled && socket.socketActive
                    && socket.IsSelecting((IXRSelectInteractable)bound))
                {
                    nextSocketAuthorityRequestTime = Time.unscaledTime + SocketAuthorityRequestIntervalSeconds;
                    Object.RequestStateAuthority();
                    return;
                }
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (!HasState || !isActiveAndEnabled)
            {
                return;
            }

            CancelDepartureRecovery();
            if (!IsHand(args.interactorObject))
            {
                return;
            }

            Role local = RoleBootstrap.LocalRole;
            if (local == Role.None)
            {
                return;
            }

            Role holder = Holder;
            if (IsOtherRole(holder, local) && HasLiveHolder())
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
            if (!HasState || !isActiveAndEnabled || !IsHand(args.interactorObject))
            {
                return;
            }

            if (Object.HasStateAuthority && !IsHeldByLocalHand() && RoleBootstrap.LocalRole != Role.None)
            {
                ClearHolder();
            }
        }

        /// <summary>Makes <paramref name="local"/> the holder, clears <see cref="Stored"/> and records a handoff if the item came straight from the other role. State authority only.</summary>
        private void TakeAsHolder(Role local)
        {
            Stored = false;
            Role previous = Holder;
            if (IsOtherRole(previous, local) && HasLiveHolder())
            {
                LastHandoffFrom = previous;
                LastHandoffTo = local;
                HandoffCount++;
            }

            Holder = local;
            HolderPlayer = Runner.LocalPlayer;
            HolderActorId = Runner.GetPlayerActorId(Runner.LocalPlayer).GetValueOrDefault();
            HolderRevision++;
            CancelDepartureRecovery();
        }

        private void ClearHolder()
        {
            Holder = Role.None;
            HolderPlayer = PlayerRef.None;
            HolderActorId = 0;
            HolderRevision++;
        }

        private bool HasLiveHolder()
        {
            return !HolderPlayer.IsNone && Runner.IsPlayerCommitted(HolderPlayer)
                && (HolderActorId == 0 || Runner.GetPlayerActorId(HolderPlayer) == HolderActorId);
        }

        public void StateAuthorityChanged()
        {
            if (!HasState || !isActiveAndEnabled)
            {
                return;
            }

            Role local = RoleBootstrap.LocalRole;

            recoveryScanPending = true;
            RequestRecoveryScan();

            if (Object.HasStateAuthority)
            {
                bool wasRequested = pendingRequest;
                pendingRequest = false;
                ContinueDepartureRecovery();
                if (local != Role.None)
                {
                    if (IsHeldByLocalHand())
                    {
                        TakeAsHolder(local);
                    }
                    else if (wasRequested && !recoveryPending && Holder != Role.None)
                    {
                        // The grab ended before authority arrived; the previous holder's grab was cancelled.
                        ClearHolder();
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

        private void RequestRecoveryScan()
        {
            SessionState state = SessionState.Instance;
            if (state != null && state.TryQueueItemRecovery(this))
            {
                recoveryScanPending = false;
            }
        }

        /// <summary>Captures the held connection before SessionState clears its role; no network writes occur here.</summary>
        internal bool BeginDepartureRecovery(PlayerRef leavingPlayer, Role leavingRole)
        {
            if (!HasState || !isActiveAndEnabled || Stored || Holder == Role.None)
            {
                return false;
            }

            PlayerRef player = HolderPlayer;
            if (player.IsNone && Holder == leavingRole)
            {
                player = leavingPlayer;
            }

            if (player.IsNone || (!leavingPlayer.IsNone && player != leavingPlayer))
            {
                return false;
            }

            if (bound != null && bound.isSelected)
            {
                return false;
            }

            // A positive actor ID distinguishes a departed connection from a reused PlayerRef.
            // Missing actor information with a committed player is inconclusive, never a reason to drop it.
            if (leavingPlayer.IsNone && Runner.IsPlayerCommitted(player))
            {
                int? actor = Runner.GetPlayerActorId(player);
                if (HolderActorId == 0 || (actor.HasValue && actor.Value == HolderActorId))
                {
                    return false;
                }
            }

            if (recoveryPending && recoveryRevision == HolderRevision && recoveryHolderPlayer == HolderPlayer
                && recoveryActorId == HolderActorId && recoveryRole == Holder)
            {
                return true;
            }

            recoveryPending = true;
            recoveryPlayer = player;
            recoveryHolderPlayer = HolderPlayer;
            recoveryActorId = HolderActorId;
            recoveryRevision = HolderRevision;
            recoveryRole = Holder;
            recoveryDeadline = TickTimer.CreateFromSeconds(Runner, RecoveryTimeoutSeconds);
            recoveryRetry = default;
            return true;
        }

        /// <summary>Called only for queued items by the master; true means an asynchronous recovery is still pending.</summary>
        internal bool ContinueDepartureRecovery()
        {
            if (!recoveryPending)
            {
                return false;
            }

            if (!HasState || !isActiveAndEnabled || !Runner.IsSharedModeMasterClient || Stored
                || Holder != recoveryRole || HolderPlayer != recoveryHolderPlayer
                || HolderActorId != recoveryActorId || HolderRevision != recoveryRevision
                || (bound != null && bound.isSelected))
            {
                CancelDepartureRecovery();
                return false;
            }

            if (recoveryDeadline.Expired(Runner))
            {
                Debug.LogWarning($"NetworkGrabbable '{name}': departed-holder recovery timed out without changing the item.", this);
                CancelDepartureRecovery();
                return false;
            }

            if (Runner.IsPlayerCommitted(recoveryPlayer))
            {
                int? actor = Runner.GetPlayerActorId(recoveryPlayer);
                if (!actor.HasValue)
                {
                    return true;
                }

                if (recoveryActorId == 0 || actor.Value == recoveryActorId)
                {
                    // PlayerLeft may precede the committed-player update. Wait rather than assuming its order.
                    return true;
                }
            }

            if (bound == null)
            {
                TryBind();
                if (bound == null || bound.isSelected)
                {
                    return true;
                }
            }

            if (body == null)
            {
                body = bound.GetComponent<Rigidbody>();
                if (body == null)
                {
                    return true;
                }

                bodyDefaultKinematic = body.isKinematic;
            }

            if (!Object.HasStateAuthority)
            {
                PlayerRef authority = Object.StateAuthority;
                if (!authority.IsNone && Runner.IsPlayerCommitted(authority))
                {
                    if (authority == Runner.LocalPlayer)
                    {
                        return true;
                    }

                    // A live player has taken authority, possibly before its holder snapshot has arrived.
                    CancelDepartureRecovery();
                    return false;
                }

                if (recoveryRetry.ExpiredOrNotRunning(Runner))
                {
                    recoveryRetry = TickTimer.CreateFromSeconds(Runner, RecoveryIntervalSeconds);
                    Object.RequestStateAuthority();
                }

                return true;
            }

            ClearHolder();
            pendingRequest = false;
            CancelDepartureRecovery();
            // This explicit drop does not replace the Rigidbody defaults cached before proxy forcing.
            body.useGravity = true;
            body.isKinematic = false;
            kinematicForced = false;

            ApplyLocalRules();
            return false;
        }

        internal void CancelDepartureRecovery()
        {
            recoveryPending = false;
            recoveryPlayer = PlayerRef.None;
            recoveryHolderPlayer = PlayerRef.None;
            recoveryRole = Role.None;
            recoveryActorId = 0;
            recoveryRevision = 0;
            recoveryDeadline = default;
            recoveryRetry = default;
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
            if (stored)
            {
                CancelDepartureRecovery();
            }
        }
    }
}
