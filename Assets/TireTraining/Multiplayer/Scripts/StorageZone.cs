using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TireTraining
{
    /// <summary>
    /// Marks an item as stored when the Fetcher puts it in this zone. Put it on a socket (an
    /// <see cref="XRSocketInteractor"/> or a subclass such as VR Builder's SnapZone). The zone acts only on the
    /// Fetcher's device, or on the solo player's device in a solo run (<see cref="SessionLauncher.IsSoloRun"/>), and sets <see cref="NetworkGrabbable.Stored"/> while the socket holds the item; a hand grab on the
    /// item clears it.
    /// </summary>
    public sealed class StorageZone : MonoBehaviour
    {
        private const float AuthorityRequestInterval = 1f;

        [Tooltip("Socket to watch. Empty uses the XRSocketInteractor on this GameObject.")]
        [SerializeField] private XRSocketInteractor socket;

        [Tooltip("Fixed placement guide, visible whenever the storage socket is empty. It does not follow the carried item.")]
        [SerializeField] private GameObject placementPreview;

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
            UpdatePlacementPreview();
            if (socket == null || SessionLauncher.Runner == null || (RoleBootstrap.LocalRole != Role.Fetcher && !SessionLauncher.IsSoloRun))
                return;

            var selected = socket.interactablesSelected;
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] is Component held)
                    TryStore(held.GetComponentInParent<NetworkGrabbable>());
            }
        }

        private void OnDisable()
        {
            if (placementPreview != null)
                placementPreview.SetActive(false);
        }

        private void UpdatePlacementPreview()
        {
            if (placementPreview == null)
                return;

            bool show = socket != null && socket.isActiveAndEnabled && socket.socketActive && !socket.hasSelection;
            if (placementPreview.activeSelf != show)
                placementPreview.SetActive(show);
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
