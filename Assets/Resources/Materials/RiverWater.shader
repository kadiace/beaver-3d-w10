Shader "Beaver/River Water"
{
    Properties
    {
        _DeepColor ("Deep Water", Color) = (0.025, 0.16, 0.22, 0.82)
        _ShallowColor ("Shallow Water", Color) = (0.08, 0.45, 0.48, 0.82)
        _FoamColor ("Foam", Color) = (0.79, 0.96, 0.91, 1)
        _FlowSpeed ("Flow Speed", Range(0, 5)) = 1.2
        _RippleScale ("Ripple Scale", Range(0.1, 5)) = 0.7
        _FoamWidth ("Intersection Foam Width", Range(0.05, 2)) = 0.8
        _SparkleStrength ("Sun Glints", Range(0, 2)) = 0.7
        _Opacity ("Water Opacity", Range(0, 1)) = 0.77
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "River Water"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _ShallowColor;
                half4 _FoamColor;
                float _FlowSpeed;
                float _RippleScale;
                float _FoamWidth;
                float _SparkleStrength;
                float _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 coord = input.positionWS.xz * _RippleScale;
                float time = _Time.y * _FlowSpeed;
                float s0 = sin(coord.y * 3.2 + time * 2.3 + sin(coord.x * 2.7));
                float s1 = sin(coord.x * 5.1 + coord.y * 4.9 + time * 1.3);
                float2 slope = float2(cos(coord.x * 4.2 + time), cos(coord.y * 4.7 + time * 2.2));
                half3 normalWS = normalize(float3(slope.x * 0.095, 1.0, slope.y * 0.095));
                float streak = smoothstep(0.35, 0.90, s0 * 0.5 + s1 * 0.16 + 0.5);
                half3 water = lerp(_DeepColor.rgb, _ShallowColor.rgb, 0.38 + streak * 0.4);

                // Depth-based foam wherever opaque objects or banks meet the surface.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float waterEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float foamDepth = 1.0 - smoothstep(0.01, _FoamWidth, max(0.0, sceneEye - waterEye));

                // Shore foam persists even where the depth buffer has only sky.
                float bankDistance = 12.0 - abs(input.positionWS.x);
                float shore = 1.0 - smoothstep(0.08, 0.85, bankDistance);
                float noise = smoothstep(0.08, 0.8,
                    sin(coord.y * 6.2 + time * 3.5 + sin(coord.x * 8.1)) * 0.5 + 0.5);
                float foam = saturate(max(foamDepth * 0.88, shore) * noise);
                water = lerp(water, _FoamColor.rgb, foam * 0.82);

                Light sun = GetMainLight();
                float3 viewDir = normalize(GetCameraPositionWS() - input.positionWS);
                float3 reflected = reflect(-normalize(sun.direction), normalWS);
                float specular = pow(saturate(dot(reflected, viewDir)), 34.0);
                float fleck = smoothstep(0.84, 0.98, s0 * s1 * 0.5 + 0.5);
                water += _SparkleStrength * specular * fleck * sun.color.rgb;
                return half4(water, saturate(_Opacity + foam * 0.17));
            }
            ENDHLSL
        }
    }
}
