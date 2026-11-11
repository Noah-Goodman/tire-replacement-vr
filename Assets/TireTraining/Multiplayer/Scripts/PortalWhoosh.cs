using UnityEngine;

namespace TireTraining
{
    /// <summary>
    /// The portal's entry sound, synthesized once and cached: about 0.9 s of noise through a band-pass filter
    /// whose center rises from 300 Hz to 2.5 kHz and falls back, under a fast-attack, slower-decay envelope,
    /// peaking at about -6 dBFS. A fixed random seed makes it identical on every run.
    /// </summary>
    public static class PortalWhoosh
    {
        public const int SampleRate = 44100;
        public const float Seconds = 0.9f;

        private const float LowHz = 300f;
        private const float HighHz = 2500f;
        private const float Damping = 0.25f;
        private const float AttackSeconds = 0.1f;
        private const float DecayRate = 4f;
        private const float TailSeconds = 0.05f;
        private const float PeakAmplitude = 0.5f;
        private const int Seed = 20251111;

        private static AudioClip clip;

        /// <summary>The whoosh clip, built on first use.</summary>
        public static AudioClip Clip
        {
            get
            {
                if (clip == null)
                {
                    clip = Build();
                }

                return clip;
            }
        }

        /// <summary>The raw samples, for tests and for writing a WAV file.</summary>
        public static float[] BuildSamples()
        {
            int count = Mathf.RoundToInt(Seconds * SampleRate);
            var samples = new float[count];
            var rng = new System.Random(Seed);
            float ratio = HighHz / LowHz;
            float low = 0f;
            float band = 0f;
            float peak = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float u = t / Seconds;
                float centerHz = LowHz * Mathf.Pow(ratio, Mathf.Sin(Mathf.PI * u));
                float f = 2f * Mathf.Sin(Mathf.PI * centerHz / SampleRate);

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += f * band;
                float high = noise - low - Damping * band;
                band += f * high;

                float env = t < AttackSeconds
                    ? t / AttackSeconds
                    : Mathf.Exp(-(t - AttackSeconds) * DecayRate);
                float untilEnd = Seconds - t;
                if (untilEnd < TailSeconds)
                {
                    env *= untilEnd / TailSeconds;
                }

                float s = band * env;
                samples[i] = s;
                float a = Mathf.Abs(s);
                if (a > peak)
                {
                    peak = a;
                }
            }

            if (peak > 0f)
            {
                float gain = PeakAmplitude / peak;
                for (int i = 0; i < count; i++)
                {
                    samples[i] *= gain;
                }
            }

            return samples;
        }

        private static AudioClip Build()
        {
            float[] samples = BuildSamples();
            AudioClip c = AudioClip.Create("PortalWhoosh", samples.Length, 1, SampleRate, false);
            c.SetData(samples, 0);
            c.hideFlags = HideFlags.DontSave;
            return c;
        }
    }
}
