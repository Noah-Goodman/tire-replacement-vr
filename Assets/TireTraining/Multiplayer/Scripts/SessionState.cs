using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// Session-wide networked state, on a NetworkObject in MainScene with the Master Client Object flag.
    /// The current master client has state authority over it and is the only device that writes it.
    /// Holds which player has each role and publishes the taken roles as session properties for the lobby.
    /// </summary>
    public sealed class SessionState : NetworkBehaviour, IPlayerLeft, IStateAuthorityChanged
    {
        /// <summary>The spawned instance on this device, or null.</summary>
        public static SessionState Instance { get; private set; }

        /// <summary>Raised on this device after the role assignment changes and when this object spawns.</summary>
        public event Action RolesChanged;

        [Networked, OnChangedRender(nameof(OnRolesChanged))] public PlayerRef Fetcher { get; set; }
        [Networked, OnChangedRender(nameof(OnRolesChanged))] public PlayerRef Assembler { get; set; }

        /// <summary>Raised on this device after the training progress changes and when this object spawns.</summary>
        public event Action ProgressChanged;

        /// <summary>
        /// Raised on every device, the sender's included, when <see cref="RPC_Celebrate"/> arrives. The argument is the
        /// index of the chapter that was just completed. Static so a scene component can subscribe before this object spawns.
        /// </summary>
        public static event Action<int> Celebrated;

        /// <summary>Index of the chapter the Assembler's process is in.</summary>
        [Networked, OnChangedRender(nameof(OnProgressChanged))] public int Chapter { get; set; }

        /// <summary>True once the Assembler's process has finished.</summary>
        [Networked, OnChangedRender(nameof(OnProgressChanged))] public NetworkBool Finished { get; set; }

        /// <summary>Name of the step the Assembler's process is in; empty when there is none.</summary>
        [Networked, OnChangedRender(nameof(OnProgressChanged))] public NetworkString<_64> Step { get; set; }

        [Tooltip("The asset that maps steps to role texts and items.")]
        [SerializeField] private RoleStepText stepText;

        /// <summary>The item the Fetcher works with in the current step, looked up in the step text asset.</summary>
        [Networked, OnChangedRender(nameof(OnProgressChanged))] public FetchItem Needed { get; set; }

        private bool warnedNoStepText;

        /// <summary>Raised on this device after <see cref="MirrorFlags"/> changes and when this object spawns.</summary>
        public event Action MirrorFlagsChanged;

        /// <summary>Bit i is set when mirrored object i is active on the Assembler's device.</summary>
        [Networked, OnChangedRender(nameof(OnMirrorFlagsChanged))] public int MirrorFlags { get; set; }

        private const float ResendIntervalSeconds = 0.5f;
        private const float WarnAfterSeconds = 5f;
        private const float ConfirmWindowSeconds = 1.5f;

        // Local reconcile state; not networked.
        private bool reconciling;
        private bool diverged;
        private bool warned;
        private TickTimer resendTimer;
        private TickTimer warnTimer;
        private TickTimer confirmTimer;

        public override void Spawned()
        {
            Instance = this;

            if (Object.HasStateAuthority)
            {
                SweepGonePlayers();
                PublishSessionProperties();
            }

            RolesChanged?.Invoke();
            ProgressChanged?.Invoke();
            MirrorFlagsChanged?.Invoke();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            StopReconciling();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnRolesChanged()
        {
            RolesChanged?.Invoke();
        }

        private void OnProgressChanged()
        {
            ProgressChanged?.Invoke();
        }

        private void OnMirrorFlagsChanged()
        {
            MirrorFlagsChanged?.Invoke();
        }

        /// <summary>True when bit <paramref name="index"/> of <see cref="MirrorFlags"/> is set; false for an index outside 0-31.</summary>
        public bool GetMirrorFlag(int index)
        {
            if (index < 0 || index > 31)
            {
                return false;
            }

            return (MirrorFlags & (1 << index)) != 0;
        }

        /// <summary>Sets or clears one mirror flag. The state authority stores it. An index outside 0-31 is ignored.</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_SetMirrorFlag(int index, NetworkBool active)
        {
            if (index < 0 || index > 31)
            {
                return;
            }

            int bit = 1 << index;
            MirrorFlags = active ? (MirrorFlags | bit) : (MirrorFlags & ~bit);
        }

        /// <summary>
        /// Sent by the Assembler's device whenever its process starts a chapter or step or finishes. The state
        /// authority stores the values. A negative chapter is ignored. A report with finished false does not clear
        /// Finished unless the chapter also changes, so a late step report cannot undo a finished process.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ReportProgress(int chapter, NetworkString<_64> step, NetworkBool finished)
        {
            if (chapter < 0)
            {
                return;
            }

            bool chapterChanged = chapter != Chapter;
            if (chapterChanged)
            {
                Chapter = chapter;
            }

            Step = step;

            if (stepText != null)
            {
                Needed = stepText.GetItem(Chapter, step.ToString());
            }
            else
            {
                Needed = FetchItem.None;
                if (!warnedNoStepText)
                {
                    warnedNoStepText = true;
                    Debug.LogWarning("SessionState: no RoleStepText assigned; Needed stays None.", this);
                }
            }

            if (finished || chapterChanged)
            {
                Finished = finished;
            }
        }

        /// <summary>
        /// Tells every device to play the celebration. <paramref name="chapter"/> is the index of the chapter that
        /// was just completed, which is the checklist row that just got ticked.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_Celebrate(int chapter)
        {
            Celebrated?.Invoke(chapter);
        }

        /// <summary>The player holding <paramref name="role"/>, or <see cref="PlayerRef.None"/>.</summary>
        public PlayerRef GetPlayer(Role role)
        {
            switch (role)
            {
                case Role.Fetcher: return Fetcher;
                case Role.Assembler: return Assembler;
                default: return PlayerRef.None;
            }
        }

        /// <summary>The role held by <paramref name="player"/>, or <see cref="Role.None"/>.</summary>
        public Role GetRole(PlayerRef player)
        {
            if (player.IsNone)
            {
                return Role.None;
            }

            if (Fetcher == player)
            {
                return Role.Fetcher;
            }

            return Assembler == player ? Role.Assembler : Role.None;
        }

        /// <summary>
        /// Asks the state authority to give <paramref name="claimant"/> the role. The claimant is passed
        /// explicitly because the docs for RpcInfo.Source don't say what it holds when the state authority
        /// invokes the RPC on itself.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ClaimRole(Role role, PlayerRef claimant)
        {
            if (role == Role.None || claimant.IsNone)
            {
                return;
            }

            if (GetPlayer(role) == claimant)
            {
                return;
            }

            PlayerRef holder = GetPlayer(role);
            if (holder.IsNone || !IsActive(holder))
            {
                SetPlayer(role, claimant);
                PublishSessionProperties();
                return;
            }

            RPC_RoleRejected(claimant);
        }

        /// <summary>Sent to one player whose claim lost; that device returns to the role-select scene.</summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_RoleRejected([RpcTarget] PlayerRef target)
        {
            SessionLauncher.ReturnToLobby("That role was just taken.");
        }

        public void PlayerLeft(PlayerRef player)
        {
            if (!Object.HasStateAuthority)
            {
                return;
            }

            bool changed = false;
            if (Fetcher == player)
            {
                Fetcher = PlayerRef.None;
                changed = true;
            }

            if (Assembler == player)
            {
                Assembler = PlayerRef.None;
                changed = true;
            }

            if (changed)
            {
                PublishSessionProperties();
            }
        }

        public void StateAuthorityChanged()
        {
            if (!Object.HasStateAuthority)
            {
                StopReconciling();
                return;
            }

            SweepGonePlayers();
            PublishSessionProperties();
        }

        private void SetPlayer(Role role, PlayerRef player)
        {
            if (role == Role.Fetcher)
            {
                Fetcher = player;
            }
            else if (role == Role.Assembler)
            {
                Assembler = player;
            }
        }

        private bool IsActive(PlayerRef player)
        {
            foreach (PlayerRef active in Runner.ActivePlayers)
            {
                if (active == player)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Clears any role whose holder is no longer in the session. State authority only.</summary>
        private void SweepGonePlayers()
        {
            if (!Fetcher.IsNone && !IsActive(Fetcher))
            {
                Fetcher = PlayerRef.None;
            }

            if (!Assembler.IsNone && !IsActive(Assembler))
            {
                Assembler = PlayerRef.None;
            }
        }

        /// <summary>Starts reconciling the session properties with the roles and runs the first check now. State authority only.</summary>
        private void PublishSessionProperties()
        {
            reconciling = true;
            Reconcile();
        }

        /// <summary>
        /// Runs on every simulated tick while this device has state authority, because a scene NetworkObject's
        /// state authority is simulated and TickTimer counts runner ticks. A device without authority stops reconciling.
        /// </summary>
        public override void FixedUpdateNetwork()
        {
            if (reconciling)
            {
                Reconcile();
            }
        }

        private void StopReconciling()
        {
            reconciling = false;
            diverged = false;
            warned = false;
            resendTimer = default;
            warnTimer = default;
            confirmTimer = default;
        }

        /// <summary>
        /// Compares the desired role_fetcher and role_assembler values with the session properties and resends
        /// them at most once per <see cref="ResendIntervalSeconds"/> until they match.
        /// SessionInfo.Properties changes only when the server confirms an update, so a match right after a send
        /// can be a stale value that the in-flight update is about to overwrite. After a send, the reconcile
        /// therefore keeps running until the values match and <see cref="ConfirmWindowSeconds"/> have passed since
        /// the last send; a confirmation that makes the values differ inside the window takes the resend path.
        /// A match with no send since the reconcile started stops at once.
        /// The return value of UpdateCustomProperties is not used: it is false both when nothing changed and when
        /// an update is dropped, and an update that quickly follows another one can be dropped. Only the session
        /// properties show whether an update took.
        /// </summary>
        private void Reconcile()
        {
            if (!Object.HasStateAuthority)
            {
                StopReconciling();
                return;
            }

            SessionInfo info = Runner.SessionInfo;
            if (info == null || !info.IsValid)
            {
                return;
            }

            int desiredFetcher = Fetcher.IsNone ? 0 : RoleInfo.TakenValue;
            int desiredAssembler = Assembler.IsNone ? 0 : RoleInfo.TakenValue;
            string fetcherKey = Role.Fetcher.GetSessionPropertyKey();
            string assemblerKey = Role.Assembler.GetSessionPropertyKey();
            bool hasFetcher = TryGetInt(info, fetcherKey, out int currentFetcher);
            bool hasAssembler = TryGetInt(info, assemblerKey, out int currentAssembler);

            if (hasFetcher && hasAssembler && currentFetcher == desiredFetcher && currentAssembler == desiredAssembler)
            {
                if (confirmTimer.IsRunning && !confirmTimer.Expired(Runner))
                {
                    return;
                }

                StopReconciling();
                return;
            }

            if (!diverged)
            {
                diverged = true;
                warned = false;
                warnTimer = TickTimer.CreateFromSeconds(Runner, WarnAfterSeconds);
            }

            if (!warned && warnTimer.Expired(Runner))
            {
                warned = true;
                Debug.LogWarning(
                    $"SessionState: session properties still differ from the roles. " +
                    $"{fetcherKey}: desired {desiredFetcher}, current {Describe(hasFetcher, currentFetcher)}; " +
                    $"{assemblerKey}: desired {desiredAssembler}, current {Describe(hasAssembler, currentAssembler)}.");
            }

            if (resendTimer.ExpiredOrNotRunning(Runner))
            {
                resendTimer = TickTimer.CreateFromSeconds(Runner, ResendIntervalSeconds);
                confirmTimer = TickTimer.CreateFromSeconds(Runner, ConfirmWindowSeconds);
                info.UpdateCustomProperties(new Dictionary<string, SessionProperty>
                {
                    [fetcherKey] = desiredFetcher,
                    [assemblerKey] = desiredAssembler
                });
            }
        }

        private static bool TryGetInt(SessionInfo info, string key, out int value)
        {
            value = 0;
            if (info.Properties == null || !info.Properties.TryGetValue(key, out SessionProperty property) || !property.IsInt)
            {
                return false;
            }

            value = (int)property;
            return true;
        }

        private static string Describe(bool present, int value)
        {
            return present ? value.ToString() : "missing";
        }
    }
}
