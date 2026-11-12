using System;
using System.Collections;
using Fusion;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// Lives in MainScene. Claims the role chosen in the lobby, moves the XR Origin to that role's spawn point,
    /// spawns this player's avatar, then publishes the role through <see cref="LocalRole"/> and <see cref="LocalRoleReady"/>.
    /// Does nothing when there is no running network session (MainScene played directly in the Editor).
    /// </summary>
    public sealed class RoleBootstrap : MonoBehaviour
    {
        private const float WaitSeconds = 15f;
        private const float ClaimResendSeconds = 3f;

        /// <summary>This device's role. <see cref="Role.None"/> until the claim has succeeded.</summary>
        public static Role LocalRole { get; private set; } = Role.None;

        /// <summary>
        /// Raised once per MainScene load, after the claim succeeded, the origin moved and the avatar spawned.
        /// A subscriber that may start later must check <see cref="LocalRole"/> first, and must unsubscribe
        /// in OnDisable or OnDestroy.
        /// </summary>
        public static event Action<Role> LocalRoleReady;

        [SerializeField] private XROrigin xrOrigin;
        [SerializeField] private Transform headSource;
        [SerializeField] private Transform leftHandSource;
        [SerializeField] private Transform rightHandSource;
        [SerializeField] private Transform fetcherSpawn;
        [SerializeField] private Transform assemblerSpawn;
        [SerializeField] private NetworkObject avatarPrefab;

        private bool avatarSpawned;
        private bool destroyed;
        private Coroutine setup;
        private NetworkRunner capturedRunner;
        private int fadeToken;
        private bool pending;

        private void Start()
        {
            LocalRole = Role.None;

            capturedRunner = SessionLauncher.Runner;
            fadeToken = SessionLauncher.JoinFadeToken;
            pending = true;
            NetworkRunner runner = capturedRunner;
            if (runner == null || (!runner.IsRunning && !runner.IsStarting))
            {
                Debug.Log("RoleBootstrap: no running network session; skipping role setup.");
                ClearPendingFade();
                return;
            }

            setup = StartCoroutine(Run(runner));
        }

        private void OnDisable()
        {
            if (setup != null)
            {
                StopCoroutine(setup);
                setup = null;
            }

            ClearPendingFade();
        }

        private void ClearPendingFade()
        {
            if (!pending)
            {
                return;
            }

            pending = false;
            ScreenFader.CancelFade(fadeToken);
            SessionLauncher.ReleaseJoinFade(fadeToken);
        }

        private void FailSetup(string message)
        {
            bool current = !Abandoned(capturedRunner);
            ClearPendingFade();
            if (current)
            {
                SessionLauncher.ReturnToLobby(message);
            }
        }

        private void OnDestroy()
        {
            destroyed = true;
            ClearPendingFade();
            LocalRole = Role.None;
        }

        private bool Abandoned(NetworkRunner runner)
        {
            return destroyed || this == null || !isActiveAndEnabled || runner == null
                || SessionLauncher.Runner != runner || (!runner.IsRunning && !runner.IsStarting);
        }

        private IEnumerator Run(NetworkRunner runner)
        {
            Role role = SessionLauncher.ChosenRole;
            if (role == Role.None)
            {
                FailSetup("No role was chosen. Pick a role to join.");
                yield break;
            }

            float deadline = Time.unscaledTime + WaitSeconds;
            while (true)
            {
                if (Abandoned(runner))
                {
                    ClearPendingFade();
                    yield break;
                }

                if (runner.IsRunning && SessionState.Instance != null)
                {
                    break;
                }

                if (Time.unscaledTime > deadline)
                {
                    FailSetup("Couldn't claim the role. Try again.");
                    yield break;
                }

                yield return null;
            }

            deadline = Time.unscaledTime + WaitSeconds;
            float nextSend = 0f;
            while (true)
            {
                if (Abandoned(runner))
                {
                    ClearPendingFade();
                    yield break;
                }

                SessionState state = SessionState.Instance;
                if (state != null && state.Object != null && state.Object.IsValid && state.Runner == runner && state.GetPlayer(role) == runner.LocalPlayer)
                {
                    break;
                }

                if (Time.unscaledTime > deadline)
                {
                    FailSetup("Couldn't claim the role. Try again.");
                    yield break;
                }

                // Re-sending is safe: a claim the player already holds is ignored, and the claim is
                // lost only if the state authority changed while it was in flight.
                if (state != null && state.Object != null && state.Object.IsValid
                    && state.Runner == runner && Time.unscaledTime >= nextSend)
                {
                    nextSend = Time.unscaledTime + ClaimResendSeconds;
                    state.RPC_ClaimRole(role, runner.LocalPlayer);
                }

                yield return null;
            }

            Complete(runner, role);
        }

        private void Complete(NetworkRunner runner, Role role)
        {
            if (Abandoned(runner))
            {
                ClearPendingFade();
                return;
            }

            if (avatarSpawned)
            {
                return;
            }

            Transform spawn = role == Role.Fetcher ? fetcherSpawn : assemblerSpawn;
            if (!MoveOrigin(spawn))
            {
                FailSetup("Couldn't move you to your spawn point. Try again.");
                return;
            }

            if (Abandoned(runner))
            {
                ClearPendingFade();
                return;
            }

            avatarSpawned = true;
            NetworkObject spawned = null;
            if (avatarPrefab != null)
            {
                try
                {
                    // The avatar's root stays at the world origin on every device. Its own pose isn't networked,
                    // and the NetworkTransforms on the head and hands send poses relative to it, so a root placed
                    // at the spawn point would shift and turn the head and hands on the other player's device.
                    spawned = runner.Spawn(avatarPrefab, Vector3.zero, Quaternion.identity, null, (r, obj) =>
                    {
                        NetworkAvatar avatar = obj.GetComponent<NetworkAvatar>();
                        if (avatar != null)
                        {
                            avatar.Initialize(role, headSource, leftHandSource, rightHandSource);
                        }
                    });
                }
                catch (Exception e)
                {
                    Debug.LogError($"RoleBootstrap: avatar spawn failed: {e}");
                }
            }
            else
            {
                Debug.LogError("RoleBootstrap: avatarPrefab is not set.");
            }

            if (spawned == null)
            {
                FailSetup("Couldn't create your avatar. Try again.");
                return;
            }

            if (Abandoned(runner))
            {
                ClearPendingFade();
                return;
            }

            pending = false;
            setup = null;
            LocalRole = role;
            LocalRoleReady?.Invoke(role);
            ScreenFader.CancelFade(fadeToken);
            SessionLauncher.ReleaseJoinFade(fadeToken);
        }

        /// <summary>Moves the XR Origin to the spawn point's position and yaw; pitch and roll stay level.</summary>
        private bool MoveOrigin(Transform spawn)
        {
            if (spawn == null || xrOrigin == null)
            {
                Debug.LogWarning("RoleBootstrap: spawn point or XR Origin is not set; the rig was not moved.");
                return false;
            }

            Transform origin = xrOrigin.transform;
            CharacterController controller = origin.GetComponent<CharacterController>();
            bool controllerWasEnabled = controller != null && controller.enabled;
            if (controllerWasEnabled)
            {
                controller.enabled = false;
            }

            origin.SetPositionAndRotation(spawn.position, Quaternion.Euler(0f, spawn.eulerAngles.y, 0f));

            if (controllerWasEnabled)
            {
                controller.enabled = true;
            }

            return true;
        }
    }
}
