using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace TireTraining
{
    /// <summary>
    /// A role's portal in RoleSelectScene. It has three looks: Connecting (no session list yet, or a join running),
    /// Taken, and Free. When the player's head enters the ring of a free portal, the portal flares, plays a whoosh,
    /// fades the view to black and then calls <see cref="SessionLauncher.Join"/>.
    /// Entry is the head's horizontal (XZ) distance to <see cref="center"/> being less than <see cref="radius"/>;
    /// no colliders are involved. Every part is optional: a null reference skips that part.
    /// Colors are changed through property blocks only, so the shared materials are never instanced.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RolePortal : MonoBehaviour
    {
        private enum Look
        {
            Unset,
            Connecting,
            Taken,
            Free,
        }

        private const float FlareSeconds = 0.4f;
        private const float FlareHaloScale = 0.15f;
        private const float FlareLightBoost = 2f;
        private const int FlareBurstCount = 30;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly Color GreyRing = new Color(0.35f, 0.35f, 0.35f, 1f);
        private static readonly Color GreyIcon = new Color(0.35f, 0.35f, 0.35f, 1f);

        [SerializeField] private Role role = Role.Fetcher;
        [Tooltip("Falls back to the SessionLauncher found in the scene when unset.")]
        [SerializeField] private SessionLauncher launcher;
        [Tooltip("Ring radius in metres.")]
        [SerializeField] private float radius = 1.1f;
        [Tooltip("Center of the ring. Defaults to this transform.")]
        [SerializeField] private Transform center;
        [Tooltip("The player's head. Defaults to the main camera.")]
        [SerializeField] private Transform head;

        [Tooltip("The sign's status line: Connecting…, TAKEN, or the role's one-line description.")]
        [FormerlySerializedAs("label")]
        [SerializeField] private TMP_Text statusLabel;
        [Tooltip("Optional. Grey when connecting or taken; the material's role color when free.")]
        [SerializeField] private Renderer ringRenderer;
        [Tooltip("Optional. Grey when connecting or taken; the material's role color when free.")]
        [SerializeField] private Renderer discRenderer;
        [Tooltip("Optional. Off unless the portal is free.")]
        [SerializeField] private Renderer haloRenderer;
        [Tooltip("Optional. Off unless the portal is free.")]
        [SerializeField] private Renderer beamRenderer;
        [Tooltip("Optional. Emits only while the portal is free.")]
        [SerializeField] private ParticleSystem particles;
        [Tooltip("Optional. On only while the portal is free.")]
        [SerializeField] private Light portalLight;
        [Tooltip("Optional. Turned about the vertical axis to face the player's head.")]
        [SerializeField] private Transform signRoot;
        [Tooltip("Optional. Role color when free, grey otherwise.")]
        [SerializeField] private SpriteRenderer signIcon;
        [Tooltip("Optional. Reads FETCHER or ASSEMBLER; role color when free, grey otherwise.")]
        [SerializeField] private TMP_Text nameLabel;
        [Tooltip("Optional. A 2D source with Play On Awake off; plays the whoosh on entry.")]
        [SerializeField] private AudioSource audioSource;

        private MaterialPropertyBlock block;
        private Look appliedLook = Look.Unset;
        private bool inside;
        private bool entering;
        private Coroutine flare;

        private bool flareCached;
        private Vector3 haloBaseScale;
        private float lightBaseIntensity;
        private Color haloBaseColor = Color.white;
        private Color beamBaseColor = Color.white;

        public Role Role => role;

        private void OnDisable()
        {
            if (flare != null)
            {
                StopCoroutine(flare);
                flare = null;
                RestoreFlare();
            }

            entering = false;
            appliedLook = Look.Unset;
        }

        private void Update()
        {
            if (launcher == null)
            {
                launcher = FindFirstObjectByType<SessionLauncher>();
            }

            if (head == null && Camera.main != null)
            {
                head = Camera.main.transform;
            }

            bool connecting = IsConnecting();
            bool available = !connecting && launcher.IsRoleAvailable(role);
            ApplyLook(connecting ? Look.Connecting : available ? Look.Free : Look.Taken);

            FaceHead();
            UpdateEntry(available);
        }

        private bool IsConnecting()
        {
            return launcher == null || !launcher.IsLobbyReady || launcher.IsJoining;
        }

        private bool IsFree()
        {
            return !IsConnecting() && launcher.IsRoleAvailable(role);
        }

        private void FaceHead()
        {
            if (signRoot == null || head == null)
            {
                return;
            }

            Vector3 away = signRoot.position - head.position;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f)
            {
                return;
            }

            // TMP text and sprites face -Z, so +Z points away from the viewer.
            signRoot.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        private void UpdateEntry(bool available)
        {
            if (head == null)
            {
                return;
            }

            Vector3 c = (center != null ? center : transform).position;
            Vector3 h = head.position;
            float dx = h.x - c.x;
            float dz = h.z - c.z;
            bool nowInside = dx * dx + dz * dz < radius * radius;

            if (!nowInside)
            {
                inside = false;
                return;
            }

            if (inside || entering || !available)
            {
                return;
            }

            inside = true;
            StartCoroutine(EnterRoutine());
        }

        private IEnumerator EnterRoutine()
        {
            entering = true;

            if (flare != null)
            {
                StopCoroutine(flare);
                RestoreFlare();
            }

            flare = StartCoroutine(FlareRoutine());

            if (audioSource != null)
            {
                audioSource.PlayOneShot(PortalWhoosh.Clip);
            }

            // The fade starts at the flare's peak, so the peak shows at full brightness and the fade covers the decay.
            for (float t = 0f; t < FlareSeconds * 0.5f; t += Time.deltaTime)
            {
                if (!IsFree())
                {
                    entering = false;
                    yield break;
                }

                yield return null;
            }

            if (!IsFree())
            {
                entering = false;
                yield break;
            }

            ScreenFader.FadeOut();
            yield return ScreenFader.WaitForFadeOut();

            if (launcher != null && IsFree())
            {
                launcher.Join(role);
            }
            else
            {
                ScreenFader.FadeIn();
            }

            entering = false;
        }

        private IEnumerator FlareRoutine()
        {
            CacheFlareBase();

            if (particles != null)
            {
                particles.Emit(FlareBurstCount);
            }

            for (float t = 0f; t < FlareSeconds; t += Time.deltaTime)
            {
                float k = Mathf.Sin(Mathf.PI * t / FlareSeconds);
                if (appliedLook == Look.Free)
                {
                    SetFlareAlpha(haloRenderer, haloBaseColor, k);
                    SetFlareAlpha(beamRenderer, beamBaseColor, k);

                    if (haloRenderer != null)
                    {
                        haloRenderer.transform.localScale = haloBaseScale * (1f + FlareHaloScale * k);
                    }

                    if (portalLight != null)
                    {
                        portalLight.intensity = lightBaseIntensity * (1f + FlareLightBoost * k);
                    }
                }

                yield return null;
            }

            flare = null;
            RestoreFlare();
        }

        private void CacheFlareBase()
        {
            if (flareCached)
            {
                return;
            }

            flareCached = true;
            if (haloRenderer != null)
            {
                haloBaseScale = haloRenderer.transform.localScale;
                haloBaseColor = SharedBaseColor(haloRenderer);
            }

            if (beamRenderer != null)
            {
                beamBaseColor = SharedBaseColor(beamRenderer);
            }

            if (portalLight != null)
            {
                lightBaseIntensity = portalLight.intensity;
            }
        }

        private static Color SharedBaseColor(Renderer r)
        {
            Material m = r.sharedMaterial;
            return m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
        }

        /// <summary>Raises the renderer's alpha toward 1 by <paramref name="k"/> through its property block.</summary>
        private void SetFlareAlpha(Renderer r, Color baseColor, float k)
        {
            if (r == null)
            {
                return;
            }

            Color c = baseColor;
            c.a = Mathf.Lerp(baseColor.a, 1f, k);
            block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            r.SetPropertyBlock(block);
        }

        /// <summary>Undoes the flare's scale, intensity and property blocks; the next Update reapplies the look.</summary>
        private void RestoreFlare()
        {
            if (!flareCached)
            {
                return;
            }

            if (haloRenderer != null)
            {
                haloRenderer.transform.localScale = haloBaseScale;
            }

            if (portalLight != null)
            {
                portalLight.intensity = lightBaseIntensity;
            }

            appliedLook = Look.Unset;
        }

        private void ApplyLook(Look look)
        {
            if (look == appliedLook)
            {
                return;
            }

            appliedLook = look;
            bool free = look == Look.Free;

            if (statusLabel != null)
            {
                statusLabel.text = look == Look.Connecting ? "Connecting…"
                    : look == Look.Taken ? "TAKEN"
                    : StatusLine(role);
            }

            if (nameLabel != null)
            {
                nameLabel.text = role.GetDisplayName().ToUpperInvariant();
                nameLabel.color = free ? role.GetColor() : GreyIcon;
            }

            if (signIcon != null)
            {
                signIcon.color = free ? role.GetColor() : GreyIcon;
            }

            if (free)
            {
                ClearBlock(ringRenderer);
                ClearBlock(discRenderer);
                ClearBlock(haloRenderer);
                ClearBlock(beamRenderer);
            }
            else
            {
                SetGrey(ringRenderer, GreyRing);
                if (discRenderer != null)
                {
                    Color grey = GreyRing;
                    grey.a = SharedBaseColor(discRenderer).a;
                    SetGrey(discRenderer, grey);
                }
            }

            if (haloRenderer != null)
            {
                haloRenderer.enabled = free;
            }

            if (beamRenderer != null)
            {
                beamRenderer.enabled = free;
            }

            if (portalLight != null)
            {
                portalLight.enabled = free;
            }

            if (particles != null)
            {
                if (free)
                {
                    if (!particles.isEmitting)
                    {
                        particles.Play();
                    }
                }
                else
                {
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        private static string StatusLine(Role r)
        {
            return r == Role.Fetcher ? "Bring tools and parts from the garage"
                : r == Role.Assembler ? "Stay at the car and fasten everything"
                : string.Empty;
        }

        private static void ClearBlock(Renderer r)
        {
            if (r != null)
            {
                r.SetPropertyBlock(null);
            }
        }

        private void SetGrey(Renderer r, Color color)
        {
            if (r == null)
            {
                return;
            }

            block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            r.SetPropertyBlock(block);
        }
    }
}
