using TMPro;
using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// Shows one role's instruction text for the current process step. The board is visible only on a device whose
    /// local role equals <see cref="role"/> while a session exists, and only when it has text to show.
    ///
    /// Fetcher board text, in priority order: "Training complete." when the process is finished; "Waiting for the
    /// Assembler" when no Assembler is in the session or the step has no entry; otherwise the entry's Fetcher text.
    ///
    /// Assembler line text: on an active delivery step, an item within <see cref="takeDistance"/> shows
    /// "Pick up the {item}" if unheld and last carried by the Fetcher, or "Take the {item} from the Fetcher" if held
    /// by a present Fetcher. Otherwise, steps with <see cref="RoleStepText.Entry.RequiresFetcher"/> show
    /// "Waiting for the Fetcher to join" when the Fetcher is absent; all other cases use the entry's Assembler text.
    /// A departed Fetcher's stale holder value must clear before the item is treated as a pickup.
    /// In a solo run (<see cref="SessionLauncher.IsSoloRun"/>) the Assembler board is the solo player's board: it shows
    /// the entry's <see cref="RoleStepText.Entry.SoloText"/>, or its Assembler text when that is empty, with no pickup
    /// or waiting message.
    ///
    /// The board follows <see cref="SessionState.Instance"/>, binding to it whenever it changes, so it works when the
    /// state spawns after the board or despawns. It also refreshes when <see cref="RoleBootstrap.LocalRoleReady"/>
    /// fires, so it appears once this device's role is known. The take condition is re-checked about five times a
    /// second while it can apply.
    ///
    /// <see cref="root"/> must be a descendant of this GameObject; if it is this GameObject or an ancestor, the board
    /// logs an error and never hides it.
    /// </summary>
    public sealed class RoleBoard : MonoBehaviour
    {
        private const float PollInterval = 0.2f;

        [Tooltip("The role whose device shows this board.")]
        [SerializeField] private Role role = Role.Fetcher;

        [Tooltip("The text that shows the board's message.")]
        [SerializeField] private TMP_Text text;

        [Tooltip("What is hidden when the board has nothing to show. When empty: this GameObject's first child if it has one, otherwise the text's GameObject.")]
        [SerializeField] private GameObject root;

        [Tooltip("Metres from the main camera within which an item carried by the Fetcher counts as close enough to take or pick up.")]
        [SerializeField] private float takeDistance = 1.5f;

        private SessionState bound;
        private bool takeCanApply;
        private bool rootIsSelfOrAncestor;
        private float nextPoll;

        private GameObject Root
        {
            get
            {
                if (root != null)
                {
                    return root;
                }

                if (transform.childCount > 0)
                {
                    return transform.GetChild(0).gameObject;
                }

                return text != null ? text.gameObject : null;
            }
        }

        private void OnEnable()
        {
            GameObject target = Root;
            rootIsSelfOrAncestor = target != null && transform.IsChildOf(target.transform);
            if (rootIsSelfOrAncestor)
            {
                Debug.LogError($"RoleBoard '{name}': root is this GameObject or an ancestor, so the board can't be hidden. Assign a child as root.", this);
            }

            RoleBootstrap.LocalRoleReady += OnLocalRoleReady;
            Bind(SessionState.Instance);
        }

        private void OnDisable()
        {
            RoleBootstrap.LocalRoleReady -= OnLocalRoleReady;
            Bind(null);
        }

        private void OnLocalRoleReady(Role ready)
        {
            Refresh();
        }

        private void Update()
        {
            SessionState current = SessionState.Instance;
            if (current != bound)
            {
                Bind(current);
            }

            if (takeCanApply && Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + PollInterval;
                Refresh();
            }
        }

        private void Bind(SessionState state)
        {
            if (bound != null)
            {
                bound.RolesChanged -= Refresh;
                bound.ProgressChanged -= Refresh;
            }

            bound = state;
            if (bound != null)
            {
                bound.RolesChanged += Refresh;
                bound.ProgressChanged += Refresh;
            }

            Refresh();
        }

        private void Refresh()
        {
            takeCanApply = false;
            string message = bound != null && RoleBootstrap.LocalRole == role ? ComputeText() : null;
            if (text != null)
            {
                text.text = message ?? "";
            }

            GameObject target = Root;
            if (target != null && !rootIsSelfOrAncestor)
            {
                target.SetActive(!string.IsNullOrEmpty(message));
            }
        }

        private string ComputeText()
        {
            return role == Role.Fetcher ? FetcherMessage() : role == Role.Assembler ? AssemblerMessage() : null;
        }

        private string FetcherMessage()
        {
            if (bound.Finished)
            {
                return "Training complete.";
            }

            RoleStepText.Entry entry = FindEntry();
            if (!bound.GetPlayer(Role.Assembler).IsRealPlayer || entry == null)
            {
                return "Waiting for the Assembler";
            }

            return entry.FetcherText;
        }

        private string AssemblerMessage()
        {
            RoleStepText.Entry entry = FindEntry();
            if (entry == null)
            {
                return null;
            }

            if (SessionLauncher.IsSoloRun)
            {
                return string.IsNullOrEmpty(entry.SoloText) ? entry.AssemblerText : entry.SoloText;
            }

            if (!bound.Finished && entry.Handoff == HandoffDirection.FetcherToAssembler && entry.Item != FetchItem.None)
            {
                takeCanApply = true;
                string pickupMessage = ItemInReachMessage(entry.Item);
                if (pickupMessage != null)
                {
                    return pickupMessage;
                }
            }

            if (entry.RequiresFetcher && !bound.GetPlayer(Role.Fetcher).IsRealPlayer)
            {
                return "Waiting for the Fetcher to join";
            }

            return entry.AssemblerText;
        }

        private RoleStepText.Entry FindEntry()
        {
            RoleStepText table = bound.StepText;
            if (table == null || !table.TryGet(bound.Chapter, bound.Step.ToString(), out RoleStepText.Entry entry))
            {
                return null;
            }

            return entry;
        }

        private string ItemInReachMessage(FetchItem item)
        {
            if (!FetchItemTag.TryGet(item, out FetchItemTag tag))
            {
                return null;
            }

            NetworkGrabbable grabbable = tag.Grabbable;
            Camera cam = Camera.main;
            if (grabbable == null || !grabbable.IsSpawned || cam == null ||
                (tag.transform.position - cam.transform.position).sqrMagnitude > takeDistance * takeDistance)
            {
                return null;
            }

            if (grabbable.Holder == Role.None && grabbable.LastHeldBy == Role.Fetcher)
            {
                return $"Pick up the {item.GetDisplayName()}";
            }

            return grabbable.Holder == Role.Fetcher && bound.GetPlayer(Role.Fetcher).IsRealPlayer
                ? $"Take the {item.GetDisplayName()} from the Fetcher"
                : null;
        }
    }
}
