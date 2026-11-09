Shader "Castle Creek/Flowing Creek Bed"
{
    Properties
    {
        _WaterColor ("Water Color", Color) = (0.12, 0.44, 0.61, 1)
        _FlowColor ("Soft Water Highlight", Color) = (0.30, 0.65, 0.77, 1)
        _FoamColor ("Foam Color", Color) = (0.90, 0.97, 1, 1)
        _FlowSpeed ("Flow Speed (World Units/Second)", Float) = 1.2
        _FlowScale ("Flow Detail Scale", Float) = 0.28
        _VerticalFlow ("Vertical Flow (0: Creek, 1: Waterfall)", Range(0, 1)) = 0
        _ContactWidth ("Contact Foam Depth", Range(0.05, 2)) = 0.55
        _ContactStrength ("Contact Foam Strength", Range(0, 1)) = 0.85
        _WaterfallFoam ("Waterfall Aeration", Range(0, 1)) = 0
        _ImpactCenterZ ("Waterfall Landing Z", Float) = 0
        _ImpactLength ("Impact Foam Downstream Length", Float) = 11
        _ImpactStrength ("Impact Foam Strength", Range(0, 1)) = 0
    }
    SubShader
    {
        // Camera depth is read after opaque objects to find contact with the creek.
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _WaterColor;
                half4 _FlowColor;
                half4 _FoamColor;
                float _FlowSpeed;
                float _FlowScale;
                float _VerticalFlow;
                float _ContactWidth;
                float _ContactStrength;
                float _WaterfallFoam;
                float _ImpactCenterZ;
                float _ImpactLength;
                float _ImpactStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
            };

            // Advected value noise instead of visually repeating sine-wave bands.
            float Hash(float2 cell)
            {
                return frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 position)
            {
                float2 cell = floor(position);
                float2 fraction = frac(position);
                float2 blend = fraction * fraction * (3.0 - 2.0 * fraction);
                return lerp(
                    lerp(Hash(cell), Hash(cell + float2(1, 0)), blend.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + 1.0), blend.x),
                    blend.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // Both submerged intersections and nearby silhouettes form white contact edges.
            float ContactFoam(float2 screenUV, float waterEyeDepth)
            {
                float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float gap = sceneEyeDepth - waterEyeDepth;
                float intersection = (1.0 - smoothstep(0.0, _ContactWidth, max(gap, 0.0)))
                    * step(0.015, gap);

                float2 texel = 1.5 / _ScaledScreenParams.xy;
                float nearest = min(
                    LinearEyeDepth(SampleSceneDepth(saturate(screenUV + float2(texel.x, 0))), _ZBufferParams),
                    LinearEyeDepth(SampleSceneDepth(saturate(screenUV - float2(texel.x, 0))), _ZBufferParams));
                nearest = min(nearest,
                    LinearEyeDepth(SampleSceneDepth(saturate(screenUV + float2(0, texel.y))), _ZBufferParams));
                nearest = min(nearest,
                    LinearEyeDepth(SampleSceneDepth(saturate(screenUV - float2(0, texel.y))), _ZBufferParams));

                float silhouette = step(0.05, gap) * step(0.04, waterEyeDepth - nearest);
                return saturate(max(intersection, silhouette * 0.90) * _ContactStrength);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Horizontal flow uses -Z; waterfalls use -Y and the same water palette.
                float alongFlow = lerp(input.positionWS.z, input.positionWS.y, _VerticalFlow);
                float2 flowUV = float2(input.positionWS.x, alongFlow + _Time.y * _FlowSpeed) * _FlowScale;
                float broad = Noise(flowUV * 0.78);
                float fine = Noise(flowUV * 2.65 + float2(8.3, 2.7));
                float variation = saturate(0.30 + (broad - 0.5) * 0.42 + (fine - 0.5) * 0.10);

                half3 water = lerp(_WaterColor.rgb, _FlowColor.rgb, variation);
                Light mainLight = GetMainLight();
                half diffuse = 0.80 + 0.20 * saturate(dot(normalize(input.normalWS), mainLight.direction));
                water *= diffuse * lerp(half3(1, 1, 1), mainLight.color.rgb, 0.22);

                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float eyeDepth = -TransformWorldToView(input.positionWS).z;
                float contact = ContactFoam(screenUV, eyeDepth);

                float bank = smoothstep(11.2, 12.0, abs(input.positionWS.x)) * 0.22;
                float aeration = _VerticalFlow * _WaterfallFoam
                    * smoothstep(0.66, 0.88, fine) * (0.45 + 0.55 * broad);

                // The two landing materials set their own world-space fall positions.
                float downstream = _ImpactCenterZ - input.positionWS.z;
                float tail = (1.0 - smoothstep(0.0, max(0.1, _ImpactLength), downstream))
                    * step(0.0, downstream);
                float landing = (1.0 - smoothstep(0.0, 2.6, downstream)) * step(0.0, downstream);
                float lateral = 1.0 - smoothstep(10.2, 12.0, abs(input.positionWS.x));
                float breakup = smoothstep(0.30, 0.75,
                    Noise(input.positionWS.xz * 0.65 + float2(2.4, -_Time.y * 0.32)));
                float impact = _ImpactStrength * tail * lateral
                    * saturate(0.15 + 0.62 * breakup + landing * 0.62);

                float foam = saturate(max(max(contact, bank), max(aeration, impact)));
                return half4(lerp(water, _FoamColor.rgb, foam), 1);
            }
            ENDHLSL
        }
    }
}
