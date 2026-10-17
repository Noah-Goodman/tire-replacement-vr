using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRBuilder.Core;

namespace TireTraining
{
    /// <summary>
    /// Always-visible list of the process chapters. Item N is checked when the
    /// process starts a later chapter, and every item is checked when the process finishes.
    /// Items map to chapters by index, so their order must match the process chapter order.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChapterChecklist : MonoBehaviour
    {
        [Serializable]
        public class Item
        {
            public Image box;
            public Image fill;
            public Image checkmark;
            public TMP_Text label;
        }

        [SerializeField]
        private Item[] items = Array.Empty<Item>();

        [SerializeField]
        private Color textColor = Color.white;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Label alpha once its chapter is complete.")]
        private float completedTextAlpha = 0.45f;

        [SerializeField, Min(0f)]
        private float popDuration = 0.25f;

        private bool[] completed = Array.Empty<bool>();

        private void Awake()
        {
            completed = new bool[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                Apply(i, false);
            }
        }

        private void OnEnable()
        {
            ProcessRunner.Events.ChapterStarted += OnChapterStarted;
            ProcessRunner.Events.ProcessFinished += OnProcessFinished;
        }

        private void OnDisable()
        {
            ProcessRunner.Events.ChapterStarted -= OnChapterStarted;
            ProcessRunner.Events.ProcessFinished -= OnProcessFinished;
        }

        private void OnChapterStarted(object sender, ProcessEventArgs args)
        {
            var chapters = args.Process.Data.Chapters;
            if (chapters.Count != items.Length)
            {
                Debug.LogWarning($"Chapter checklist has {items.Length} items but the process has {chapters.Count} chapters.", this);
            }

            int current = chapters.IndexOf(args.Process.Data.Current);
            for (int i = 0; i < items.Length; i++)
            {
                SetCompleted(i, i < current);
            }
        }

        private void OnProcessFinished(object sender, ProcessEventArgs args)
        {
            for (int i = 0; i < items.Length; i++)
            {
                SetCompleted(i, true);
            }
        }

        /// <summary>
        /// Checks or clears one item. Checking an item plays a short pop on its box.
        /// </summary>
        public void SetCompleted(int index, bool isCompleted)
        {
            if (index < 0 || index >= items.Length || completed[index] == isCompleted)
            {
                return;
            }

            Apply(index, isCompleted);
            if (isCompleted && isActiveAndEnabled && items[index].box != null)
            {
                StartCoroutine(Pop(items[index].box.rectTransform));
            }
        }

        private void Apply(int index, bool isCompleted)
        {
            completed[index] = isCompleted;
            Item item = items[index];
            if (item.fill != null)
            {
                item.fill.enabled = isCompleted;
            }

            if (item.checkmark != null)
            {
                item.checkmark.enabled = isCompleted;
            }

            if (item.label != null)
            {
                Color color = textColor;
                color.a = isCompleted ? completedTextAlpha : textColor.a;
                item.label.color = color;
            }
        }

        private IEnumerator Pop(RectTransform target)
        {
            for (float t = 0f; t < popDuration; t += Time.deltaTime)
            {
                target.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(Mathf.PI * t / popDuration));
                yield return null;
            }

            target.localScale = Vector3.one;
        }
    }
}
