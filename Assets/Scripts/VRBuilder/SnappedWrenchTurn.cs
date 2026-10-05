using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRBuilder.XRInteraction.Properties;

namespace TireTraining
{
    /// <summary>
    /// Turns the original snapped wrench by rotating its socket's attach point.
    /// </summary>
    public class SnappedWrenchTurn : MonoBehaviour
    {
        private const float TurnCompletionTolerance = 10f;

        /// <summary>
        /// Final tightening visits the five nuts in a star pattern: each next
        /// nut skips one around the hub. LugNut1 to LugNut5 run in order around it.
        /// </summary>
        private static readonly int[] StarPatternOrder = { 0, 2, 4, 1, 3 };

        [SerializeField] private SnappableProperty wrench;
        [SerializeField] private XRSimpleInteractable turnGrip;
        [SerializeField] private float requiredDegrees = 360f;
        [SerializeField] private Transform wheelWithNuts;
        [SerializeField] private SnapZoneProperty lugNutSnapZone;
        [SerializeField] private Transform replacementNuts;

        public float DegreesTurned { get; private set; }
        public int CompletedBolts { get; private set; }
        public int RemovedBolts { get; private set; }
        public int TightenedBolts { get; private set; }
        public bool WheelReleased { get; private set; }
        public bool IsRemovingBolts => removingBolts;
        public bool IsTighteningBolts => tighteningBolts;

        private Transform selectingHand;
        private Vector3 previousDirection;
        private Quaternion previousHandRotation;
        private Transform snapAnchor;
        private Quaternion initialAnchorRotation;
        private bool wasSnapped;
        private bool turnCompleted;
        private Transform[] lugNuts;
        private int currentBoltIndex;
        private Vector3 firstSnapZonePosition;
        private Vector3 firstLugNutPosition;
        private Transform initialWrenchParent;
        private Vector3 initialWrenchLocalPosition;
        private Quaternion initialWrenchLocalRotation;
        private Vector3 initialWrenchLocalScale;
        private bool removingBolts;
        private bool tighteningBolts;
        private bool finalTightening;
        private Transform[] replacementLugNuts;
        private Vector3 firstReplacementNutPosition;
        private XRGrabInteractable wheelGrab;
        private Rigidbody wheelBody;
        private Renderer wheelRenderer;
        private MaterialPropertyBlock originalWheelProperties;
        private bool wheelWasGrabbed;

        private void Awake()
        {
            if (wrench != null)
            {
                Transform wrenchTransform = wrench.transform;
                initialWrenchParent = wrenchTransform.parent;
                initialWrenchLocalPosition = wrenchTransform.localPosition;
                initialWrenchLocalRotation = wrenchTransform.localRotation;
                initialWrenchLocalScale = wrenchTransform.localScale;
            }

            // The saved pivot and grip are offset from the wrench. Align both
            // before VR Builder starts measuring the pivot's rotation.
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            if (turnGrip != null)
            {
                turnGrip.transform.localPosition = Vector3.zero;
            }

            InitializeLugNutSequence();
        }

        private void OnEnable()
        {
            if (turnGrip != null)
            {
                turnGrip.selectEntered.AddListener(OnGripSelected);
                turnGrip.selectExited.AddListener(OnGripReleased);
            }
        }

        private void OnDisable()
        {
            if (turnGrip != null)
            {
                turnGrip.selectEntered.RemoveListener(OnGripSelected);
                turnGrip.selectExited.RemoveListener(OnGripReleased);
            }

            selectingHand = null;
            RestoreSnapAnchor();
            wasSnapped = false;
        }

