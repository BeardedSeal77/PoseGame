Shader "PoseGame/WallCutout"
{
    Properties
    {
        _BaseMap ("Wall Texture", 2D) = "white" {}
        _CutoutMask ("Cutout Mask", 2D) = "white" {}
        _Tiling ("Texture Tiling", Float) = 2.0
        _AlphaCutoff ("Alpha Cutoff", Range(0,1)) = 0.5

        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Bump Strength", Range(0,3)) = 1.0

        _HeightMap ("Height Map", 2D) = "black" {}
        _HeightScale ("Height (Parallax)", Range(0,0.1)) = 0.02

        _RoughnessMap ("Roughness", 2D) = "white" {}
        _OcclusionMap ("Ambient Occlusion", 2D) = "white" {}

        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float2 uvMask      : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                float3 normalWS    : TEXCOORD3;
                float3 tangentWS   : TEXCOORD4;
                float3 bitangentWS : TEXCOORD5;
                float  fogFactor   : TEXCOORD6;
            };

            TEXTURE2D(_BaseMap);       SAMPLER(sampler_BaseMap);
            TEXTURE2D(_CutoutMask);    SAMPLER(sampler_CutoutMask);
            TEXTURE2D(_BumpMap);       SAMPLER(sampler_BumpMap);
            TEXTURE2D(_HeightMap);     SAMPLER(sampler_HeightMap);
            TEXTURE2D(_RoughnessMap);  SAMPLER(sampler_RoughnessMap);
            TEXTURE2D(_OcclusionMap);  SAMPLER(sampler_OcclusionMap);

            CBUFFER_START(UnityPerMaterial)
                float _Tiling;
                float _AlphaCutoff;
                float _BumpScale;
                float _HeightScale;
                float4 _Color;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = normInputs.normalWS;
                OUT.tangentWS = normInputs.tangentWS;
                OUT.bitangentWS = normInputs.bitangentWS;
                OUT.uv = IN.uv * _Tiling;     // Tiled UVs for wall texture
                OUT.uvMask = IN.uv;            // Untiled UVs for cutout mask
                OUT.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Sample cutout mask (mask UV is NOT tiled)
                half maskVal = SAMPLE_TEXTURE2D(_CutoutMask, sampler_CutoutMask, IN.uvMask).r;
                clip(maskVal - _AlphaCutoff);

                // Parallax offset (simple single-step)
                float3 viewDirTS = float3(
                    dot(IN.tangentWS, normalize(_WorldSpaceCameraPos - IN.positionWS)),
                    dot(IN.bitangentWS, normalize(_WorldSpaceCameraPos - IN.positionWS)),
                    dot(IN.normalWS, normalize(_WorldSpaceCameraPos - IN.positionWS))
                );
                float heightSample = SAMPLE_TEXTURE2D(_HeightMap, sampler_HeightMap, IN.uv).r;
                float2 parallaxOffset = viewDirTS.xy / viewDirTS.z * (heightSample * _HeightScale);
                float2 tiledUV = IN.uv + parallaxOffset;

                // Base color
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, tiledUV) * _Color;

                // Normal mapping
                half3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, tiledUV), _BumpScale);
                float3x3 TBN = float3x3(IN.tangentWS, IN.bitangentWS, IN.normalWS);
                float3 normalWS = normalize(mul(normalTS, TBN));

                // AO
                half ao = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, tiledUV).r;

                // Roughness -> smoothness
                half roughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, tiledUV).r;
                half smoothness = 1.0 - roughness;

                // Lighting
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = normalize(_WorldSpaceCameraPos - IN.positionWS);
                inputData.fogCoord = IN.fogFactor;

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = baseColor.rgb;
                surfaceData.metallic = 0;
                surfaceData.smoothness = smoothness;
                surfaceData.normalTS = normalTS;
                surfaceData.occlusion = ao;
                surfaceData.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, IN.fogFactor);
                return color;
            }
            ENDHLSL
        }

        // Shadow caster pass
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CutoutMask); SAMPLER(sampler_CutoutMask);

            CBUFFER_START(UnityPerMaterial)
                float _Tiling;
                float _AlphaCutoff;
                float _BumpScale;
                float _HeightScale;
                float4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float3 _LightDirection;

            Varyings vertShadow(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                posWS = posWS + _LightDirection * 0.01;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 fragShadow(Varyings IN) : SV_Target
            {
                half mask = SAMPLE_TEXTURE2D(_CutoutMask, sampler_CutoutMask, IN.uv).r;
                clip(mask - _AlphaCutoff);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}