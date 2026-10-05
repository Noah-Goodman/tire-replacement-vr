using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRBuilder.XRInteraction.Properties;

namespace TireTraining
{
    /// <summary>
    /// VR screw jack. Grip the yellow crank knob and turn around the vertical
    /// screw axis. Clockwise raises the saddle and car; reverse lowers them.
    /// Geometry is generated so the fused source jack mesh is not required.
    /// </summary>
    [ExecuteAlways]
    public sealed class ScrewJackController : MonoBehaviour
    {
        [SerializeField] private Transform car;
        [SerializeField, Min(0.05f)] private float maximumLift = 0.35f;
        [SerializeField, Min(45f)] private float degreesPerFullLift = 1080f;

        public float LiftHeight => liftHeight;
        public bool IsFullyRaised => liftHeight >= maximumLift - 0.001f;
        public bool IsFullyLowered => liftHeight <= 0.001f;
        private bool TargetReached => lowering ? IsFullyLowered : IsFullyRaised;

        private const string GeneratedName = "_GeneratedJack";
        private float liftHeight;
        private Vector3 carStartPosition;
        private Transform saddle;
        private Transform threadedScrew;
        private Transform crank;
        private XRSimpleInteractable crankGrip;
        private SnappableProperty jackSnappable;
        private bool crankingEnabled;
        private bool lowering;
        private Transform selectingHand;
        private Vector3 previousDirection;
        private Quaternion previousHandRotation;
        private Material metalMaterial;
        private Material darkMaterial;
        private Material cueMaterial;

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                if (car == null)
                {
                    GameObject found = GameObject.Find("Car_2_Silver");
                    if (found != null) car = found.transform;
                }

                if (car == null)
                    Debug.LogError("Screw jack needs the Car_2_Silver vehicle in this scene.", this);
                else
                    carStartPosition = car.position;
            }

            BuildVisuals();
            if (!Application.isPlaying)
                return;

            crankingEnabled = false;
            jackSnappable = GetComponent<SnappableProperty>();
            crankGrip.enabled = false;