        private void Update()
        {
            if (wrench == null || turnGrip == null)
            {
                return;
            }

            bool isSnapped = wrench.IsSnapped;
            if (isSnapped != wasSnapped)
            {
                if (isSnapped)
                {
                    PrepareSnappedWrench();
                }
                else
                {
                    RestoreSnapAnchor();
                }

                wasSnapped = isSnapped;
            }

            bool gripShouldBeEnabled = isSnapped && !turnCompleted;
            if (turnGrip.enabled != gripShouldBeEnabled)
            {
                turnGrip.enabled = gripShouldBeEnabled;
            }

            if (!gripShouldBeEnabled || selectingHand == null)
            {
                selectingHand = null;
                return;
            }

            Vector3 axis = wrench.transform.forward;
            Vector3 direction = Vector3.ProjectOnPlane(
                selectingHand.position - transform.position, axis);

            float movementDelta = 0f;
            if (direction.sqrMagnitude >= 0.0001f)
            {
                direction.Normalize();
                if (previousDirection.sqrMagnitude >= 0.0001f)
                {
                    movementDelta = Vector3.SignedAngle(previousDirection, direction, axis);
                }

                previousDirection = direction;
            }

            Quaternion rotationChange = selectingHand.rotation * Quaternion.Inverse(previousHandRotation);
            rotationChange.ToAngleAxis(out float twistAngle, out Vector3 twistAxis);
            if (twistAngle > 180f)
            {
                twistAngle -= 360f;
            }

            float twistDelta = twistAngle * Vector3.Dot(twistAxis, axis);
            previousHandRotation = selectingHand.rotation;

            // Accept either moving the hand around the lug or twisting the wrist.
            float delta = Mathf.Abs(movementDelta) >= Mathf.Abs(twistDelta)
                ? movementDelta : twistDelta;

            // Ignore tracking jumps and hand teleports instead of counting them as turns.
            if (Mathf.Abs(delta) <= 90f && snapAnchor != null)
            {
                // DegreesTurned counts progress in the current pass's direction.
                // Stop exactly at one full turn.
                float progress = delta * TurnDirection;
                if (progress > 0f)
                {
                    progress = Mathf.Min(progress, requiredDegrees - DegreesTurned);
                }

                // The socket drives the XR Grab Interactable, so rotate its
                // attach point and the original wrench follows it.
                snapAnchor.Rotate(Vector3.forward, progress * TurnDirection, Space.Self);
                wrench.transform.rotation = snapAnchor.rotation;
                DegreesTurned += progress;

                if (DegreesTurned >= requiredDegrees - TurnCompletionTolerance)
                {
                    float remainingDegrees = requiredDegrees - DegreesTurned;
                    if (remainingDegrees > 0f)
                    {
                        snapAnchor.Rotate(Vector3.forward, remainingDegrees * TurnDirection, Space.Self);
                        wrench.transform.rotation = snapAnchor.rotation;
                    }

                    DegreesTurned = requiredDegrees;
                    selectingHand = null;
                    turnGrip.enabled = false;
                    if (removingBolts)
                    {
                        RemoveCurrentLugNut();
                    }

                    FinishCurrentTurn();
                }
            }
        }

        /// <summary>
        /// Positive Z rotation of the snap anchor is counterclockwise, viewed
        /// from outside the wheel. Tightening turns the other way.
        /// </summary>
        private float TurnDirection => tighteningBolts ? -1f : 1f;

        private Transform[] CurrentNuts => tighteningBolts ? replacementLugNuts : lugNuts;

        /// <summary>
        /// Index into CurrentNuts of the nut being turned. currentBoltIndex
        /// counts turns in the pass; the final pass maps it to the star pattern.
        /// </summary>
        private int CurrentNutIndex => finalTightening
            ? StarPatternOrder[currentBoltIndex] : currentBoltIndex;

