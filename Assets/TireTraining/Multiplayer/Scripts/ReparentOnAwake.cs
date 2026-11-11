using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// Moves this object under <see cref="newParent"/> in <c>Awake</c>, keeping its world pose.
    /// </summary>
    public class ReparentOnAwake : MonoBehaviour
    {
        [Tooltip("The transform this object is parented to at load.")]
        [SerializeField] private Transform newParent;

        private void Awake()
        {
            if (newParent != null && transform.parent != newParent)
            {
                transform.SetParent(newParent, true);
            }
        }
    }
}
