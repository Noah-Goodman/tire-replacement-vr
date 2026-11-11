using System.Collections;
using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// Fades the view to black and back with a small black box drawn around the head, so it covers both eyes
    /// without post-processing. One instance survives scene loads; a second instance destroys itself.
    /// The box is a root object, never a child of the camera: the rig is destroyed with its scene and the
    /// fader must outlive it. The static methods do nothing when no instance exists.
    /// A duplicate instance destroys its whole GameObject, so this component belongs on its own root GameObject
    /// with no other components.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScreenFader : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private const float BoxSize = 0.4f;

        private static ScreenFader instance;

        [Tooltip("Black, drawn over everything (ZTest Always, render queue 5000). Built from the WaypointOnTop shader with _Sprite off.")]
        [SerializeField] private Material fadeMaterial;
        [SerializeField] private float fadeOutSeconds = 0.5f;
        [SerializeField] private float fadeInSeconds = 0.75f;
        [Tooltip("If the view stays fully black this long, the fader fades in by itself and logs a warning.")]
        [SerializeField] private float maxBlackSeconds = 20f;

        private MeshRenderer boxRenderer;
        private Mesh boxMesh;
        private MaterialPropertyBlock block;
        private float alpha;
        private float target;
        private float blackTime;

        /// <summary>True when the view is fully covered.</summary>
        public static bool IsBlack => instance != null && instance.alpha >= 0.999f;

        /// <summary>Starts fading to black.</summary>
        public static void FadeOut()
        {
            if (instance != null)
            {
                instance.target = 1f;
            }
        }

        /// <summary>Starts fading back in.</summary>
        public static void FadeIn()
        {
            if (instance != null)
            {
                instance.target = 0f;
            }
        }

        /// <summary>
        /// Yield on this from a coroutine to wait until the view is fully black. It ends at once when there is
        /// no instance, and ends early if a <see cref="FadeIn"/> reverses the fade.
        /// </summary>
        public static IEnumerator WaitForFadeOut()
        {
            while (instance != null && instance.target >= 1f && instance.alpha < 0.999f)
            {
                yield return null;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            if (transform.parent != null)
            {
                transform.SetParent(null, true);
            }

            DontDestroyOnLoad(gameObject);
            BuildBox();
            Application.onBeforeRender += PlaceBox;
        }

        private void OnDestroy()
        {
            if (instance != this)
            {
                return;
            }

            instance = null;
            Application.onBeforeRender -= PlaceBox;
            if (boxMesh != null)
            {
                Destroy(boxMesh);
            }

            if (boxRenderer != null)
            {
                Destroy(boxRenderer.gameObject);
            }
        }

        private void BuildBox()
        {
            float h = BoxSize * 0.5f;
            var vertices = new Vector3[]
            {
                new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(-h, h, -h),
                new Vector3(-h, -h, h), new Vector3(h, -h, h), new Vector3(h, h, h), new Vector3(-h, h, h),
            };
            var triangles = new int[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5,
                3, 7, 6, 3, 6, 2,
                0, 1, 5, 0, 5, 4,
            };

            boxMesh = new Mesh { name = "ScreenFadeBox", vertices = vertices, triangles = triangles };
            boxMesh.RecalculateBounds();

            var go = new GameObject("ScreenFadeBox") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<MeshFilter>().sharedMesh = boxMesh;
            boxRenderer = go.AddComponent<MeshRenderer>();
            boxRenderer.sharedMaterial = fadeMaterial;
            boxRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            boxRenderer.receiveShadows = false;
            boxRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            boxRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            boxRenderer.enabled = false;
            block = new MaterialPropertyBlock();

            if (fadeMaterial == null)
            {
                Debug.LogWarning("ScreenFader: fadeMaterial is not set; the screen will not fade.");
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (alpha < 0.999f)
            {
                blackTime = 0f;
            }
            else
            {
                blackTime += dt;
                if (target >= 1f && blackTime >= maxBlackSeconds)
                {
                    Debug.LogWarning($"ScreenFader: the view was black for {maxBlackSeconds:0} s; fading in.");
                    target = 0f;
                }
            }

            if (!Mathf.Approximately(alpha, target))
            {
                float seconds = target > alpha ? fadeOutSeconds : fadeInSeconds;
                float step = seconds <= 0f ? 1f : dt / seconds;
                alpha = Mathf.MoveTowards(alpha, target, step);
            }

            if (boxRenderer == null)
            {
                return;
            }

            bool visible = alpha > 0f && fadeMaterial != null;
            if (boxRenderer.enabled != visible)
            {
                boxRenderer.enabled = visible;
            }

            if (visible)
            {
                boxRenderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, new Color(0f, 0f, 0f, alpha));
                boxRenderer.SetPropertyBlock(block);
            }
        }

        private void PlaceBox()
        {
            if (boxRenderer == null || !boxRenderer.enabled)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                boxRenderer.transform.position = cam.transform.position;
            }
        }
    }
}