        private void FinishCurrentTurn()
        {
            int finishedNut = CurrentNutIndex + 1;
            if (tighteningBolts)
            {
                TightenedBolts = currentBoltIndex + 1;
            }
            else if (removingBolts)
            {
                RemovedBolts = currentBoltIndex + 1;
            }
            else
            {
                CompletedBolts = currentBoltIndex + 1;
            }

            Vector3 wrenchOffset = snapAnchor != null
                ? wrench.transform.position - snapAnchor.position : Vector3.zero;
            if (snapAnchor != null)
            {
                snapAnchor.localRotation = initialAnchorRotation;
            }

            if (CurrentNuts != null && currentBoltIndex + 1 < CurrentNuts.Length)
            {
                currentBoltIndex++;
                MoveSnapZoneToCurrentBolt();
                if (snapAnchor != null)
                {
                    wrench.transform.SetPositionAndRotation(
                        snapAnchor.position + wrenchOffset, snapAnchor.rotation);
                }

                DegreesTurned = 0f;
                Debug.Log(tighteningBolts
                    ? $"Lug nut {finishedNut} tightened. Wrench moved to nut {CurrentNutIndex + 1}."
                    : removingBolts
                    ? $"Lug nut {RemovedBolts} removed. Wrench moved to nut {currentBoltIndex + 1}."
                    : $"Lug bolt {CompletedBolts} loosened. Wrench moved to bolt {currentBoltIndex + 1}.", this);
            }
            else
            {
                if (removingBolts)
                {
                    ReturnWrenchToBench();
                    lugNutSnapZone.transform.position = firstSnapZonePosition;
                }

                turnCompleted = true;
                Debug.Log(finalTightening
                    ? "All five lug nuts have been tightened in a star pattern."
                    : tighteningBolts
                    ? "All five replacement lug nuts have been tightened one full turn."
                    : removingBolts
                    ? "All five lug nuts have been removed. Wrench returned to its original bench position; the wheel is ready to lift off."
                    : "All five lug bolts have been loosened once.", this);
            }
        }

