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
    /// Assembler line text, in priority order: on a step with a handoff in either direction (Handoff is not
    /// <see cref="HandoffDirection.None"/>), "Waiting for the Fetcher to join" when no Fetcher is in the session; then,
    /// on a <see cref="HandoffDirection.FetcherToAssembler"/> step with an item, "Take the {item} from the Fetcher"
    /// while the Fetcher holds the item and it is within <see cref="takeDistance"/> of the main camera; otherwise the
    /// entry's Assembler text. The Fetcher check comes first because a Fetcher who has left can't hand anything over,
    /// although the item's holder may still read Fetcher.
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

        [Tooltip("Metres from the main camera within which the Fetcher's held item counts as close enough to take.")]
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

            if (entry.Handoff != HandoffDirection.None && !bound.GetPlayer(Role.Fetcher).IsRealPlayer)
            {
                return "Waiting for the Fetcher to join";
            }

            if (entry.Handoff == HandoffDirection.FetcherToAssembler && entry.Item != FetchItem.None)
            {
                takeCanApply = true;
                if (IsItemInReach(entry.Item))
                {
                    return $"Take the {entry.Item.GetDisplayName()} from the Fetcher";
                }
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

        private bool IsItemInReach(FetchItem item)
        {
            if (!FetchItemTag.TryGet(item, out FetchItemTag tag))
            {
                return false;
            }

            NetworkGrabbable grabbable = tag.Grabbable;
            Camera cam = Camera.main;
            if (grabbable == null || !grabbable.IsSpawned || cam == null || grabbable.Holder != Role.Fetcher)
            {
                return false;
            }

            return (tag.transform.position - cam.transform.position).sqrMagnitude <= takeDistance * takeDistance;
        }
    }
}
