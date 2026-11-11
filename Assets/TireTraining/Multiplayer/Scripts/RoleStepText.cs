using System;
using System.Collections.Generic;
using UnityEngine;

namespace TireTraining
{
    /// <summary>Which way an item changes hands during a step.</summary>
    public enum HandoffDirection
    {
        None,
        FetcherToAssembler,
        AssemblerToFetcher
    }

    /// <summary>
    /// Maps a process step, identified by chapter index and step name, to the text each role sees and the item
    /// the Fetcher works with in that step. Step names repeat across chapters, so the chapter index is part of the key.
    /// </summary>
    [CreateAssetMenu(menuName = "Tire Training/Role Step Text")]
    public sealed class RoleStepText : ScriptableObject
    {
        /// <summary>The texts and item for one step.</summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("0-based chapter index, as SessionState.Chapter.")]
            [SerializeField] private int chapter;

            [Tooltip("Process step name; matched exactly, case-sensitive.")]
            [SerializeField] private string stepName;

            [TextArea] [SerializeField] private string fetcherText;
            [TextArea] [SerializeField] private string assemblerText;

            [Tooltip("The item the Fetcher works with in this step.")]
            [SerializeField] private FetchItem item;

            [Tooltip("Which way the item changes hands in this step.")]
            [SerializeField] private HandoffDirection handoff;

            /// <summary>0-based chapter index of the step.</summary>
            public int Chapter => chapter;

            /// <summary>Name of the process step.</summary>
            public string StepName => stepName;

            /// <summary>What the Fetcher should do in this step.</summary>
            public string FetcherText => fetcherText;

            /// <summary>What the Assembler should do in this step.</summary>
            public string AssemblerText => assemblerText;

            /// <summary>The item the Fetcher works with in this step.</summary>
            public FetchItem Item => item;

            /// <summary>Which way the item changes hands in this step.</summary>
            public HandoffDirection Handoff => handoff;

            /// <summary>Whether the step waits for a handoff or one of the Fetcher's two storage actions.</summary>
            public bool RequiresFetcher => handoff != HandoffDirection.None
                || (chapter == 2 && stepName == "Store the old wheel on the garage tire rack")
                || (chapter == 4 && stepName == "End Ch5");
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<(int, string), Entry> lookup;
        private readonly HashSet<(int, string)> warned = new HashSet<(int, string)>();

        /// <summary>All entries, in asset order.</summary>
        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>
        /// Finds the entry whose chapter and step name both match exactly. Returns false without a warning when the
        /// step name is null or empty, which is the normal state before any step has started. Returns false, and
        /// logs a warning once per missing pair, when a non-empty name has no entry.
        /// </summary>
        public bool TryGet(int chapter, string stepName, out Entry entry)
        {
            if (string.IsNullOrEmpty(stepName))
            {
                entry = null;
                return false;
            }

            if (lookup == null)
            {
                BuildLookup(false);
            }

            if (lookup.TryGetValue((chapter, stepName), out entry))
            {
                return true;
            }

            entry = null;
            if (warned.Add((chapter, stepName)))
            {
                Debug.LogWarning($"RoleStepText '{name}': no entry for chapter {chapter}, step '{stepName}'.", this);
            }

            return false;
        }

        /// <summary>The item for the step, or <see cref="FetchItem.None"/> when there is no entry.</summary>
        public FetchItem GetItem(int chapter, string stepName)
        {
            return TryGet(chapter, stepName, out Entry entry) ? entry.Item : FetchItem.None;
        }

        private void OnValidate()
        {
            BuildLookup(true);
        }

        private void BuildLookup(bool logDuplicates)
        {
            lookup = new Dictionary<(int, string), Entry>();
            warned.Clear();
            foreach (Entry entry in entries)
            {
                if (entry == null || entry.StepName == null)
                {
                    continue;
                }

                if (!lookup.TryAdd((entry.Chapter, entry.StepName), entry) && logDuplicates)
                {
                    Debug.LogError($"RoleStepText '{name}': duplicate entry for chapter {entry.Chapter}, step '{entry.StepName}'.", this);
                }
            }
        }
    }
}
