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

        [SerializeField] private SnappableProperty wrench;
        [SerializeField] private XRSimpleInteractable turnGrip;
        [SerializeField] private float requiredDegrees = 360f;
        [SerializeField] private float nutFallTimeoutSeconds = 1.5f;
        [SerializeField] private Transform wheelWithNuts;
        [SerializeField] private SnapZoneProperty lugNutSnapZone;

        public float DegreesTurned { get; private set; }
        public int CompletedBolts { get; private set; }
        public int RemovedBolts { get; private set; }
        public bool WheelReleased { get; private set; }
        public bool IsRemovingBolts => removingBolts;

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
        private bool removingBolts;
        private XRGrabInteractable wheelGrab;
        private Rigidbody wheelBody;
        private Renderer wheelRenderer;
        private MaterialPropertyBlock originalWheelProperties;
        private bool wheelWasGrabbed;
        private LugNutLandingDetector fallingNut;
        private float nutDropStartedAt;

        private void Awake()
        {
            if (nutFallTimeoutSeconds <= 0f)
            {
                nutFallTimeoutSeconds = 1.5f;
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

            if (fallingNut != null &&
                (fallingNut.Landed || Time.time - nutDropStartedAt >= nutFallTimeoutSeconds))
            {
                if (!fallingNut.Landed)
                {
                    PlaceNutOnFloor(fallingNut);
                }

                fallingNut = null;
                FinishCurrentTurn();
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

            bool gripShouldBeEnabled = isSnapped && !turnCompleted && fallingNut == null;
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
                // Positive Z rotation is the counterclockwise turn used by
                // the VR Builder condition. Stop exactly at one full turn.
                if (delta > 0f)
                {
                    delta = Mathf.Min(delta, requiredDegrees - DegreesTurned);
                }

                // The socket drives the XR Grab Interactable, so rotate its
                // attach point and the original wrench follows it.
                snapAnchor.Rotate(Vector3.forward, delta, Space.Self);
                wrench.transform.rotation = snapAnchor.rotation;
                DegreesTurned += delta;

                if (DegreesTurned >= requiredDegrees - TurnCompletionTolerance)
                {
                    float remainingDegrees = requiredDegrees - DegreesTurned;
                    if (remainingDegrees > 0f)
                    {
                        snapAnchor.Rotate(Vector3.forward, remainingDegrees, Space.Self);
                        wrench.transform.rotation = snapAnchor.rotation;
                    }

                    DegreesTurned = requiredDegrees;
                    selectingHand = null;
                    turnGrip.enabled = false;
                    if (removingBolts)
                    {
                        fallingNut = DropCurrentLugNut();
                        nutDropStartedAt = Time.time;
                        Debug.Log($"Lug nut {currentBoltIndex + 1} completed 360 degrees and was released.", this);
                    }
                    else
                    {
                        FinishCurrentTurn();
                    }
                }
            }
        }

        private void FinishCurrentTurn()
        {
            if (removingBolts)
            {
                RemovedBolts = currentBoltIndex + 1;
            }
            else
            {
                CompletedBolts = currentBoltIndex + 1;
            }

            if (lugNuts != null && currentBoltIndex + 1 < lugNuts.Length)
            {
                Vector3 wrenchOffset = snapAnchor != null
                    ? wrench.transform.position - snapAnchor.position : Vector3.zero;
                if (snapAnchor != null)
                {
                    snapAnchor.localRotation = initialAnchorRotation;
                }

                currentBoltIndex++;
                MoveSnapZoneToCurrentBolt();
                if (snapAnchor != null)
                {
                    wrench.transform.SetPositionAndRotation(
                        snapAnchor.position + wrenchOffset, snapAnchor.rotation);
                }

                DegreesTurned = 0f;
                Debug.Log(removingBolts
                    ? $"Lug nut {RemovedBolts} landed. Wrench moved to nut {currentBoltIndex + 1}."
                    : $"Lug bolt {CompletedBolts} loosened. Wrench moved to bolt {currentBoltIndex + 1}.", this);
            }
            else
            {
                turnCompleted = true;
                Debug.Log(removingBolts
                    ? "All five lug nuts have landed. The wheel is ready to lift off."
                    : "All five lug bolts have been loosened once.", this);
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

            bool wrenchMoved = false;

            if (wrench.IsSnapped)
            {
                snapAnchor = anchor;
                initialAnchorRotation = anchor.localRotation;
                Vector3 wrenchOffset = wrench.transform.position - snapAnchor.position;
                snapAnchor.localRotation = initialAnchorRotation;
                MoveSnapZoneToCurrentBolt();
                wrench.transform.SetPositionAndRotation(
                    snapAnchor.position + wrenchOffset, snapAnchor.rotation);
                wrenchMoved = true;
            }
            else
            {
                MoveSnapZoneToCurrentBolt();
                XRGrabInteractable grabbable = wrench.GetComponent<XRGrabInteractable>();
                if (grabbable != null && !grabbable.isSelected)
                {
                    wrench.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
                    wrenchMoved = true;
                    if (zone.interactionManager != null)
                    {
                        zone.interactionManager.SelectEnter(
                            (IXRSelectInteractor)zone, (IXRSelectInteractable)grabbable);
                    }
                }
            }

            Debug.Log(wrenchMoved
                ? "Remove each lug nut with one more full counterclockwise turn. Wrench returned to nut 1."
                : "Remove each lug nut with one more full counterclockwise turn. Place the held wrench on nut 1.", this);
        }

        private LugNutLandingDetector DropCurrentLugNut()
        {
            Transform nut = lugNuts[currentBoltIndex];
            nut.SetParent(null, true);
            // Clear the socket before gravity takes over so the nut cannot rest on the wrench.
            nut.position += nut.forward * 0.03f;
            LugNutLandingDetector detector = nut.GetComponent<LugNutLandingDetector>();
            if (detector == null)
            {
                detector = nut.gameObject.AddComponent<LugNutLandingDetector>();
            }

            Rigidbody body = nut.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = nut.gameObject.AddComponent<Rigidbody>();
            }

            body.mass = 0.05f;
            body.useGravity = true;
            body.isKinematic = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = nut.forward * 0.1f;
            return detector;
        }

        private void PlaceNutOnFloor(LugNutLandingDetector nut)
        {
            GameObject floor = GameObject.Find("Floor");
            Collider floorCollider = floor != null ? floor.GetComponent<Collider>() : null;
            Collider nutCollider = nut.GetComponent<Collider>();
            Rigidbody body = nut.GetComponent<Rigidbody>();
            if (floorCollider == null || nutCollider == null || body == null)
            {
                Debug.LogWarning("Lug nut did not report a floor landing; continuing to the next nut.", this);
                return;
            }

            Vector3 position = nut.transform.position;
            position.y = floorCollider.bounds.max.y + nutCollider.bounds.extents.y + 0.002f;
            body.position = position;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.Sleep();
            Debug.Log($"Lug nut {currentBoltIndex + 1} was placed on the floor after its fall timeout.", this);
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
        }

        private void MoveSnapZoneToCurrentBolt()
        {
            lugNutSnapZone.transform.position = firstSnapZonePosition
                + (lugNuts[currentBoltIndex].position - firstLugNutPosition);
            Debug.Log($"Wrench snap zone moved to lug bolt {currentBoltIndex + 1}.", this);
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
            if (removingBolts && fallingNut == null && !turnCompleted)
            {
                Debug.Log($"Lug nut {currentBoltIndex + 1} turn progress: {DegreesTurned:0} of {requiredDegrees:0} degrees.", this);
            }

            selectingHand = null;
        }
    }
}
