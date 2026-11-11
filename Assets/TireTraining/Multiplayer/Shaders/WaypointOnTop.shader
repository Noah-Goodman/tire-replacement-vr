// Alpha-blended unlit shader that draws over everything (ZTest Always), for avatar waypoint icons and chevrons.
//
// The _Sprite toggle (keyword _WAYPOINT_SPRITE, on by default) selects the renderer type the material is for:
//  - On, for a SpriteRenderer (the icon): output = _MainTex * vertex color * _Color * unity_SpriteColor * _BaseColor,
//    with the renderer's flip taken from unity_SpriteProps.xy, as in URP's Sprite-Unlit-Default.
//  - Off, for a MeshRenderer (the chevron): output = _MainTex * _BaseColor. Vertex color, _Color and the sprite
//    per-draw values are not read, so a color set on both _BaseColor and _Color is applied once.
// _MainTex defaults to white, so a renderer without a texture shows the plain color.
Shader "TireTraining/WaypointOnTop"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Color ("Tint", Color) = (1, 1, 1, 1)
        [Toggle(_WAYPOINT_SPRITE)] _Sprite ("Sprite Renderer", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Pass
        {
            Name "WaypointOnTop"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _WAYPOINT_SPRITE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _Color;
                float _Sprite;
            CBUFFER_END

            #ifdef _WAYPOINT_SPRITE
                // Per-instance SpriteRenderer color and flip, used when the draw is instanced.
                #ifdef UNITY_INSTANCING_ENABLED
                    UNITY_INSTANCING_BUFFER_START(PerDrawSprite)
                        UNITY_DEFINE_INSTANCED_PROP(float4, unity_SpriteRendererColorArray)
                        UNITY_DEFINE_INSTANCED_PROP(float2, unity_SpriteFlipArray)
                    UNITY_INSTANCING_BUFFER_END(PerDrawSprite)

                    #define SpriteColor UNITY_ACCESS_INSTANCED_PROP(PerDrawSprite, unity_SpriteRendererColorArray)
                    #define SpriteFlip  UNITY_ACCESS_INSTANCED_PROP(PerDrawSprite, unity_SpriteFlipArray)
                #else
                    #define SpriteColor unity_SpriteColor
                    #define SpriteFlip  unity_SpriteProps.xy
                #endif
            #endif

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            #ifdef _WAYPOINT_SPRITE
                half4 color : COLOR;
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionOS = input.positionOS;
            #ifdef _WAYPOINT_SPRITE
                positionOS.xy *= SpriteFlip;
                half4 tint = _BaseColor * _Color * input.color * (half4)SpriteColor;
            #else
                half4 tint = _BaseColor;
            #endif

                output.positionCS = TransformObjectToHClip(positionOS);
                output.uv = input.uv;
                output.color = tint;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