        private void ReturnWrenchToBench()
        {
            SnapZoneProperty snappedZone = wrench.SnappedZone as SnapZoneProperty;
            XRGrabInteractable grabbable = wrench.GetComponent<XRGrabInteractable>();
            if (snappedZone != null && grabbable != null)
            {
                var zone = snappedZone.SnapZone;
                var manager = zone.interactionManager ?? grabbable.interactionManager;
                if (manager != null)
                {
                    manager.SelectExit((IXRSelectInteractor)zone, (IXRSelectInteractable)grabbable);
                }
                else
                {
                    Debug.LogError("Cannot release the wrench from its snap zone without an XR Interaction Manager.", this);
                }
            }

            Transform wrenchTransform = wrench.transform;
            wrenchTransform.SetParent(initialWrenchParent, false);
            wrenchTransform.localPosition = initialWrenchLocalPosition;
            wrenchTransform.localRotation = initialWrenchLocalRotation;
            wrenchTransform.localScale = initialWrenchLocalScale;

            Rigidbody body = wrench.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private void PrepareSnappedWrench()
        {
            DegreesTurned = 0f;
            SnapZoneProperty zone = wrench.SnappedZone as SnapZoneProperty;
            snapAnchor = zone != null ? zone.SnapZone.Anchor : null;
            if (snapAnchor == null)
            {
                Debug.LogWarning("The snapped wrench has no snap anchor to rotate.", this);
                return;
            }

            initialAnchorRotation = snapAnchor.localRotation;
            Debug.Log("Wrench turn grip is ready on the original wrench (Z axis).", this);
        }

        public void BeginUnscrewing()
        {
            if (removingBolts)
            {
                return;
            }

            if (lugNuts == null || lugNutSnapZone == null || wrench == null)
            {
                Debug.LogError("Cannot start lug nut removal without the wheel, snap zone, and wrench.", this);
                return;
            }

            if (CompletedBolts < lugNuts.Length)
            {
                Debug.LogWarning("Starting Remove Wheel directly; earlier loosening turns were not recorded in this play session.", this);
            }

            var zone = lugNutSnapZone.SnapZone;
            Transform anchor = zone != null ? zone.Anchor : null;
            if (anchor == null)
            {
                Debug.LogError("Wrench snap zone has no anchor.", this);
                return;
            }

            removingBolts = true;
            turnCompleted = false;
            currentBoltIndex = 0;
            DegreesTurned = 0f;

            bool wrenchMoved = PlaceWrenchOnCurrentBolt(zone, anchor);
            Debug.Log(wrenchMoved
                ? "Remove each lug nut with one more full counterclockwise turn. Wrench returned to nut 1."
                : "Remove each lug nut with one more full counterclockwise turn. Place the held wrench on nut 1.", this);
        }

        /// <summary>
        /// Starts a clockwise pass on the replacement wheel's nuts. The first
        /// pass snugs them in order 1 to 5; its wrench snap zone stays at its
        /// first position, which lines up with replacement nut 1, so the learner
        /// places the wrench there. A pass started after the previous one
        /// finished is final tightening, after the car is lowered: it moves the
        /// snap zone and wrench back to nut 1 and follows the star pattern.
        /// </summary>
        public void BeginTightening()
        {
            if (tighteningBolts && !turnCompleted)
            {
                return;
            }

            if (replacementLugNuts == null || lugNutSnapZone == null || wrench == null)
            {
                Debug.LogError("Cannot start tightening without ReplacementNuts, the wrench snap zone, and the wrench.", this);
                return;
            }

            bool repeatPass = tighteningBolts;
            var zone = lugNutSnapZone.SnapZone;
            Transform anchor = zone != null ? zone.Anchor : null;
            if (repeatPass && anchor == null)
            {
                Debug.LogError("Wrench snap zone has no anchor.", this);
                return;
            }

            tighteningBolts = true;
            finalTightening = repeatPass;
            removingBolts = false;
            turnCompleted = false;
            currentBoltIndex = 0;
            DegreesTurned = 0f;
            TightenedBolts = 0;

            if (!repeatPass)
            {
                firstReplacementNutPosition = replacementLugNuts[0].position;
                lugNutSnapZone.transform.position = firstSnapZonePosition;
                Debug.Log("Tighten each replacement lug nut with one full clockwise turn.", this);
                return;
            }

            bool wrenchMoved = PlaceWrenchOnCurrentBolt(zone, anchor);
            Debug.Log(wrenchMoved
                ? "Car lowered. Wrench moved to lug nut 1 for final tightening in a star pattern: 1, 3, 5, 2, 4."
                : "Car lowered. Place the held wrench on lug nut 1 for final tightening in a star pattern: 1, 3, 5, 2, 4.", this);
        }

        /// <summary>
        /// Moves the wrench snap zone to the current nut and carries the wrench
        /// with it, snapping the wrench in if it is lying loose. Returns false
        /// when the learner is holding the wrench and must place it themselves.
        /// </summary>
        private bool PlaceWrenchOnCurrentBolt(XRSocketInteractor zone, Transform anchor)
        {
            if (wrench.IsSnapped)
            {
                snapAnchor = anchor;
                initialAnchorRotation = anchor.localRotation;
                Vector3 wrenchOffset = wrench.transform.position - snapAnchor.position;
                MoveSnapZoneToCurrentBolt();
                wrench.transform.SetPositionAndRotation(
                    snapAnchor.position + wrenchOffset, snapAnchor.rotation);
                return true;
            }

            MoveSnapZoneToCurrentBolt();
            XRGrabInteractable grabbable = wrench.GetComponent<XRGrabInteractable>();
            if (grabbable == null || grabbable.isSelected)
            {
                return false;
            }

            wrench.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            if (zone.interactionManager != null)
            {
                zone.interactionManager.SelectEnter(
                    (IXRSelectInteractor)zone, (IXRSelectInteractable)grabbable);
            }

            return true;
        }

        private void RemoveCurrentLugNut()
        {
            Transform nut = lugNuts[currentBoltIndex];
            nut.SetParent(null, true);
            GameObject floor = GameObject.Find("Floor");
            Collider floorCollider = floor != null ? floor.GetComponent<Collider>() : null;
            // The imported lug nut meshes have no colliders, so measure the
            // nut with its renderers instead.
            Renderer[] nutRenderers = nut.GetComponentsInChildren<Renderer>();
            if (floorCollider == null || nutRenderers.Length == 0)
            {
                Debug.LogWarning("Lug nut removed, but it could not be placed on the floor.", this);
                return;
            }

            Bounds nutBounds = nutRenderers[0].bounds;
            for (int i = 1; i < nutRenderers.Length; i++)
            {
                nutBounds.Encapsulate(nutRenderers[i].bounds);
            }

            Vector3 position = nut.position;
            position.y += floorCollider.bounds.max.y - nutBounds.min.y + 0.002f;
            nut.position = position;
            Debug.Log($"Lug nut {currentBoltIndex + 1} was placed on the floor.", this);
        }

        public void BeginWheelRemoval()
        {
            if (wheelWithNuts == null || lugNuts == null || RemovedBolts != lugNuts.Length || wheelGrab != null)
            {
                return;
            }

            Transform wheelMesh = wheelWithNuts.Find("Wheel");
            MeshFilter meshFilter = wheelMesh != null ? wheelMesh.GetComponent<MeshFilter>() : null;
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("WheelWithNut needs its Wheel mesh for grabbing and collision.", this);
                return;
            }

            BoxCollider wheelCollider = wheelMesh.GetComponent<BoxCollider>();
            if (wheelCollider == null)
            {
                wheelCollider = wheelMesh.gameObject.AddComponent<BoxCollider>();
            }

            wheelCollider.center = meshFilter.sharedMesh.bounds.center;
            wheelCollider.size = meshFilter.sharedMesh.bounds.size;

            wheelBody = wheelWithNuts.GetComponent<Rigidbody>();
            if (wheelBody == null)
            {
                wheelBody = wheelWithNuts.gameObject.AddComponent<Rigidbody>();
            }

            wheelBody.mass = 12f;
            wheelBody.useGravity = true;
            wheelBody.isKinematic = true; // Keep the mounted wheel in place until it is grabbed.

            wheelGrab = wheelWithNuts.GetComponent<XRGrabInteractable>();
            if (wheelGrab == null)
            {
                wheelGrab = wheelWithNuts.gameObject.AddComponent<XRGrabInteractable>();
            }

            wheelGrab.colliders.Clear();
            wheelGrab.colliders.Add(wheelCollider);
            wheelGrab.retainTransformParent = false;
            wheelGrab.throwOnDetach = false;
            wheelGrab.selectEntered.AddListener(OnWheelSelected);
            wheelGrab.selectExited.AddListener(OnWheelReleased);

            wheelRenderer = wheelMesh.GetComponent<Renderer>();
            if (wheelRenderer != null)
            {
                originalWheelProperties = new MaterialPropertyBlock();
                wheelRenderer.GetPropertyBlock(originalWheelProperties);
                MaterialPropertyBlock highlight = new MaterialPropertyBlock();
                wheelRenderer.GetPropertyBlock(highlight);
                highlight.SetColor("_BaseColor", new Color(1f, 0.76f, 0.12f, 1f));
                highlight.SetColor("_Color", new Color(1f, 0.76f, 0.12f, 1f));
                wheelRenderer.SetPropertyBlock(highlight);
            }
        }

