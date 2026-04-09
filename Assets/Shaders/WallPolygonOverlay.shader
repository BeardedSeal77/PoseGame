Shader "Hidden/PoseGame/WallPolygonOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _MaskColor ("Mask Color", Color) = (0.35, 0.35, 0.35, 0.55)
        _OutlineColor ("Outline Color", Color) = (0.7, 0.7, 0.7, 0.95)
        _OutlineThickness ("Outline Thickness", Float) = 0.006
        _EdgeSoftness ("Edge Softness", Float) = 0.0015
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_VERTICES 32

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float4 _MaskColor;
            float4 _OutlineColor;
            float _OutlineThickness;
            float _EdgeSoftness;
            int _AnimatedVertexCount;
            int _OutlineVertexCount;

            float4 _AnimatedVertices[MAX_VERTICES];
            float4 _OutlineVertices[MAX_VERTICES];

            v2f vert(appdata_t input)
            {
                v2f output;
                output.vertex = TransformObjectToHClip(input.vertex.xyz);
                output.uv = input.texcoord;
                return output;
            }

            bool PointInAnimatedPolygon(float2 uv)
            {
                bool inside = false;
                int previousIndex = _AnimatedVertexCount - 1;
                for (int index = 0; index < _AnimatedVertexCount; previousIndex = index++)
                {
                    float2 current = _AnimatedVertices[index].xy;
                    float2 previous = _AnimatedVertices[previousIndex].xy;
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

            float DistanceToSegment(float2 sampleUv, float2 segmentStart, float2 segmentEnd)
            {
                float2 segment = segmentEnd - segmentStart;
                float denominator = max(0.000001, dot(segment, segment));
                float t = saturate(dot(sampleUv - segmentStart, segment) / denominator);
                float2 closestPoint = segmentStart + (segment * t);
                return distance(sampleUv, closestPoint);
            }

            float MinDistanceToOutline(float2 uv)
            {
                float minDistance = 1000.0;
                int previousIndex = _OutlineVertexCount - 1;
                for (int index = 0; index < _OutlineVertexCount; previousIndex = index++)
                {
                    minDistance = min(
                        minDistance,
                        DistanceToSegment(uv, _OutlineVertices[previousIndex].xy, _OutlineVertices[index].xy));
                }

                return minDistance;
            }

            half4 frag(v2f input) : SV_Target
            {
                if (_AnimatedVertexCount < 3 || _OutlineVertexCount < 3)
                {
                    return 0;
                }

                bool insideAnimated = PointInAnimatedPolygon(input.uv);
                float outlineDistance = MinDistanceToOutline(input.uv);
                float outlineAlpha = 1.0 - smoothstep(_OutlineThickness, _OutlineThickness + max(0.0001, _EdgeSoftness), outlineDistance);

                half4 color = insideAnimated ? half4(0, 0, 0, 0) : _MaskColor;
                color.rgb = lerp(color.rgb, _OutlineColor.rgb, _OutlineColor.a * outlineAlpha);
                color.a = saturate(color.a + (_OutlineColor.a * outlineAlpha));

                return color;
            }
            ENDHLSL
        }
    }
}
