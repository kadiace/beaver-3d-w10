Shader "Beaver/Pixel Waterfall"
{
    Properties
    {
        _DeepColor ("01 / Falling Water Shadow", Color) = (0.12, 0.43, 0.66, 1)
        _MidColor ("02 / Falling Water", Color) = (0.24, 0.65, 0.81, 1)
        _BrightColor ("03 / Falling Veins", Color) = (0.59, 0.86, 0.93, 1)
        _FoamColor ("04 / Plunge Foam", Color) = (0.80, 0.96, 0.99, 1)
        _GlintColor ("05 / Splash Sparkle", Color) = (0.96, 1, 1, 1)
        _PixelDensity ("World Pixels Per Metre", Range(2, 16)) = 5
        _AnimationFPS ("Waterfall Animation FPS", Range(4, 24)) = 12
        _FallSpeed ("Downward Pattern Speed", Range(1, 20)) = 9
        _StreakDensity ("Streak Lanes Per Metre", Range(0.3, 4)) = 1.0
        _StreakBreakup ("Streak Frequency", Range(0.05, 0.9)) = 0.35
        _ContactTolerance ("Rock Contact Plane Tolerance", Range(0.1, 1.2)) = 0.53
        _ContactPixelRadius ("Contact Outline Pixel Radius", Range(0.5, 2.5)) = 1.3
        _EdgeFoamWidth ("Rock Side Foam Metres", Range(0.03, 0.6)) = 0.18
        _HalfWidth ("Falls Half Width", Float) = 12
        _LipHeight ("Falling Lip Fraction", Range(0.005, 0.10)) = 0.028
        _SplashHeight ("Plunge Foam Height Fraction", Range(0.01, 0.22)) = 0.092
        _SplashCoverage ("Plunge Foam Coverage", Range(0, 1)) = 0.72
        _SparkleStrength ("Foot Sparkle Strength", Range(0, 1)) = 0.35
        _Opacity ("Curtain Transparency", Range(0.3, 1)) = 0.88
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Pixel Falling Veins / Rock Contour / Broken Plunge Foam"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Assets/Resources/Materials/FlatPixelWaterCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _MidColor;
                half4 _BrightColor;
                half4 _FoamColor;
                half4 _GlintColor;
                float _PixelDensity;
                float _AnimationFPS;
                float _FallSpeed;
                float _StreakDensity;
                float _StreakBreakup;
                float _ContactTolerance;
                float _ContactPixelRadius;
                float _EdgeFoamWidth;
                float _HalfWidth;
                float _LipHeight;
                float _SplashHeight;
                float _SplashCoverage;
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
                float3 positionOS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // A waterfall is a thin upright curtain, not an animated
                // solid cuboid. Ignore its top, bottom and lateral cube faces.
                clip(abs(input.normalWS.z) - 0.80);

                float density = max(1.0, _PixelDensity);
                float2 p = PixelWaterSnap(input.positionWS.xy, density);
                float tick = floor(_Time.y * max(1.0, _AnimationFPS)) /
                             max(1.0, _AnimationFPS);
                float movingY = p.y + tick * _FallSpeed;
                float fromBottom = saturate(input.positionOS.y + 0.5);
                float fromTop = 1.0 - fromBottom;

                // Flat main curtain with only three discrete tones.
                // No full-width sinusoidal stripes and no vertex waves.
                float xLane = floor(p.x * _StreakDensity);
                float laneVariation = PixelWaterHash(float2(xLane, 4.0));
                half3 water = lerp(_DeepColor.rgb, _MidColor.rgb,
                                  0.70 + 0.20 * laneVariation);

                // Broken, narrow, downward-travelling vertical highlights.
                // Each lane has independently seeded dash lengths and phases.
                float2 movingCell = float2(
                    xLane, floor(movingY * 0.66));
                float seed = PixelWaterHash(movingCell + 9.0);
                float xPhase = frac(p.x * _StreakDensity);
                float target = 0.16 + 0.66 * PixelWaterHash(
                    float2(xLane, 31.0));
                float thinLine = 1.0 - step(0.12, abs(xPhase - target));
                float verticalPhase = frac(movingY * 0.66);
                float dashLength = 0.24 + 0.50 * PixelWaterHash(
                    movingCell + 19.0);
                float dash = 1.0 - step(dashLength, verticalPhase);
                float appears = step(1.0 - _StreakBreakup, seed);
                float streak = thinLine * dash * appears;
                water = lerp(water, _BrightColor.rgb, streak * 0.49);

                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float surfaceEye = -TransformWorldToView(input.positionWS).z;
                float contact = PixelWaterContact(
                    uv, input.positionWS, surfaceEye,
                    float3(0, 0, 1), _ContactTolerance,
                    0.14, _ContactPixelRadius);

                // Broken foam along both rock edges, keyed to the visible
                // geometry bounds instead of an unrelated tiled wave texture.
                float edgeGap = _HalfWidth - abs(p.x);
                float side = 1.0 - smoothstep(
                    0.008, max(0.03, _EdgeFoamWidth), edgeGap);
                float sideBreakup = step(0.40, PixelWaterHash(
                    floor(float2(p.x * density * 0.75,
                                 movingY * density * 0.47))));
                float rockFoam = side * sideBreakup * 0.78;

                float lip = 1.0 - smoothstep(0.0, _LipHeight, fromTop);
                float lipBreakup = step(0.26, PixelWaterHash(
                    floor(float2(p.x * density * 0.40,
                                 movingY * density * 0.25))));
                float brightLip = lip * lipBreakup * 0.40;

                // A wide but irregular mass of chunky turquoise/white
                // splash cells collects at the receiving pool. These are
                // raster shapes in the curtain; there is no fluid sim.
                float foot = 1.0 - smoothstep(
                    0.01, max(0.02, _SplashHeight), fromBottom);
                float2 bubbleCellA = floor(float2(
                    p.x * density * 0.47 + tick * 0.75,
                    movingY * density * 0.24));
                float2 bubbleCellB = floor(float2(
                    p.x * density * 0.28 - tick * 0.42,
                    movingY * density * 0.31));
                float bubbleA = step(1.0 - _SplashCoverage,
                                     PixelWaterHash(bubbleCellA));
                float bubbleB = step(0.60, PixelWaterHash(
                    bubbleCellB + 73.0));
                float foamMass = foot * saturate(
                    bubbleA * 0.78 + bubbleB * 0.43);

                water = lerp(water, _BrightColor.rgb, foot * 0.25);
                float foam = saturate(max(
                    max(contact * 0.95, rockFoam),
                    max(brightLip, foamMass)));
                water = lerp(water, _FoamColor.rgb, foam * 0.92);

                float sparks = foot * step(0.981, PixelWaterHash(
                    floor(float2(p.x * density,
                                 movingY * density)) + 103.0));
                water = lerp(water, _GlintColor.rgb,
                             sparks * _SparkleStrength);

                return half4(water,
                             saturate(lerp(_Opacity, 0.985, foam)));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
