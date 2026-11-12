using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TireTraining
{
    /// <summary>
    /// Lives on each runner's DontDestroyOnLoad GameObject, added by <see cref="SessionLauncher"/>.
    /// Handles only an unplanned shutdown of the current game runner while MainScene is loaded:
    /// it sends the player back to role selection with <see cref="LostMessage"/> through
    /// <see cref="SessionLauncher.ReturnToLobby"/>. Lobby runners are excluded because
    /// <see cref="SessionLauncher.ChosenRole"/> is None, and deliberate shutdowns because they clear
    /// <see cref="SessionLauncher.Runner"/> first. There is no reconnection wait: Fusion's automatic
    /// reconnect does not cover Shared Mode.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionConnectionRecovery : MonoBehaviour, INetworkRunnerCallbacks
    {
        public const string LostMessage = "Connection lost. Choose your role to rejoin.";

        private static bool quitting;

        private NetworkRunner source;
        private bool handled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            quitting = false;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        private static void OnApplicationQuitting()
        {
            quitting = true;
        }

        public void Init(NetworkRunner runner)
        {
            source = runner;
            runner.AddCallbacks(this);
        }

        private void OnDestroy()
        {
            if (source != null)
            {
                source.RemoveCallbacks(this);
            }
        }

        private static bool ShouldRecover(NetworkRunner runner)
        {
            // ReferenceEquals: once Fusion destroys the runner, Unity's == treats it as null.
            return !quitting
                && Application.isPlaying
                && ReferenceEquals(SessionLauncher.Runner, runner)
                && SessionLauncher.ChosenRole != Role.None
                && SceneManager.GetSceneByBuildIndex(SessionLauncher.MainSceneIndex).isLoaded;
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            if (handled || !ReferenceEquals(runner, source) || !ShouldRecover(runner))
            {
                return;
            }

            handled = true;
            Debug.LogWarning($"SessionConnectionRecovery: game connection lost ({shutdownReason}). Returning to role selection.");
            _ = ReturnAfterShutdown(runner);
        }

        // Static: the runner's shutdown can destroy this component before the continuation resumes.
        private static async Task ReturnAfterShutdown(NetworkRunner runner)
        {
            try
            {
                await Task.Yield();
                if (ShouldRecover(runner))
                {
                    SessionLauncher.ReturnToLobby(LostMessage);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SessionConnectionRecovery: return to role selection failed: {e.Message}");
            }
        }

        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
#pragma warning disable CS0618 // the interface member's parameter type is obsolete in Fusion 2.1
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
#pragma warning restore CS0618
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    }
}
