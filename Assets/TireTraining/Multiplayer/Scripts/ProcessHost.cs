using System.Collections;
using Fusion;
using UnityEngine;
using VRBuilder.Core;

namespace TireTraining
{
    /// <summary>
    /// Runs the VR Builder training process on the Assembler's device only, resuming at the chapter stored in
    /// <see cref="SessionState"/>, and reports every chapter, step and finish back to it. Sends the celebrate RPC
    /// when the process moves past a chapter or finishes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProcessHost : MonoBehaviour
    {
        private const float WaitSeconds = 30f;

        private bool started;
        private bool began;
        private bool ranProcess;
        private bool subscribed;
        private bool subscribedToRole;
        private int highestChapter;
        private bool celebratedFinish;

        private void Start()
        {
            started = true;
            CheckRole();
        }

        private void OnEnable()
        {
            if (!started)
            {
                return;
            }

            if (ranProcess)
            {
                if (ProcessRunner.IsRunning)
                {
                    SubscribeProcess();
                }
            }
            else
            {
                CheckRole();
            }
        }

        private void OnDisable()
        {
            UnsubscribeRole();
            UnsubscribeProcess();
            if (!ranProcess)
            {
                // Disabling only the component leaves the wait coroutine running; stop it so a later enable
                // starts a single new one instead of a second one that would call Run() again.
                StopAllCoroutines();
                began = false;
            }
        }

        private void OnDestroy()
        {
            UnsubscribeRole();
            UnsubscribeProcess();
        }

        private void CheckRole()
        {
            if (began || subscribedToRole)
            {
                return;
            }

            if (RoleBootstrap.LocalRole == Role.Assembler)
            {
                Begin();
            }
            else if (RoleBootstrap.LocalRole == Role.None)
            {
                RoleBootstrap.LocalRoleReady += OnLocalRoleReady;
                subscribedToRole = true;
            }
        }

        private void OnLocalRoleReady(Role role)
        {
            UnsubscribeRole();
            if (role == Role.Assembler)
            {
                Begin();
            }
        }

        private void UnsubscribeRole()
        {
            if (subscribedToRole)
            {
                RoleBootstrap.LocalRoleReady -= OnLocalRoleReady;
                subscribedToRole = false;
            }
        }

        private void Begin()
        {
            if (began)
            {
                return;
            }

            began = true;
            StartCoroutine(BeginRoutine());
        }

        private IEnumerator BeginRoutine()
        {
            NetworkRunner runner = SessionLauncher.Runner;
            float waited = 0f;
            while (ProcessRunner.Current == null || SessionState.Instance == null || !SessionState.Instance.Object.IsValid)
            {
                if (SessionLauncher.Runner != runner)
                {
                    yield break;
                }

                if (waited > WaitSeconds)
                {
                    Debug.LogError("ProcessHost: the training process or the session state was not ready after 30 s; the process was not started.", this);
                    yield break;
                }

                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            SessionState state = SessionState.Instance;
            if (state.Finished)
            {
                Debug.Log("ProcessHost: the training is already finished; not running the process.", this);
                yield break;
            }

            int chapter = state.Chapter;
            var chapters = ProcessRunner.Current.Data.Chapters;
            if (chapter > 0)
            {
                if (chapter < chapters.Count)
                {
                    ProcessRunner.SetNextChapter(chapters[chapter]);
                    Debug.Log($"ProcessHost: resuming at chapter {chapter} ({chapters[chapter].Data.Name}).", this);
                }
                else
                {
                    Debug.LogWarning($"ProcessHost: saved chapter {chapter} is out of range (0-{chapters.Count - 1}); starting from the beginning.", this);
                    chapter = 0;
                }
            }

            highestChapter = chapter;

            // ProcessRunner.Events is replaced on every scene unload, so handlers must be added right before Run.
            SubscribeProcess();
            ranProcess = true;

            Debug.Log("ProcessHost: running the training process as the Assembler.", this);
            ProcessRunner.Run();
        }

        private void SubscribeProcess()
        {
            if (subscribed)
            {
                return;
            }

            subscribed = true;
            ProcessRunner.Events.ChapterStarted += OnChapterStarted;
            ProcessRunner.Events.StepStarted += OnStepStarted;
            ProcessRunner.Events.ProcessFinished += OnProcessFinished;
        }

        private void UnsubscribeProcess()
        {
            if (!subscribed)
            {
                return;
            }

            subscribed = false;
            ProcessRunner.Events.ChapterStarted -= OnChapterStarted;
            ProcessRunner.Events.StepStarted -= OnStepStarted;
            ProcessRunner.Events.ProcessFinished -= OnProcessFinished;
        }

        private void OnChapterStarted(object sender, ProcessEventArgs args)
        {
            int index = args.Process.Data.Chapters.IndexOf(args.Chapter);
            Report(index, args, false);

            if (index > highestChapter)
            {
                highestChapter = index;
                SessionState state = ReadyState();
                if (state != null)
                {
                    state.RPC_Celebrate(index - 1);
                }
            }
        }

        private void OnStepStarted(object sender, ProcessEventArgs args)
        {
            Report(args.Process.Data.Chapters.IndexOf(args.Chapter), args, false);
        }

        private void OnProcessFinished(object sender, ProcessEventArgs args)
        {
            int count = args.Process.Data.Chapters.Count;
            int last = args.Chapter != null ? args.Process.Data.Chapters.IndexOf(args.Chapter) : -1;
            if (last < 0)
            {
                last = Mathf.Max(highestChapter, count - 1);
            }

            Report(last, args, true);

            if (!celebratedFinish)
            {
                celebratedFinish = true;
                SessionState state = ReadyState();
                if (state != null)
                {
                    state.RPC_Celebrate(last);
                }
            }
        }

        private void Report(int chapterIndex, ProcessEventArgs args, bool finished)
        {
            SessionState state = ReadyState();
            if (state == null || chapterIndex < 0)
            {
                return;
            }

            string name = args.Step?.Data.Name ?? "";
            state.RPC_ReportProgress(chapterIndex, name, finished);
        }

        private static SessionState ReadyState()
        {
            SessionState state = SessionState.Instance;
            return state != null && state.Object.IsValid ? state : null;
        }
    }
}
