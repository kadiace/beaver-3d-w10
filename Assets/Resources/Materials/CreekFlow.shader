Shader "Castle Creek/Flowing Creek Bed"
{
    Properties
    {
        _WaterColor ("Water Color", Color) = (0.12, 0.45, 0.62, 1)
        _FlowColor ("Flow Highlight Color", Color) = (0.4, 0.81, 0.9, 1)
        _FlowSpeed ("Flow Speed", Float) = 1.2
        _FlowScale ("Flow Scale", Float) = 0.42
        _VerticalFlow ("Vertical Flow (0: Creek, 1: Cliff)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _WaterColor;
                half4 _FlowColor;
                float _FlowSpeed;
                float _FlowScale;
                float _VerticalFlow;
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
                float flowLength = lerp(input.positionWS.z, input.positionWS.y, _VerticalFlow);
                float2 flow = float2(input.positionWS.x, flowLength) * _FlowScale;
                flow.y += _Time.y * _FlowSpeed;
                float waviness = sin(flow.x * 2.8 + sin(flow.y * 0.65)) * 0.38;
                float ripple = sin(flow.y * 8.0 + waviness + sin(flow.x * 5.0) * 0.7);
                float detail = sin(flow.y * 13.0 + flow.x * 3.2) * 0.18;
                half streak = smoothstep(0.55, 0.9, ripple * 0.5 + 0.5 + detail);
                half3 color = lerp(_WaterColor.rgb, _FlowColor.rgb, streak);
                Light light = GetMainLight();
                half lighting = saturate(dot(normalize(input.normalWS), light.direction)) * 0.55 + 0.45;
                return half4(color * lighting * light.color, 1);
            }
            ENDHLSL
        }
    }
}
