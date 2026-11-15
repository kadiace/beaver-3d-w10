Shader "BeaverRecycling/EyeGlow"
{
    Properties
    {
        [HDR] _GlowColor ("Glow Color", Color) = (0.34, 0.85, 1, 1)
        _UseVertexColor ("Use Vertex Color", Float) = 1
        _UseRadialFalloff ("Use Radial Falloff", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "EyeGlow"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half _UseVertexColor;
                half _UseRadialFalloff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = lerp(half4(1, 1, 1, 1), input.color, _UseVertexColor);
                half radial = saturate(1.0h - length(input.uv - 0.5h) * 2.0h);
                color.a *= lerp(1.0h, radial * radial, _UseRadialFalloff);
                return color * _GlowColor;
            }
            ENDHLSL
        }
    }
}
