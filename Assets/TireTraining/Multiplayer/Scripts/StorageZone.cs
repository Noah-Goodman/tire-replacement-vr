using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TireTraining
{
    /// <summary>
    /// Marks an item as stored when the Fetcher puts it in this zone. Put it on a socket (an
    /// <see cref="XRSocketInteractor"/> or a subclass such as VR Builder's SnapZone). The zone acts only on the
    /// Fetcher's device and leaves <see cref="NetworkGrabbable.Stored"/> set after the item leaves.
    /// </summary>
    public sealed class StorageZone : MonoBehaviour
    {
        private const float AuthorityRequestInterval = 1f;

        [Tooltip("Socket to watch. Empty uses the XRSocketInteractor on this GameObject.")]
        [SerializeField] private XRSocketInteractor socket;

        private readonly HashSet<NetworkGrabbable> reported = new HashSet<NetworkGrabbable>();
        private float nextRequestTime;

        private void Awake()
        {
            if (socket == null)
                socket = GetComponent<XRSocketInteractor>();
            if (socket == null)
                Debug.LogError("StorageZone needs an XRSocketInteractor on the same GameObject or in its Socket field.", this);
        }

        private void Update()
        {
            if (socket == null || SessionLauncher.Runner == null || RoleBootstrap.LocalRole != Role.Fetcher)
                return;

            var selected = socket.interactablesSelected;
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] is Component held)
                    TryStore(held.GetComponentInParent<NetworkGrabbable>());
            }
        }

        private void TryStore(NetworkGrabbable item)
        {
            if (item == null || !item.IsSpawned || item.Stored)
                return;

            if (item.Object.HasStateAuthority)
            {
                item.SetStored(true);
                if (reported.Add(item))
                    Debug.Log($"{item.name} stored in {name}.", this);
                return;
            }

            if (Time.unscaledTime >= nextRequestTime)
            {
                nextRequestTime = Time.unscaledTime + AuthorityRequestInterval;
                item.Object.RequestStateAuthority();
            }
        }
    }
}