            crankGrip.selectEntered.AddListener(OnSelected);
            crankGrip.selectExited.AddListener(OnReleased);
        }

        private void OnDisable()
        {
            crankingEnabled = false;
            if (crankGrip != null)
            {
                crankGrip.selectEntered.RemoveListener(OnSelected);
                crankGrip.selectExited.RemoveListener(OnReleased);
            }
            selectingHand = null;
            DestroyGenerated();
        }

        private void Update()
        {
            if (!Application.isPlaying || crankGrip == null)
                return;

            bool canCrank = crankingEnabled && jackSnappable != null
                && jackSnappable.IsSnapped && !TargetReached;
            if (crankGrip.enabled != canCrank)
                crankGrip.enabled = canCrank;
            if (!canCrank)
            {
                selectingHand = null;
                return;
            }

            if (selectingHand == null || crank == null)
                return;

            Vector3 axis = transform.up;
            Vector3 direction = Vector3.ProjectOnPlane(selectingHand.position - crank.position, axis);
            float orbitDelta = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                direction.Normalize();
                if (previousDirection.sqrMagnitude > 0.0001f)
                    orbitDelta = Vector3.SignedAngle(previousDirection, direction, axis);
                previousDirection = direction;
            }

            Quaternion change = selectingHand.rotation * Quaternion.Inverse(previousHandRotation);
            change.ToAngleAxis(out float wristAngle, out Vector3 wristAxis);
            if (wristAngle > 180f) wristAngle -= 360f;
            float wristDelta = wristAngle * Vector3.Dot(wristAxis, axis);
            previousHandRotation = selectingHand.rotation;

            // Accept an orbiting hand or a wrist twist. Reject tracking jumps.
            float delta = Mathf.Abs(orbitDelta) >= Mathf.Abs(wristDelta) ? orbitDelta : wristDelta;
            if (Mathf.Abs(delta) > 60f) return;

            float oldHeight = liftHeight;
            liftHeight = Mathf.Clamp(liftHeight + delta * maximumLift / degreesPerFullLift, 0f, maximumLift);
            float acceptedDegrees = (liftHeight - oldHeight) * degreesPerFullLift / maximumLift;
            crank.Rotate(Vector3.up, acceptedDegrees, Space.Self);
            ApplyHeight();
            if (oldHeight < maximumLift && IsFullyRaised)
                Debug.Log("Jack fully raised. Place the jack stands before removing the wheel.", this);
            else if (oldHeight > 0f && IsFullyLowered)
                Debug.Log("Jack fully lowered. The car is on the ground.", this);
        }

        public void SetHeightNormalized(float normalizedHeight)
        {
            liftHeight = Mathf.Clamp01(normalizedHeight) * maximumLift;
            ApplyHeight();
        }

        /// <summary>
        /// Called by the active VR Builder jack-cranking condition. The crank stays
        /// usable until the jack reaches full height, or the ground when lowering.
        /// </summary>
        public void SetCrankingEnabled(bool enabled, bool lowerCar = false)
        {
            crankingEnabled = enabled;
            lowering = lowerCar;
            if (!enabled)
                selectingHand = null;
            if (crankGrip != null)
                crankGrip.enabled = enabled && jackSnappable != null
                    && jackSnappable.IsSnapped && !TargetReached;
        }

        private void OnSelected(SelectEnterEventArgs args)
        {
            if (!crankingEnabled || jackSnappable == null || !jackSnappable.IsSnapped || TargetReached)
                return;

            selectingHand = args.interactorObject.transform;
            Vector3 direction = Vector3.ProjectOnPlane(selectingHand.position - crank.position, transform.up);
            previousDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
            previousHandRotation = selectingHand.rotation;
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            selectingHand = null;
            previousDirection = Vector3.zero;
        }

        private void ApplyHeight()
        {
            if (saddle != null)
                saddle.localPosition = new Vector3(0f, 0.56f + liftHeight, 0f);
            if (threadedScrew != null)
            {
                threadedScrew.localPosition = new Vector3(0f, 0.47f + liftHeight * 0.5f, 0f);
                threadedScrew.localScale = new Vector3(0.1f, 0.18f + liftHeight, 0.1f);
            }
            if (Application.isPlaying && car != null)
            {
                // The saddle moves in jack-local units; scale that travel into world units
                // before applying the same vertical displacement to the car.
                float worldLift = transform.TransformVector(Vector3.up * liftHeight).y;
                car.position = carStartPosition + Vector3.up * worldLift;
            }
        }

        private void BuildVisuals()
        {
            DestroyGenerated();
            var generated = new GameObject(GeneratedName);
            generated.transform.SetParent(transform, false);
            if (!Application.isPlaying)
                generated.hideFlags = HideFlags.DontSaveInEditor;

            metalMaterial = MakeMaterial(new Color(0.22f, 0.28f, 0.34f));
            darkMaterial = MakeMaterial(new Color(0.045f, 0.055f, 0.065f));
            cueMaterial = MakeMaterial(new Color(1f, 0.72f, 0.08f));

            AddBlock("Base", generated.transform, new Vector3(0f, 0.07f, 0f),
                new Vector3(0.62f, 0.14f, 0.42f), darkMaterial);
            AddBlock("Lower housing", generated.transform, new Vector3(0f, 0.27f, 0f),
                new Vector3(0.25f, 0.4f, 0.25f), metalMaterial);
            threadedScrew = AddBlock("Threaded screw", generated.transform,
                new Vector3(0f, 0.47f, 0f), new Vector3(0.1f, 0.18f, 0.1f), cueMaterial).transform;
            saddle = new GameObject("Moving saddle").transform;
            saddle.SetParent(generated.transform, false);
            AddBlock("Saddle plate", saddle, Vector3.zero,
                new Vector3(0.4f, 0.07f, 0.34f), metalMaterial);
            AddBlock("Jack point cue", saddle, new Vector3(0f, 0.043f, 0f),
                new Vector3(0.27f, 0.012f, 0.22f), cueMaterial);

            crank = new GameObject("Rotating crank").transform;
            crank.SetParent(generated.transform, false);
            crank.localPosition = new Vector3(0f, 0.35f, 0f);
            AddBlock("Crank arm", crank, new Vector3(0.19f, 0f, 0f),
                new Vector3(0.38f, 0.035f, 0.04f), metalMaterial);
            GameObject knob = AddBlock("Grip and rotate clockwise to raise", crank,
                new Vector3(0.38f, 0.07f, 0f), new Vector3(0.095f, 0.16f, 0.095f), cueMaterial);

            if (Application.isPlaying)
            {
                crankGrip = knob.AddComponent<XRSimpleInteractable>();
            }
            else
            {
                // Preview is visual only; runtime adds the XR interactable.
                foreach (Collider collider in generated.GetComponentsInChildren<Collider>())
                    DestroyImmediate(collider);
                foreach (Transform child in generated.GetComponentsInChildren<Transform>())
                    child.gameObject.hideFlags = HideFlags.DontSaveInEditor;
            }
            ApplyHeight();
        }

        private static GameObject AddBlock(string name, Transform parent, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = scale;
            block.GetComponent<Renderer>().sharedMaterial = material;
            return block;
        }

        private static Material MakeMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var material = new Material(shader) { color = color, hideFlags = HideFlags.DontSave };
            return material;
        }

        private void DestroyGenerated()
        {
            Transform existing = transform.Find(GeneratedName);
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }
            saddle = null;
            threadedScrew = null;
            crank = null;
            crankGrip = null;
            if (metalMaterial != null) DestroyMaterial(metalMaterial);
            if (darkMaterial != null) DestroyMaterial(darkMaterial);
            if (cueMaterial != null) DestroyMaterial(cueMaterial);
        }

        private static void DestroyMaterial(Material material)
        {
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
    }
}
