using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// On the Fetcher's device, shows the chapter canvas that matches <see cref="SessionState.Chapter"/>, or only the
    /// complete canvas once <see cref="SessionState.Finished"/> is set. The Fetcher's device does not run the training
    /// process, so nothing else toggles these canvases there. On the Assembler's device, and with no session, this
    /// component never touches the canvases.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChapterCanvasMirror : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Element i is the canvas for chapter i of the process.")]
        private GameObject[] chapterCanvases = System.Array.Empty<GameObject>();

        [SerializeField]
        [Tooltip("Shown instead of every chapter canvas once the process has finished.")]
        private GameObject completeCanvas;

        private bool started;
        private bool subscribedToRole;
        private bool isFetcher;
        private SessionState bound;

        private void Start()
        {
            started = true;
            CheckRole();
        }

        private void OnEnable()
        {
            if (started)
            {
                CheckRole();
            }
        }

        private void OnDisable()
        {
            UnsubscribeRole();
            Unbind();
        }

        private void OnDestroy()
        {
            UnsubscribeRole();
            Unbind();
        }

        private void CheckRole()
        {
            if (isFetcher || subscribedToRole)
            {
                return;
            }

            Role role = RoleBootstrap.LocalRole;
            if (role == Role.None)
            {
                RoleBootstrap.LocalRoleReady += OnLocalRoleReady;
                subscribedToRole = true;
            }
            else
            {
                isFetcher = role == Role.Fetcher;
            }
        }

        private void OnLocalRoleReady(Role role)
        {
            UnsubscribeRole();
            isFetcher = role == Role.Fetcher;
        }

        private void UnsubscribeRole()
        {
            if (subscribedToRole)
            {
                RoleBootstrap.LocalRoleReady -= OnLocalRoleReady;
                subscribedToRole = false;
            }
        }

        private void Update()
        {
            if (!isFetcher)
            {
                return;
            }

            if (bound != SessionState.Instance)
            {
                Bind();
            }
        }

        private void Bind()
        {
            Unbind();
            bound = SessionState.Instance;
            if (bound != null)
            {
                bound.ProgressChanged += Apply;
                Apply();
            }
        }

        private void Unbind()
        {
            if (bound != null)
            {
                bound.ProgressChanged -= Apply;
                bound = null;
            }
        }

        private void Apply()
        {
            if (bound == null || !isFetcher)
            {
                return;
            }

            bool finished = bound.Finished;
            int chapter = bound.Chapter;
            for (int i = 0; i < chapterCanvases.Length; i++)
            {
                if (chapterCanvases[i] != null)
                {
                    chapterCanvases[i].SetActive(!finished && i == chapter);
                }
            }

            if (completeCanvas != null)
            {
                completeCanvas.SetActive(finished);
            }
        }
    }
}