        public void EndWheelHighlight()
        {
            if (wheelRenderer != null)
            {
                wheelRenderer.SetPropertyBlock(originalWheelProperties);
            }
        }

        private void OnWheelSelected(SelectEnterEventArgs args)
        {
            wheelWasGrabbed = true;
            wheelWithNuts.SetParent(null, true);
            EndWheelHighlight();
        }

        private void OnWheelReleased(SelectExitEventArgs args)
        {
            if (!wheelWasGrabbed)
            {
                return;
            }

            wheelBody.isKinematic = false;
            wheelBody.useGravity = true;
            WheelReleased = true;
            wheelGrab.selectEntered.RemoveListener(OnWheelSelected);
            wheelGrab.selectExited.RemoveListener(OnWheelReleased);
        }

        private void InitializeLugNutSequence()
        {
            if (wheelWithNuts == null)
            {
                GameObject wheel = GameObject.Find("WheelWithNut");
                wheelWithNuts = wheel != null ? wheel.transform : null;
            }

            if (lugNutSnapZone == null)
            {
                GameObject zone = GameObject.Find("Wrench_SnapZone1");
                lugNutSnapZone = zone != null ? zone.GetComponent<SnapZoneProperty>() : null;
            }

            if (wheelWithNuts == null || lugNutSnapZone == null)
            {
                Debug.LogError("Wrench sequence needs WheelWithNut and Wrench_SnapZone1 in the scene.", this);
                return;
            }

            // The existing snap zone starts at the unsuffixed LugNut.
            string[] names = { "LugNut", "LugNut (1)", "LugNut (2)", "LugNut (3)", "LugNut (4)" };
            lugNuts = new Transform[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                lugNuts[i] = wheelWithNuts.Find(names[i]);
                if (lugNuts[i] == null)
                {
                    Debug.LogError($"Wrench sequence cannot find {names[i]} under WheelWithNut.", this);
                    lugNuts = null;
                    return;
                }
            }

            firstSnapZonePosition = lugNutSnapZone.transform.position;
            firstLugNutPosition = lugNuts[0].position;

            if (replacementNuts == null)
            {
                // ReplacementNuts stays inactive until Chapter 4 enables it,
                // and GameObject.Find skips inactive objects.
                foreach (Transform candidate in FindObjectsByType<Transform>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (candidate.name == "ReplacementNuts" && candidate.parent == null)
                    {
                        replacementNuts = candidate;
                        break;
                    }
                }
            }

            if (replacementNuts == null)
            {
                Debug.LogError("Wrench sequence needs ReplacementNuts in the scene.", this);
                return;
            }

            replacementLugNuts = new Transform[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string replacementName = $"LugNut{i + 1}";
                replacementLugNuts[i] = replacementNuts.Find(replacementName);
                if (replacementLugNuts[i] == null)
                {
                    Debug.LogError($"Wrench sequence cannot find {replacementName} under ReplacementNuts.", this);
                    replacementLugNuts = null;
                    return;
                }
            }
        }

