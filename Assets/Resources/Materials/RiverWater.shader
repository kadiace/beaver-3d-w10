Shader "Beaver/Pixel River Water"
{
    Properties
    {
        _ShallowColor ("01 / Shallow Turquoise", Color) = (0.37, 0.77, 0.83, 1)
        _MidColor ("02 / Clear River", Color) = (0.20, 0.66, 0.78, 1)
        _DeepColor ("03 / Deep Blue", Color) = (0.10, 0.41, 0.63, 1)
        _FoamColor ("04 / Waterline Foam", Color) = (0.80, 0.96, 0.98, 1)
        _HighlightColor ("05 / Pixel Reflections", Color) = (0.94, 0.99, 0.95, 1)
        _WakeColor ("06 / Moving Wake", Color) = (0.56, 0.85, 0.91, 1)
        _FlowDirection ("Flow Direction X/Z", Vector) = (0, -1, 0, 0)
        _PixelDensity ("World Pixels Per Metre", Range(2, 16)) = 5
        _AnimationFPS ("Water Animation FPS", Range(4, 24)) = 12
        _FlowSpeed ("Visual Flow / Metres Per Second", Range(0, 5)) = 1.3
        _DepthRange ("Depth Palette Range", Range(0.2, 8)) = 2.7
        _ShallowOpacity ("Shallow Coverage", Range(0, 1)) = 0.54
        _DeepOpacity ("Deep Coverage", Range(0, 1)) = 0.91
        _RefractionPixels ("Pixel Refraction Offset", Range(0, 2)) = 0.6
        _ContactTolerance ("Contact Height Tolerance", Range(0.1, 1)) = 0.42
        _ContactPixelRadius ("Contact Outline Pixel Radius", Range(0.5, 2.5)) = 1.2
        _ShoreDepth ("Edge Foam Depth", Range(0.02, 0.5)) = 0.15
        _BankFoamWidth ("Outer Bank Foam Width", Range(0.02, 0.6)) = 0.18
        _RiverHalfWidth ("River Half Width (Runtime)", Float) = 12
        _WakeRadiusScale ("Wake Size", Range(0.7, 2.5)) = 1.1
        _WakeTailLength ("Wake Length", Range(0.5, 6)) = 2.3
        _WakeFoamDensity ("Wake Fragment Density", Range(0, 1)) = 0.48
        _GlintCellDensity ("Surface Highlight Cells Per Metre", Range(0.25, 2)) = 0.9
        _GlintStrength ("Rare Surface Highlight Strength", Range(0, 1)) = 0.66
        _GlintCycle ("Highlight Appearance Rate", Range(0.1, 2)) = 0.7
        _ImpactReach ("Waterfall Foot Foam Distance", Range(0.2, 6)) = 2.1
        _ImpactEnabled ("Waterfall Foot Foam (Runtime)", Float) = 0
        _WaterfallImpactZ ("Waterfall Foot Z (Runtime)", Float) = -15
        _PlanarReflectionTex ("Optional Planar Reflection", 2D) = "black" {}
        _ReflectionStrength ("Optional Reflection Strength", Range(0, 0.5)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Flat Pixel River / Shallow Color + Refraction + Contact + Drift"
            Tags { "LightMode"="UniversalForward" }
            Blend One Zero
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Assets/Resources/Materials/FlatPixelWaterCommon.hlsl"

            #define MAX_RIVER_WAKES 24
            int _RiverWakeCount;
            float4 _RiverWakeObjects[MAX_RIVER_WAKES];
            float4 _RiverWakeMotion[MAX_RIVER_WAKES];

            TEXTURE2D(_PlanarReflectionTex);
            SAMPLER(sampler_PlanarReflectionTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _MidColor;
                half4 _DeepColor;
                half4 _FoamColor;
                half4 _HighlightColor;
                half4 _WakeColor;
                float4 _FlowDirection;
                float _PixelDensity;
                float _AnimationFPS;
                float _FlowSpeed;
                float _DepthRange;
                float _ShallowOpacity;
                float _DeepOpacity;
                float _RefractionPixels;
                float _ContactTolerance;
                float _ContactPixelRadius;
                float _ShoreDepth;
                float _BankFoamWidth;
                float _RiverHalfWidth;
                float _WakeRadiusScale;
                float _WakeTailLength;
                float _WakeFoamDensity;
                float _GlintCellDensity;
                float _GlintStrength;
                float _GlintCycle;
                float _ImpactReach;
                float _ImpactEnabled;
                float _WaterfallImpactZ;
                float _ReflectionStrength;
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
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float PixelRiverWakes(float2 p, float tick, float density, float2 flow)
            {
                float wake = 0.0;
                [loop]
                for (int j = 0; j < MAX_RIVER_WAKES; ++j)
                {
                    if (j >= _RiverWakeCount) break;
                    float4 body = _RiverWakeObjects[j];
                    float4 motion = _RiverWakeMotion[j];
                    float2 delta = p - body.xy;
                    float2 halfSize = max(body.zw * _WakeRadiusScale,
                                          float2(0.28, 0.28));

                    // No foam inside AABB bodies: two thin broken streamlines
                    // behind each obstacle, shaped by relative motion.
                    float aft = dot(delta, flow) / halfSize.y;
                    if (aft < 0.9 || aft > _WakeTailLength + 0.9 ||
                        abs(delta.x) > halfSize.x * 2.5)
                        continue;

                    float laneX = halfSize.x * (1.04 + aft * 0.10);
                    float stripeWidth = abs(abs(delta.x) - laneX);
                    float strand = 1.0 - smoothstep(0.045, 0.18, stripeWidth);
                    float lifetime = smoothstep(0.9, 1.16, aft) *
                        (1.0 - smoothstep(_WakeTailLength * 0.68 + 0.9,
                                          _WakeTailLength + 0.9, aft));
                    float2 moving = floor(float2(
                        p.x * density * 0.75 + motion.y,
                        (p.y + tick * _FlowSpeed * (delta.x < 0 ? 0.84 : 1.13))
                        * density * 0.62));
                    float fragments = step(1.0 - _WakeFoamDensity,
                                           PixelWaterHash(moving));
                    float motionFactor = 0.42 + 0.58 * saturate(motion.x / 4.0);
                    wake = max(wake, strand * lifetime * fragments *
                                     motionFactor);
                }
                return wake;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Cube water volumes render only their perfectly flat top face.
                clip(input.normalWS.y - 0.80);

                float density = max(1.0, _PixelDensity);
                float2 p = PixelWaterSnap(input.positionWS.xz, density);
                float tick = floor(_Time.y * max(1.0, _AnimationFPS)) /
                             max(1.0, _AnimationFPS);
                float2 flow = _FlowDirection.xy /
                    max(length(_FlowDirection.xy), 0.001);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float2 screenStep = 1.0 / _ScaledScreenParams.xy;
                float2 snappedScreen = (floor(screenUV / screenStep) + 0.5) *
                                        screenStep;
                float surfaceEye = -TransformWorldToView(input.positionWS).z;
                float behindEye = PixelWaterEyeDepth(screenUV);
                float depth = max(0.0, behindEye - surfaceEye);

                // Three deliberate color bands (Spilled!-style palette),
                // no sinusoidal displacement and no tiled wave texture.
                float d = saturate(depth / max(0.1, _DepthRange));
                half3 baseColor = _ShallowColor.rgb;
                baseColor = lerp(baseColor, _MidColor.rgb, step(0.20, d));
                baseColor = lerp(baseColor, _DeepColor.rgb, step(0.72, d));
                float alpha = lerp(_ShallowOpacity, _DeepOpacity,
                                   saturate(d * 1.25));

                // Small quantized distortion samples the opaque scene. We
                // compose the water ourselves, avoiding double transparency.
                float2 refractCell = floor((p - flow * tick * 0.21) *
                                             density * 0.6);
                float2 refractJitter = float2(
                    PixelWaterHash(refractCell + 7.0),
                    PixelWaterHash(refractCell + 31.0)) - 0.5;
                float2 refractedUV = saturate(
                    snappedScreen + refractJitter * _RefractionPixels *
                                    screenStep * (1.0 - 0.40 * d));
                half3 scene = SampleSceneColor(refractedUV);
                half3 color = lerp(scene, baseColor, alpha);

                // Optional planar-reflection capture can be supplied without
                // forcing an expensive extra camera into every scene.
                if (_ReflectionStrength > 0.0001)
                {
                    half3 reflected = SAMPLE_TEXTURE2D(
                        _PlanarReflectionTex, sampler_PlanarReflectionTex,
                        snappedScreen).rgb;
                    color = lerp(color, reflected, _ReflectionStrength);
                }

                float contact = PixelWaterContact(
                    screenUV, input.positionWS, surfaceEye,
                    float3(0, 1, 0), _ContactTolerance,
                    _ShoreDepth, _ContactPixelRadius);

                float bankGap = _RiverHalfWidth - abs(p.x);
                float bankMask = 1.0 - smoothstep(
                    0.01, max(0.025, _BankFoamWidth), bankGap);
                float bankBreak = step(0.34, PixelWaterHash(
                    floor((p - flow * tick * _FlowSpeed * 0.45) *
                          density * 0.55)));
                float thinBank = bankMask * bankBreak * 0.55;

                float wake = PixelRiverWakes(p, tick, density, flow);

                // The two downstream river sections receive the waterfall
                // immediately upstream. A non-periodic broken patch field
                // appears only in the first few metres after its foot.
                float fromFoot = _WaterfallImpactZ - p.y;
                float footBand = _ImpactEnabled *
                    step(0.0, fromFoot) *
                    (1.0 - smoothstep(0.0, _ImpactReach, fromFoot));
                float2 footCells = floor(float2(
                    p.x * density * 0.44,
                    (p.y + tick * _FlowSpeed * 0.45) * density * 0.46));
                float footFoam = footBand * step(
                    0.48, PixelWaterHash(footCells)) * 0.82;
                float foam = saturate(max(max(contact, thinBank),
                                          max(wake * 0.72, footFoam)));

                color = lerp(color, _WakeColor.rgb,
                             saturate(wake * 0.27 + footBand * 0.11));
                color = lerp(color, _FoamColor.rgb, foam * 0.93);

                // Sparse camera-horizontal strokes; unlike a scrolling sine
                // texture, separate cells appear and disappear independently.
                float glint = PixelWaterGlints(
                    p, flow, tick, density, _GlintCellDensity,
                    _FlowSpeed * 0.32, _GlintCycle) * _GlintStrength;
                glint *= 1.0 - foam * 0.75;
                color = lerp(color, _HighlightColor.rgb, glint);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
