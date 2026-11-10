using UnityEngine;
using VRBuilder.Core.Utils.ParticleMachines;

namespace TireTraining
{
    /// <summary>
    /// Plays confetti at each anchor and a chime when <see cref="SessionState.Celebrated"/> fires, so every device
    /// shows the same celebration. The chime is synthesised in code and played through a 2D AudioSource.
    /// </summary>
    [DisallowMultipleComponent]
    public class CelebrationPlayer : MonoBehaviour
    {
        const string ConfettiResourcePath = "Confetti/Prefabs/LugNutConfettiMachine";
        const int SampleRate = 44100;
        const float ChimeLength = 1.0f;
        const float FadeSeconds = 2f;

        [SerializeField, Tooltip("Confetti machine prefab. When empty, it is loaded from Resources.")]
        GameObject confettiPrefab;

        [SerializeField, Tooltip("Where confetti spawns (the car and the checklist). Null entries are skipped.")]
        Transform[] anchors;

        [SerializeField] float radius = 1f;
        [SerializeField] float duration = 3f;
        [SerializeField, Range(0f, 1f)] float chimeVolume = 0.6f;

        AudioSource audioSource;
        AudioClip chime;
        bool warnedNoMachine;

        void Awake()
        {
            if (confettiPrefab == null)
            {
                confettiPrefab = Resources.Load<GameObject>(ConfettiResourcePath);
                if (confettiPrefab == null)
                    Debug.LogError($"CelebrationPlayer: no confetti prefab assigned and Resources/{ConfettiResourcePath} was not found.", this);
            }

            chime = BuildChime();
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.loop = false;
            audioSource.clip = chime;
        }

        void OnEnable()
        {
            SessionState.Celebrated += Play;
        }

        void OnDisable()
        {
            SessionState.Celebrated -= Play;
        }

        void OnDestroy()
        {
            if (chime != null)
                Destroy(chime);
        }

        /// <summary>Plays the confetti at every non-null anchor and the chime once.</summary>
        public void Play(int chapter)
        {
            if (confettiPrefab != null && anchors != null)
            {
                foreach (Transform anchor in anchors)
                {
                    if (anchor == null)
                        continue;
                    SpawnConfetti(anchor.position);
                }
            }

            if (audioSource != null && chime != null)
                audioSource.PlayOneShot(chime, chimeVolume);
        }

        void SpawnConfetti(Vector3 position)
        {
            GameObject instance = Instantiate(confettiPrefab, position, Quaternion.Euler(90f, 0f, 0f));
            IParticleMachine machine = instance.GetComponent<IParticleMachine>();
            if (machine == null)
            {
                if (!warnedNoMachine)
                {
                    warnedNoMachine = true;
                    Debug.LogWarning("CelebrationPlayer: the confetti prefab has no IParticleMachine component.", this);
                }
                Destroy(instance);
                return;
            }

            machine.Activate(radius, duration);
            Destroy(instance, duration + FadeSeconds);
        }

        /// <summary>Two bell tones a fifth apart, exponentially decaying, mono, normalised below full scale.</summary>
        static AudioClip BuildChime()
        {
            int count = (int)(SampleRate * ChimeLength);
            float[] data = new float[count];
            AddBell(data, 0f, 880f);
            AddBell(data, 0.18f, 1320f);

            float peak = 0f;
            for (int i = 0; i < count; i++)
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float scale = peak > 0f ? 0.8f / peak : 1f;
            for (int i = 0; i < count; i++)
                data[i] *= scale;

            AudioClip clip = AudioClip.Create("CelebrationChime", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void AddBell(float[] data, float startSeconds, float frequency)
        {
            int start = (int)(startSeconds * SampleRate);
            const float attack = 0.005f;
            for (int i = start; i < data.Length; i++)
            {
                float t = (float)(i - start) / SampleRate;
                float env = Mathf.Exp(-5f * t) * Mathf.Min(1f, t / attack);
                float tone = Mathf.Sin(2f * Mathf.PI * frequency * t)
                             + 0.3f * Mathf.Sin(2f * Mathf.PI * frequency * 2f * t);
                data[i] += env * tone;
            }
        }
    }
}
