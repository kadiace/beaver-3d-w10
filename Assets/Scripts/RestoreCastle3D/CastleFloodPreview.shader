Shader "RestoreCastle3D/FloodPreview"
{
    Properties
    {
        [MainColor] _TintColor ("Tint Color", Color) = (1, 0.08, 0.05, 0.7)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _TintColor;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Mask"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off
            ColorMask R

            HLSLPROGRAM
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment
            #pragma multi_compile_instancing

            struct MaskAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct MaskVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            MaskVaryings MaskVertex(MaskAttributes input)
            {
                MaskVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 MaskFragment(MaskVaryings input) : SV_Target
            {
                return half4(1, 1, 1, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Tint"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment TintFragment
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 TintFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half mask = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).r;
                return half4(_TintColor.rgb, _TintColor.a * mask);
            }
            ENDHLSL
        }
    }
}
