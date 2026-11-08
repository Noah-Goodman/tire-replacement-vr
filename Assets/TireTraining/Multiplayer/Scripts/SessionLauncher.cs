using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TireTraining
{
    /// <summary>
    /// Lives in RoleSelectScene. Connects to Photon's Shared session lobby, tracks which roles the
    /// session list reports as taken, and starts the shared session when a player joins a role.
    /// The NetworkRunner sits on a DontDestroyOnLoad object so it survives the load of MainScene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionLauncher : MonoBehaviour, INetworkRunnerCallbacks
    {
        public const string SessionName = "TireTraining-NJGOODM";
        public const int LobbySceneIndex = 0;
        public const int MainSceneIndex = 1;
        public const int MaxPlayers = 2;

        private const int RetryDelayMs = 5000;

        /// <summary>The role this device joined with. <see cref="Role.None"/> until <see cref="Join"/> accepts a role.</summary>
        public static Role ChosenRole = Role.None;

        /// <summary>The current runner, or null.</summary>
        public static NetworkRunner Runner;

        /// <summary>Shown once when the lobby loads, then cleared.</summary>
        public static string LobbyMessage;

        [Tooltip("Optional. Shows LobbyMessage and connection problems.")]
        [SerializeField] private TMP_Text messageText;

        private bool fetcherTaken;
        private bool assemblerTaken;
        private bool lobbyLoopRunning;

        public bool IsLobbyReady { get; private set; }
        public bool IsJoining { get; private set; }

        public bool IsRoleAvailable(Role role)
        {
            if (!IsLobbyReady || role == Role.None)
            {
                return false;
            }

            return role == Role.Fetcher ? !fetcherTaken : !assemblerTaken;
        }

        private async void Start()
        {
            ShowMessage(LobbyMessage);
            LobbyMessage = null;

            try
            {
                if (Runner != null)
                {
                    NetworkRunner old = Runner;
                    Runner = null;
                    await ShutdownRunner(old);
                    if (this == null)
                    {
                        return;
                    }
                }

                await RunLobbyLoop();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SessionLauncher: start failed: {e.Message}");
            }
        }

        private void OnDestroy()
        {
            if (Runner != null)
            {
                Runner.RemoveCallbacks(this);
            }
        }

        /// <summary>Creates a runner and joins the lobby, retrying every few seconds until it works or this component is destroyed.</summary>
        private async Task RunLobbyLoop()
        {
            if (lobbyLoopRunning)
            {
                return;
            }

            lobbyLoopRunning = true;
            try
            {
                while (this != null && !IsLobbyReady)
                {
                    NetworkRunner runner = CreateRunner();
                    Runner = runner;

                    string failure;
                    try
                    {
                        StartGameResult result = await runner.JoinSessionLobby(SessionLobby.Shared);
                        if (this == null)
                        {
                            return;
                        }

                        if (result.Ok)
                        {
                            IsLobbyReady = true;
                            ShowMessage(null);
                            return;
                        }

                        failure = result.ErrorMessage;
                    }
                    catch (Exception e)
                    {
                        if (this == null)
                        {
                            return;
                        }

                        failure = e.Message;
                    }

                    Debug.LogWarning($"SessionLauncher: could not join the session lobby: {failure}");
                    ShowMessage("Can't reach the multiplayer service. Retrying…");

                    if (Runner == runner)
                    {
                        Runner = null;
                    }

                    await ShutdownRunner(runner);
                    if (this == null)
                    {
                        return;
                    }

                    await Task.Delay(RetryDelayMs);
                }
            }
            finally
            {
                if (this != null)
                {
                    lobbyLoopRunning = false;
                }
            }
        }

        /// <summary>
        /// Starts the shared session as <paramref name="role"/>. Ignored unless the lobby is ready,
        /// the role is available and no join is already running.
        /// </summary>
        public async void Join(Role role)
        {
            if (IsJoining || !IsRoleAvailable(role) || Runner == null)
            {
                return;
            }

            IsJoining = true;
            ChosenRole = role;

            try
            {
                StartGameResult result = await StartSharedGame(Runner);
                if (result.Ok)
                {
                    // MainScene is loading; this component is destroyed with RoleSelectScene.
                    return;
                }

                Debug.LogWarning($"SessionLauncher: StartGame after the lobby join failed ({result.ShutdownReason}): {result.ErrorMessage}. Retrying with a new runner.");

                NetworkRunner lobbyRunner = Runner;
                Runner = null;
                await ShutdownRunner(lobbyRunner);
                if (this == null)
                {
                    return;
                }

                NetworkRunner fresh = CreateRunner();
                Runner = fresh;
                result = await StartSharedGame(fresh);
                if (result.Ok)
                {
                    return;
                }

                Debug.LogWarning($"SessionLauncher: StartGame with a new runner failed ({result.ShutdownReason}): {result.ErrorMessage}");
                await FailJoin(result.ErrorMessage);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SessionLauncher: join failed: {e.Message}");
                if (this != null)
                {
                    try
                    {
                        await FailJoin(e.Message);
                    }
                    catch (Exception inner)
                    {
                        Debug.LogWarning($"SessionLauncher: recovery failed: {inner.Message}");
                    }
                }
            }
        }

        private async Task FailJoin(string reason)
        {
            NetworkRunner dead = Runner;
            Runner = null;
            if (dead != null)
            {
                await ShutdownRunner(dead);
            }

            if (this == null)
            {
                return;
            }

            ChosenRole = Role.None;
            IsJoining = false;
            IsLobbyReady = false;
            fetcherTaken = false;
            assemblerTaken = false;
            ShowMessage($"Could not join: {reason}");
            await RunLobbyLoop();
        }

        private static Task<StartGameResult> StartSharedGame(NetworkRunner runner)
        {
            return runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = SessionName,
                Scene = SceneRef.FromIndex(MainSceneIndex),
                SceneManager = runner.GetComponent<NetworkSceneManagerDefault>(),
                PlayerCount = MaxPlayers
            });
        }

        private NetworkRunner CreateRunner()
        {
            GameObject go = new GameObject("NetworkRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<NetworkSceneManagerDefault>();
            NetworkRunner runner = go.AddComponent<NetworkRunner>();
            runner.ProvideInput = false;
            runner.AddCallbacks(this);
            return runner;
        }

        private static async Task ShutdownRunner(NetworkRunner runner)
        {
            if (runner == null)
            {
                return;
            }

            try
            {
                await runner.Shutdown(true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SessionLauncher: runner shutdown failed: {e.Message}");
            }

            if (runner != null)
            {
                Destroy(runner.gameObject);
            }
        }

        /// <summary>
        /// Shuts the runner down and loads the role-select scene, which shows <paramref name="message"/> once.
        /// Works without a SessionLauncher instance.
        /// </summary>
        public static async void ReturnToLobby(string message)
        {
            LobbyMessage = message;
            ChosenRole = Role.None;

            NetworkRunner runner = Runner;
            Runner = null;

            try
            {
                await ShutdownRunner(runner);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SessionLauncher: return to lobby shutdown failed: {e.Message}");
            }

            SceneManager.LoadScene(LobbySceneIndex);
        }

        private void ShowMessage(string text)
        {
            if (messageText != null)
            {
                messageText.text = text ?? "";
            }
        }

        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            if (runner != Runner)
            {
                return;
            }

            fetcherTaken = false;
            assemblerTaken = false;

            foreach (SessionInfo info in sessionList)
            {
                if (info.Name != SessionName)
                {
                    continue;
                }

                fetcherTaken = IsTaken(info, Role.Fetcher);
                assemblerTaken = IsTaken(info, Role.Assembler);
                break;
            }
        }

        private static bool IsTaken(SessionInfo info, Role role)
        {
            if (info.Properties == null || !info.Properties.TryGetValue(role.GetSessionPropertyKey(), out SessionProperty value))
            {
                return false;
            }

            return value.IsInt && (int)value == RoleInfo.TakenValue;
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            // Only an unexpected drop of the current lobby runner matters here; every deliberate
            // shutdown clears Runner first.
            if (runner != Runner || IsJoining)
            {
                return;
            }

            Debug.LogWarning($"SessionLauncher: lobby connection lost ({shutdownReason}).");
            Runner = null;
            IsLobbyReady = false;
            ShowMessage("Connection lost. Reconnecting…");
            _ = RestartLobby(runner);
        }

        private async Task RestartLobby(NetworkRunner dead)
        {
            try
            {
                await ShutdownRunner(dead);
                if (this == null)
                {
                    return;
                }

                await RunLobbyLoop();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SessionLauncher: lobby restart failed: {e.Message}");
            }
        }

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
