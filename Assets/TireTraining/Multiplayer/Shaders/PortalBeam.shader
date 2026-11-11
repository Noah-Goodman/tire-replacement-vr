// Rising portal beam: an additive, unlit shader for an open-ended cylinder seen from outside and inside.
//
// Alpha is full at the bottom of the height range and falls to zero at the top, raised to _FadePower.
// It is multiplied by a procedural value noise (two octaves) that scrolls upward over time.
// The noise is sampled in 3D object space, so there is no UV seam around the cylinder. Because it is
// object space, a scaled mesh scales the noise with it: raise _NoiseScale for a larger beam.
// _BaseColor is HDR, so an intensity above 1 reaches Bloom.
//
// The height range is in object space. The defaults (-1 to 1) match Unity's built-in Cylinder.
Shader "TireTraining/PortalBeam"
{
    Properties
    {
        [HDR] _BaseColor ("Color (HDR)", Color) = (1, 1, 1, 1)
        _HeightMin ("Fade Start Height (object space)", Float) = -1
        _HeightMax ("Fade End Height (object space)", Float) = 1
        _FadePower ("Fade Exponent", Range(0.1, 8)) = 1.5
        _NoiseScale ("Noise Scale", Float) = 2
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.7
        _ScrollSpeed ("Upward Scroll Speed", Float) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PortalBeam"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _HeightMin;
                float _HeightMax;
                float _FadePower;
                float _NoiseScale;
                float _NoiseStrength;
                float _ScrollSpeed;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Hash of a 3D lattice point to [0, 1), without sine.
            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            // Trilinear value noise in [0, 1].
            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash13(i);
                float n100 = Hash13(i + float3(1, 0, 0));
                float n010 = Hash13(i + float3(0, 1, 0));
                float n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1));
                float n101 = Hash13(i + float3(1, 0, 1));
                float n011 = Hash13(i + float3(0, 1, 1));
                float n111 = Hash13(i + float3(1, 1, 1));

                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y),
                            f.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.positionOS = input.positionOS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 1 at the bottom of the range, 0 at the top.
                float t = saturate((input.positionOS.y - _HeightMin) / max(_HeightMax - _HeightMin, 1e-4));
                float fade = pow(1.0 - t, _FadePower);

                // Noise pattern drifts upward: sample at a position that moves downward.
                float3 p = input.positionOS * _NoiseScale;
                p.y -= _Time.y * _ScrollSpeed * _NoiseScale;
                float n = ValueNoise(p) * 0.65 + ValueNoise(p * 2.07 + 17.3) * 0.35;
                float noise = lerp(1.0, n, _NoiseStrength);

                half alpha = (half)(_BaseColor.a * fade * noise);
                return half4(_BaseColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
