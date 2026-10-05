using UnityEngine;
using VRBuilder.Core;

namespace TireTraining
{
    /// <summary>
    /// Starts the training process at a later chapter for Play Mode testing.
    /// Chapter zero keeps the normal learner flow.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TestChapterStart : MonoBehaviour
    {
        [SerializeField, Min(0)]
        [Tooltip("0 = normal start, 1 = Lift car, 2 = Remove Wheel.")]
        private int chapterIndex;

#if UNITY_EDITOR
        private void OnEnable()
        {
            ProcessRunner.Events.ProcessInitialized += OnProcessInitialized;
        }

        private void OnDisable()
        {
            ProcessRunner.Events.ProcessInitialized -= OnProcessInitialized;
        }

        private void OnProcessInitialized(object sender, ProcessEventArgs args)
        {
            if (chapterIndex == 0)
            {
                return;
            }

            var chapters = args.Process.Data.Chapters;
            if (chapterIndex < 0 || chapterIndex >= chapters.Count)
            {
                Debug.LogWarning($"Test chapter index {chapterIndex} is out of range (0-{chapters.Count - 1}).", this);
                return;
            }

            ProcessRunner.SetNextChapter(chapters[chapterIndex]);

            // The Remove Wheel chapter assumes the car was already lifted.
            // Its wrench controller supports starting without recorded loosening turns.
            if (chapterIndex == 2)
            {
                ScrewJackController jack = FindFirstObjectByType<ScrewJackController>();
                if (jack == null)
                {
                    Debug.LogWarning("Remove Wheel test start could not find the screw jack to raise the car.", this);
                }
                else
                {
                    jack.SetHeightNormalized(1f);
                }
            }

            Debug.Log($"Test start: {chapters[chapterIndex].Data.Name} (chapter {chapterIndex}).", this);
        }
#endif
    }
}
