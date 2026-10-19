using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// Shows a warning while the learner turns the wrench or cranks the jack
    /// against the active step's direction. Put this on an object that stays
    /// active; it hides and shows the separate popup object.
    /// </summary>
    public class WrongWayPopup : MonoBehaviour
    {
        [Tooltip("The wrench (or its WrenchPivot) or screw jack whose wrong-way turns show this popup.")]
        [SerializeField] private GameObject source;

        [Tooltip("The warning to show. Keep it outside the source's hierarchy so it is not scaled or spun with it.")]
        [SerializeField] private GameObject popup;

        [Tooltip("Keep the popup at the source's position plus the offset, so it follows the wrench between nuts.")]
        [SerializeField] private bool followSource = true;

        [Tooltip("World-space offset from the source, used when Follow Source is on.")]
        [SerializeField] private Vector3 offset = new Vector3(0f, 0.25f, 0f);

        [Tooltip("Turn the popup toward the headset so its text stays readable.")]
        [SerializeField] private bool faceViewer = true;

        private Transform viewer;

        private void Awake()
        {
            if (popup != null)
            {
                popup.SetActive(false);
            }
        }

        private void OnEnable()
        {
            WrenchTurnCondition.WrongWayChanged += OnWrongWayChanged;
            CrankJackCondition.WrongWayChanged += OnWrongWayChanged;
        }

        private void OnDisable()
        {
            WrenchTurnCondition.WrongWayChanged -= OnWrongWayChanged;
            CrankJackCondition.WrongWayChanged -= OnWrongWayChanged;
            if (popup != null)
            {
                popup.SetActive(false);
            }
        }

        private void OnWrongWayChanged(GameObject turnedObject, bool turningWrongWay)
        {
            // Accept the source or anything under it, so either the wrench or
            // its WrenchPivot child works as the source.
            if (popup == null || source == null || turnedObject == null
                || !turnedObject.transform.IsChildOf(source.transform))
            {
                return;
            }

            popup.SetActive(turningWrongWay);
            if (turningWrongWay)
            {
                PlacePopup();
                Debug.Log($"Showing {popup.name} at {popup.transform.position}.", popup);
            }
            else
            {
                Debug.Log($"Hiding {popup.name}.", popup);
            }
        }

        private void LateUpdate()
        {
            if (popup != null && popup.activeSelf)
            {
                PlacePopup();
            }
        }

        private void PlacePopup()
        {
            Transform popupTransform = popup.transform;
            if (followSource && source != null)
            {
                popupTransform.position = source.transform.position + offset;
            }

            if (!faceViewer)
            {
                return;
            }

            if (viewer == null && Camera.main != null)
            {
                viewer = Camera.main.transform;
            }

            if (viewer != null)
            {
                // TextMeshPro reads from behind its forward axis, so point
                // forward away from the viewer.
                Vector3 away = popupTransform.position - viewer.position;
                away.y = 0f;
                if (away.sqrMagnitude > 0.0001f)
                {
                    popupTransform.rotation = Quaternion.LookRotation(away);
                }
            }
        }
    }
}
