Shader "Castle Creek/Flowing Creek Bed"
{
    Properties
    {
        _MudColor ("Mud Color", Color) = (0.29, 0.23, 0.15, 1)
        _FlowColor ("Flow Color", Color) = (0.43, 0.36, 0.24, 1)
        _FlowSpeed ("Flow Speed", Float) = 1.2
        _FlowScale ("Flow Scale", Float) = 0.42
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
                half4 _MudColor;
                half4 _FlowColor;
                float _FlowSpeed;
                float _FlowScale;
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
                float2 flow = input.positionWS.xz * _FlowScale;
                flow.y += _Time.y * _FlowSpeed;
                float waviness = sin(flow.x * 2.8 + sin(flow.y * 0.65)) * 0.38;
                float ripple = sin(flow.y * 8.0 + waviness + sin(flow.x * 5.0) * 0.7);
                float detail = sin(flow.y * 13.0 + flow.x * 3.2) * 0.18;
                half streak = smoothstep(0.55, 0.9, ripple * 0.5 + 0.5 + detail);
                half3 color = lerp(_MudColor.rgb, _FlowColor.rgb, streak);
                Light light = GetMainLight();
                half lighting = saturate(dot(normalize(input.normalWS), light.direction)) * 0.55 + 0.45;
                return half4(color * lighting * light.color, 1);
            }
            ENDHLSL
        }
    }
}