        private void MoveSnapZoneToCurrentBolt()
        {
            Vector3 firstNutPosition = tighteningBolts ? firstReplacementNutPosition : firstLugNutPosition;
            lugNutSnapZone.transform.position = firstSnapZonePosition
                + (CurrentNuts[CurrentNutIndex].position - firstNutPosition);
            Debug.Log($"Wrench snap zone moved to lug bolt {CurrentNutIndex + 1}.", this);
        }

        private void RestoreSnapAnchor()
        {
            if (snapAnchor != null)
            {
                snapAnchor.localRotation = initialAnchorRotation;
                snapAnchor = null;
            }
        }

        private void OnGripSelected(SelectEnterEventArgs args)
        {
            if (wrench == null || !wrench.IsSnapped || turnCompleted)
            {
                return;
            }

            selectingHand = args.interactorObject.transform;
            Vector3 direction = Vector3.ProjectOnPlane(
                selectingHand.position - transform.position, transform.forward);
            previousDirection = direction.sqrMagnitude >= 0.0001f ? direction.normalized : Vector3.zero;
            previousHandRotation = selectingHand.rotation;
            Debug.Log("Wrench turn grip selected.", this);
        }

        private void OnGripReleased(SelectExitEventArgs args)
        {
            if ((removingBolts || tighteningBolts) && !turnCompleted)
            {
                Debug.Log($"Lug nut {CurrentNutIndex + 1} turn progress: {DegreesTurned:0} of {requiredDegrees:0} degrees.", this);
            }

            selectingHand = null;
        }
    }
}
