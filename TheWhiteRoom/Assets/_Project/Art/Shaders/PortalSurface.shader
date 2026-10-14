// The White Room - portal screen (proposed D-139; D-097, D-074 URP).
// Unlit and opaque: it shows the portal camera's HDR render texture, sampled by SCREEN position, so the picture
// lines up pixel-for-pixel with the world around the frame. Any face of the mesh (flat screen or the near-plane
// box, proposed D-142) shows the right pixel, which is why culling is off. No lighting, no post here:
// bloom and tonemapping are applied once by the main camera (proposed D-141).
// The texture is assigned at runtime by PortalCamera and is never saved.
Shader "TWR/Portal Surface"
{
    Properties
    {
        [NoScaleOffset] _PortalTex ("Portal View (set at runtime)", 2D) = "white" {}
        [HideInInspector] _Brightness ("Brightness (keep 1)", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        Cull Off
        ZWrite On
        ZTest LEqual

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_PortalTex);
        SAMPLER(sampler_PortalTex);

        // Same per-material buffer in every pass, so the SRP Batcher accepts the shader.
        CBUFFER_START(UnityPerMaterial)
            half _Brightness;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS   : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings PortalVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return output;
        }
        ENDHLSL

        // Colour: the live view.
        Pass
        {
            Name "PortalSurface"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex PortalVertex
            #pragma fragment PortalFragment
            #pragma multi_compile_instancing

            half4 PortalFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 0..1 across the screen, with the platform's Y flip already handled by URP.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half3 colour = SAMPLE_TEXTURE2D(_PortalTex, sampler_PortalTex, screenUV).rgb * _Brightness;
                return half4(colour, 1.0h); // HDR values pass through unclamped (D-064 bloom happens later)
            }
            ENDHLSL
        }

        // Depth prepass (so the screen is in the camera depth texture like any wall).
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex PortalVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing

            half DepthFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Depth + normals prepass (used when e.g. SSAO is on in the main renderer).
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex PortalVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normalWS = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remapped = saturate(octNormalWS * 0.5 + 0.5);
                    return half4(PackFloat2To888(remapped), 0.0);
                #else
                    return half4(normalWS, 0.0);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
