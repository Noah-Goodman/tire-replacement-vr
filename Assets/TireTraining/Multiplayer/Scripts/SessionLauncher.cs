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
    /// With <see cref="SoloMode"/> on, <see cref="TryStartSolo"/> instead starts an offline
    /// <see cref="GameMode.Single"/> runner as the Assembler, which needs no Photon connection; the lobby
    /// keeps retrying in the background but hides its retry message.
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
        private const string RetryMessage = "Can't reach the multiplayer service. Retrying…";

        private static bool returningToLobby;
        private static bool soloMode;

        /// <summary>
        /// True when multiplayer is off: one player does every step alone with no Photon connection.
        /// False at launch; kept across returns to the lobby within one launch.
        /// </summary>
        public static bool SoloMode
        {
            get => soloMode;
            set
            {
                if (soloMode == value)
                {
                    return;
                }

                soloMode = value;
                SoloModeChanged?.Invoke();
            }
        }

        /// <summary>Raised when <see cref="SoloMode"/> changes value.</summary>
        public static event Action SoloModeChanged;

        /// <summary>
        /// True during a solo run: <see cref="SoloMode"/> is on and the current <see cref="Runner"/> is starting or running
        /// in <see cref="GameMode.Single"/>. Solo rules in MainScene and GarageScene check this; MainScene with no
        /// runner stays the no-session case.
        /// </summary>
        public static bool IsSoloRun
        {
            get
            {
                NetworkRunner runner = Runner;
                return soloMode && runner != null && (runner.IsRunning || runner.IsStarting) && runner.GameMode == GameMode.Single;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSoloMode()
        {
            soloMode = false;
            SoloModeChanged = null;
        }

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
        private int lobbyLoopGeneration;
        private int lobbyLoopCount;
        private int lobbyLoopId;
        private int attemptGeneration;

        // The message area shows RetryMessage while retryShown is set, unless SoloMode hides it; otherwise shownMessage.
        private string shownMessage;
        private bool retryShown;

        // The LobbyMessage this lobby opened with; it stays on screen until a role is accepted.
        private string returnMessage;

        internal static int JoinFadeToken { get; private set; }

        internal static void ReleaseJoinFade(int token)
        {
            if (JoinFadeToken == token)
            {
                JoinFadeToken = 0;
            }
        }

        private static void CancelJoinFade(int token)
        {
            ScreenFader.CancelFade(token);
            ReleaseJoinFade(token);
        }

        public bool IsLobbyReady { get; private set; }
        public bool IsJoining { get; private set; }

        public bool IsRoleAvailable(Role role)
        {
            if (!IsLobbyReady || (role != Role.Fetcher && role != Role.Assembler))
            {
                return false;
            }

            return role == Role.Fetcher ? !fetcherTaken : !assemblerTaken;
        }

        private async void Start()
        {
            ScreenFader.FadeIn();
            JoinFadeToken = 0;
            returnMessage = LobbyMessage;
            ShowMessage(returnMessage);
            LobbyMessage = null;

            try
            {
                if (Runner != null)
                {
                    NetworkRunner old = Runner;
                    Runner = null;
                    await ShutdownRunner(old);
                    if (this == null || !isActiveAndEnabled || Runner != null)
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

        private void OnEnable()
        {
            SoloModeChanged += RefreshMessage;
        }

        private void OnDisable()
        {
            SoloModeChanged -= RefreshMessage;
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
            // A loop left from an earlier attempt generation (a solo start can interrupt one mid-retry) exits at its next check and doesn't block this one.
            if (returningToLobby || this == null || !isActiveAndEnabled || (lobbyLoopRunning && lobbyLoopGeneration == attemptGeneration) || Runner != null || IsJoining)
            {
                return;
            }

            int generation = attemptGeneration;
            int loopId = ++lobbyLoopCount;
            lobbyLoopRunning = true;
            lobbyLoopGeneration = generation;
            lobbyLoopId = loopId;
            try
            {
                while (!returningToLobby && this != null && isActiveAndEnabled && generation == attemptGeneration && !IsLobbyReady && !IsJoining && Runner == null)
                {
                    NetworkRunner runner = CreateRunner();
                    Runner = runner;

                    string failure;
                    try
                    {
                        StartGameResult result = await runner.JoinSessionLobby(SessionLobby.Shared);
                        if (!IsCurrentAttempt(generation, runner))
                        {
                            return;
                        }

                        if (result.Ok)
                        {
                            IsLobbyReady = true;
                            ShowMessage(returnMessage);
                            return;
                        }

                        failure = result.ErrorMessage;
                    }
                    catch (Exception e)
                    {
                        if (!IsCurrentAttempt(generation, runner))
                        {
                            return;
                        }

                        failure = e.Message;
                    }

                    Debug.LogWarning($"SessionLauncher: could not join the session lobby: {failure}");
                    ShowRetryMessage();

                    if (Runner == runner)
                    {
                        Runner = null;
                    }

                    await ShutdownRunner(runner);
                    if (!IsCurrentAttempt(generation, null))
                    {
                        return;
                    }

                    await Task.Delay(RetryDelayMs);
                }
            }
            finally
            {
                if (this != null && lobbyLoopId == loopId)
                {
                    lobbyLoopRunning = false;
                }
            }
        }

        /// <summary>Compatibility entry point for callers that do not need an acceptance result.</summary>
        public void Join(Role role) => TryJoin(role);

        /// <summary>Accepts an available role synchronously; a rejected caller retains its fade ownership.</summary>
        public bool TryJoin(Role role, int fadeToken = 0)
        {
            NetworkRunner runner = Runner;
            if (returningToLobby || this == null || !isActiveAndEnabled || IsJoining || !IsRoleAvailable(role)
                || runner == null || !runner.isActiveAndEnabled || !runner.IsCloudReady)
            {
                return false;
            }

            IsJoining = true;
            ChosenRole = role;
            returnMessage = null;
            JoinFadeToken = fadeToken == 0 ? ScreenFader.CurrentFadeToken : fadeToken;
            int generation = ++attemptGeneration;
            _ = JoinRoutine(runner, generation, JoinFadeToken);
            return true;
        }

        /// <summary>
        /// Starts an offline solo run as the Assembler when <see cref="SoloMode"/> is on; the lobby doesn't need to be ready.
        /// A rejected caller retains its fade ownership.
        /// </summary>
        public bool TryStartSolo(int fadeToken = 0)
        {
            if (!SoloMode || returningToLobby || this == null || !isActiveAndEnabled || IsJoining)
            {
                return false;
            }

            IsJoining = true;
            ChosenRole = Role.Assembler;
            returnMessage = null;
            JoinFadeToken = fadeToken == 0 ? ScreenFader.CurrentFadeToken : fadeToken;
            int generation = ++attemptGeneration;
            _ = SoloRoutine(generation, JoinFadeToken);
            return true;
        }

        private bool IsCurrentAttempt(int generation, NetworkRunner expected)
        {
            return !returningToLobby && this != null && isActiveAndEnabled && generation == attemptGeneration && Runner == expected;
        }

        private async Task JoinRoutine(NetworkRunner expected, int generation, int fadeToken)
        {
            try
            {
                StartGameResult result = await StartSharedGame(expected);
                if (result.Ok)
                {
                    // A destroyed lobby launcher is expected after this runner's successful scene load.
                    if (Runner != expected || (this != null && (!isActiveAndEnabled || generation != attemptGeneration)))
                    {
                        CancelJoinFade(fadeToken);
                    }

                    return;
                }

                if (!IsCurrentAttempt(generation, expected))
                {
                    CancelJoinFade(fadeToken);
                    return;
                }

                Debug.LogWarning($"SessionLauncher: StartGame after the lobby join failed ({result.ShutdownReason}): {result.ErrorMessage}. Retrying with a new runner.");
                NetworkRunner lobbyRunner = expected;
                Runner = null;
                expected = null;
                await ShutdownRunner(lobbyRunner);
                if (!IsCurrentAttempt(generation, null))
                {
                    CancelJoinFade(fadeToken);
                    return;
                }

                expected = CreateRunner();
                Runner = expected;
                result = await StartSharedGame(expected);
                if (result.Ok)
                {
                    if (Runner != expected || (this != null && (!isActiveAndEnabled || generation != attemptGeneration)))
                    {
                        CancelJoinFade(fadeToken);
                    }

                    return;
                }

                if (!IsCurrentAttempt(generation, expected))
                {
                    CancelJoinFade(fadeToken);
                    return;
                }

                Debug.LogWarning($"SessionLauncher: StartGame with a new runner failed ({result.ShutdownReason}): {result.ErrorMessage}");
                await FailJoin(result.ErrorMessage, expected, generation, fadeToken);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SessionLauncher: join failed: {e.Message}");
                if (!IsCurrentAttempt(generation, expected))
                {
                    CancelJoinFade(fadeToken);
                    return;
                }

                try
                {
                    await FailJoin(e.Message, expected, generation, fadeToken);
                }
                catch (Exception inner)
                {
                    CancelJoinFade(fadeToken);
                    Debug.LogWarning($"SessionLauncher: recovery failed: {inner.Message}");
                }
            }
        }

        private async Task SoloRoutine(int generation, int fadeToken)
        {
            NetworkRunner expected = null;
            try
            {
                NetworkRunner lobbyRunner = Runner;
                Runner = null;
                await ShutdownRunner(lobbyRunner);
                if (!IsCurrentAttempt(generation, null))
                {
                    CancelJoinFade(fadeToken);
                    return;
                }

                expected = CreateRunner();
                Runner = expected;
                StartGameResult result = await StartSoloGame(expected);
                if (result.Ok)
                {
                    // A destroyed lobby launcher is expected after this runner's successful scene load.
                    if (Runner != expected || (this != null && (!isActiveAndEnabled || generation != attemptGeneration)))
                    {
                        CancelJoinFade(fadeToken);
                    }

                    return;
                }

                if (!IsCurrentAttempt(generation, expected))
                {
                    CancelJoinFade(fadeToken);
                    return;
                }

                Debug.LogWarning($"SessionLauncher: solo StartGame failed ({result.ShutdownReason}): {result.ErrorMessage}");
                await FailJoin(result.ErrorMessage, expected, generation, fadeToken);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SessionLauncher: solo start failed: {e.Message}");
                if (!IsCurrentAttempt(generation, expected))
                {
                    CancelJoinFade(fadeToken);
                    return;
                }

                try
                {
                    await FailJoin(e.Message, expected, generation, fadeToken);
                }
                catch (Exception inner)
                {
                    CancelJoinFade(fadeToken);
                    Debug.LogWarning($"SessionLauncher: recovery failed: {inner.Message}");
                }
            }
        }

        private async Task FailJoin(string reason, NetworkRunner expected, int generation, int fadeToken)
        {
            if (!IsCurrentAttempt(generation, expected))
            {
                CancelJoinFade(fadeToken);
                return;
            }

            Runner = null;
            await ShutdownRunner(expected);
            CancelJoinFade(fadeToken);
            if (!IsCurrentAttempt(generation, null))
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
            // Fusion only updates custom properties that already exist, so both role keys must exist when the session is created (0 = free).
            Dictionary<string, SessionProperty> sessionProperties = new Dictionary<string, SessionProperty>
            {
                { Role.Fetcher.GetSessionPropertyKey(), 0 },
                { Role.Assembler.GetSessionPropertyKey(), 0 }
            };

            return runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionProperties = sessionProperties,
                SessionName = SessionName,
                Scene = SceneRef.FromIndex(MainSceneIndex),
                SceneManager = runner.GetComponent<NetworkSceneManagerDefault>(),
                PlayerCount = MaxPlayers
            });
        }

        private static Task<StartGameResult> StartSoloGame(NetworkRunner runner)
        {
            return runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single,
                Scene = SceneRef.FromIndex(MainSceneIndex),
                SceneManager = runner.GetComponent<NetworkSceneManagerDefault>(),
                PlayerCount = 1
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
            go.AddComponent<SessionConnectionRecovery>().Init(runner);
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
                if (!runner.IsShutdown)
                {
                    await runner.Shutdown(true);
                }
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
            // A second call (for example two role rejections) must not shut down or load twice.
            if (returningToLobby)
            {
                return;
            }

            returningToLobby = true;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> clear = null;
            clear = (scene, mode) =>
            {
                if (scene.buildIndex == LobbySceneIndex)
                {
                    returningToLobby = false;
                    SceneManager.sceneLoaded -= clear;
                }
            };
            SceneManager.sceneLoaded += clear;

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
            shownMessage = text;
            retryShown = false;
            RefreshMessage();
        }

        // In solo mode the retry text is hidden and the area shows returnMessage or nothing.
        private void ShowRetryMessage()
        {
            shownMessage = returnMessage;
            retryShown = true;
            RefreshMessage();
        }

        private void RefreshMessage()
        {
            if (messageText != null)
            {
                messageText.text = (retryShown && !SoloMode ? RetryMessage : shownMessage) ?? "";
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
            int generation = attemptGeneration;
            try
            {
                await ShutdownRunner(dead);
                if (!IsCurrentAttempt(generation, null))
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
