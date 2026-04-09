Shader "Hidden/PoseGame/WallCutoutFace"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.38, 0.4, 0.42, 1)
        _TextureTiling ("Texture Tiling", Vector) = (1, 1, 0, 0)
        _Cutoff ("Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
            "RenderType" = "TransparentCutout"
        }

        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "ForwardLit"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #define MAX_VERTICES 32

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            float4 _BaseColor;
            float4 _TextureTiling;
            float _Cutoff;
            int _HoleVertexCount;
            float4 _HoleVertices[MAX_VERTICES];

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalize(normalInputs.normalWS);
                output.uv = input.uv;
                return output;
            }

            bool PointInHole(float2 uv)
            {
                bool inside = false;
                int previousIndex = _HoleVertexCount - 1;
                for (int index = 0; index < _HoleVertexCount; previousIndex = index++)
                {
                    float2 current = _HoleVertices[index].xy;
                    float2 previous = _HoleVertices[previousIndex].xy;
                    bool crossesY = ((current.y > uv.y) != (previous.y > uv.y));
                    if (!crossesY)
                    {
                        continue;
                    }

                    float interpolatedX = ((previous.x - current.x) * (uv.y - current.y) / max(0.000001, previous.y - current.y)) + current.x;
                    if (uv.x < interpolatedX)
                    {
                        inside = !inside;
                    }
                }

                return inside;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (_HoleVertexCount >= 3 && PointInHole(input.uv))
                {
                    clip(-1);
                }

                float2 tiledUv = input.uv * max(_TextureTiling.xy, float2(0.0001, 0.0001));
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, tiledUv) * _BaseColor;
                clip(baseColor.a - _Cutoff);

                Light mainLight = GetMainLight();
                float3 normalWS = normalize(input.normalWS);
                float ndotl = saturate(dot(normalWS, mainLight.direction));
                float lighting = 0.3 + (0.7 * ndotl);
                baseColor.rgb *= lighting * mainLight.color.rgb;
                return baseColor;
            }
            ENDHLSL
        }
    }
}