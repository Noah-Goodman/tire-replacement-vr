using System.Collections.Generic;
using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// Marks a scene object as one of the shared <see cref="FetchItem"/>s. Goes on the item's root GameObject, beside
    /// its <see cref="NetworkGrabbable"/>. Enabled tags are registered so other scripts can find an item by its enum value.
    /// </summary>
    public sealed class FetchItemTag : MonoBehaviour
    {
        private static readonly Dictionary<FetchItem, FetchItemTag> Registry = new Dictionary<FetchItem, FetchItemTag>();

        [Tooltip("The item this GameObject is.")]
        [SerializeField] private FetchItem item;

        private NetworkGrabbable grabbable;

        /// <summary>The item this GameObject is.</summary>
        public FetchItem Item => item;

        /// <summary>The <see cref="NetworkGrabbable"/> on this GameObject, or null.</summary>
        public NetworkGrabbable Grabbable
        {
            get
            {
                if (grabbable == null)
                {
                    grabbable = GetComponent<NetworkGrabbable>();
                }

                return grabbable;
            }
        }

        /// <summary>Finds the enabled tag for <paramref name="item"/>. False for <see cref="FetchItem.None"/> or when none is enabled.</summary>
        public static bool TryGet(FetchItem item, out FetchItemTag tag)
        {
            if (item != FetchItem.None && Registry.TryGetValue(item, out tag) && tag != null)
            {
                return true;
            }

            tag = null;
            return false;
        }

        private void OnEnable()
        {
            if (item == FetchItem.None)
            {
                return;
            }

            if (Registry.TryGetValue(item, out FetchItemTag existing) && existing != null && existing != this)
            {
                Debug.LogError($"FetchItemTag: '{name}' and '{existing.name}' are both tagged {item}.", this);
                return;
            }

            Registry[item] = this;
        }

        private void OnDisable()
        {
            if (item == FetchItem.None)
            {
                return;
            }

            if (Registry.TryGetValue(item, out FetchItemTag existing) && existing == this)
            {
                Registry.Remove(item);
            }
        }
    }
}
