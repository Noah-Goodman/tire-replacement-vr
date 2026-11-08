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

        public override void Spawned()
        {
            Instance = this;

            if (Object.HasStateAuthority)
            {
                SweepGonePlayers();
                PublishSessionProperties();
            }

            RolesChanged?.Invoke();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnRolesChanged()
        {
            RolesChanged?.Invoke();
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

        /// <summary>Writes role_fetcher and role_assembler to the session properties. State authority only.</summary>
        private void PublishSessionProperties()
        {
            SessionInfo info = Runner.SessionInfo;
            if (info == null || !info.IsValid)
            {
                return;
            }

            var properties = new Dictionary<string, SessionProperty>
            {
                [Role.Fetcher.GetSessionPropertyKey()] = Fetcher.IsNone ? 0 : RoleInfo.TakenValue,
                [Role.Assembler.GetSessionPropertyKey()] = Assembler.IsNone ? 0 : RoleInfo.TakenValue
            };

            if (!info.UpdateCustomProperties(properties))
            {
                Debug.LogWarning("SessionState: could not update the session properties.");
            }
        }
    }
}
