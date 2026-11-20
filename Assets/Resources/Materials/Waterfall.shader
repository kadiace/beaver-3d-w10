Shader "Beaver/Pixel Waterfall"
{
    Properties
    {
        _DeepColor ("01 / Calm Falling Water", Color) = (0.17, 0.49, 0.55, 1)
        _MidColor ("02 / Light Falling Water", Color) = (0.27, 0.66, 0.70, 1)
        _HighlightColor ("03 / Splash Tint", Color) = (0.47, 0.79, 0.78, 1)
        _FoamColor ("04 / Fine Contact Foam", Color) = (0.89, 0.97, 0.92, 1)
        _GlintColor ("05 / Rare Splash Glint", Color) = (0.96, 1, 0.92, 1)
        _PixelDensity ("Pixels Per World Unit", Range(1, 12)) = 4
        _AnimationFPS ("Foam Animation FPS", Range(2, 24)) = 10
        _FallSpeed ("Foam Falling Speed", Range(0, 16)) = 7
        _FoamWidth ("Thin Object Contact Depth", Range(0.03, 0.5)) = 0.16
        _EdgeFoamWidth ("Rock Edge Foam Width", Range(0.03, 0.6)) = 0.20
        _SplashHeight ("Foot Splash Height / 0-1", Range(0.01, 0.25)) = 0.038
        _SparkleStrength ("Foot Glints", Range(0, 1)) = 0.16
        _Opacity ("Waterfall Transparency", Range(0.25, 1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Quiet Falling Water and Silhouette Foam"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _MidColor;
                half4 _HighlightColor;
                half4 _FoamColor;
                half4 _GlintColor;
                float _PixelDensity;
                float _AnimationFPS;
                float _FallSpeed;
                float _FoamWidth;
                float _EdgeFoamWidth;
                float _SplashHeight;
                float _SparkleStrength;
                float _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionOS = v.positionOS.xyz;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }
            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float ThinIntersection(float2 uv, float3 surfaceWS,
                                   float surfaceEye, float sceneEye,
                                   float depthRange)
            {
                float2 pixelStep = 1.3 / _ScaledScreenParams.xy;
                float2 offsets[4] = {
                    float2(pixelStep.x, 0), float2(-pixelStep.x, 0),
                    float2(0, pixelStep.y), float2(0, -pixelStep.y)
                };
                float minEye = 1e8;
                float2 nearestUV = uv;
                float nearestRaw = 0;
                float edgeContrast = 0;
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    float2 sampleUV = saturate(uv + offsets[k]);
                    float raw = SampleSceneDepth(sampleUV);
                    float sampleEye = LinearEyeDepth(raw, _ZBufferParams);
                    edgeContrast = max(edgeContrast, abs(sampleEye - sceneEye));
                    if (sampleEye < minEye)
                    {
                        minEye = sampleEye;
                        nearestUV = sampleUV;
                        nearestRaw = raw;
                    }
                }

                float centerGap = sceneEye - surfaceEye;
                float visibleWater = step(0.02, centerGap);
                float frontEdge = visibleWater *
                    (1.0 - step(surfaceEye - 0.025, minEye));
                if (frontEdge > 0.0)
                {
                    // Only a real height intersection produces foam, not
                    // the full projected silhouette of a floating object.
                    float3 objectWS = ComputeWorldSpacePosition(
                        nearestUV, nearestRaw, UNITY_MATRIX_I_VP);
                    frontEdge *= 1.0 - smoothstep(0.20, 0.58,
                        abs(objectWS.y - surfaceWS.y));
                }

                float submergedEdge = visibleWater *
                    (1.0 - smoothstep(0.02, max(0.04, depthRange), centerGap)) *
                    smoothstep(0.08, 0.30, edgeContrast);
                return max(frontEdge, submergedEdge);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float pixels = max(_PixelDensity, 1.0);
                float2 p = floor(i.positionWS.xy * pixels) / pixels;
                float fps = max(1.0, _AnimationFPS);
                float tick = floor(_Time.y * fps) / fps;
                float fallingY = p.y + tick * _FallSpeed;
                float fromBottom = saturate(i.positionOS.y + 0.5);

                // Waterfall's main curtain is calm and nearly uniform;
                // no repetitive vertical stripes, sin waves or rain texture.
                half3 water = lerp(_DeepColor.rgb, _MidColor.rgb, 0.66);

                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float waterfallEye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float contact = ThinIntersection(uv, i.positionWS,
                    waterfallEye, sceneEye, _FoamWidth);

                // The only persistent foam is where water meets rock, the
                // falling lip, or the receiving pool. It is thin and broken.
                float bankDistance = 12.0 - abs(i.positionWS.x);
                float edge = (1.0 - smoothstep(0.02,
                    max(_EdgeFoamWidth, 0.03), bankDistance)) * 0.28;
                float foot = 1.0 - smoothstep(0.006,
                    max(_SplashHeight, 0.018), fromBottom);
                float lip = smoothstep(0.988, 0.999, fromBottom) * 0.23;
                float breakup = step(0.27, Hash21(floor(
                    float2(p.x * 1.1, fallingY * 0.8) * pixels + 43.0)));
                float foam = saturate(max(contact * 0.86,
                    max(edge, max(foot * 0.64, lip)) * breakup));
                water = lerp(water, _HighlightColor.rgb, foot * 0.12);
                water = lerp(water, _FoamColor.rgb, foam * 0.84);

                // Rare glints cling to splash particles, not the curtain.
                float glintNoise = Hash21(floor(
                    float2(p.x * pixels, fallingY * pixels) + 17.0));
                float glint = step(0.988, glintNoise) *
                    step(0.35, foot) * _SparkleStrength;
                water = lerp(water, _GlintColor.rgb, glint);

                return half4(water, saturate(lerp(_Opacity, 0.96, foam)));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
