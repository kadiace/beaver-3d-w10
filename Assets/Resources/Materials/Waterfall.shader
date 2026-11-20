Shader "Beaver/Pixel Waterfall"
{
    Properties
    {
        _DeepColor ("01 / Deep Waterfall", Color) = (0.06, 0.23, 0.29, 1)
        _MidColor ("02 / Flowing Turquoise", Color) = (0.15, 0.48, 0.54, 1)
        _HighlightColor ("03 / Falling Strands", Color) = (0.43, 0.77, 0.77, 1)
        _FoamColor ("04 / Splash + Intersection", Color) = (0.79, 0.94, 0.86, 1)
        _GlintColor ("05 / Droplet Sparkle", Color) = (0.95, 1, 0.90, 1)
        _PixelDensity ("Pixels Per World Unit", Range(1, 12)) = 4
        _AnimationFPS ("Animation Frames Per Second", Range(2, 24)) = 10
        _FallSpeed ("Vertical Fall Speed", Range(0, 16)) = 7
        _StripeFrequency ("Vertical Strand Density", Range(0.3, 6)) = 2.8
        _FoamWidth ("Object Intersection Foam", Range(0.04, 2)) = 0.75
        _EdgeFoamWidth ("Rock Edge Foam Width", Range(0.05, 2)) = 0.62
        _SplashHeight ("Bottom Splash Height / 0-1", Range(0.01, 0.25)) = 0.067
        _SparkleStrength ("Falling Pixel Droplets", Range(0, 1)) = 0.7
        _Opacity ("Waterfall Transparency", Range(0.25, 1)) = 0.86
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Pixel Waterfall"
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
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
                float _StripeFrequency;
                float _FoamWidth;
                float _EdgeFoamWidth;
                float _SplashHeight;
                float _SparkleStrength;
                float _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
            };

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionOS = v.positionOS.xyz;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float pixels = max(_PixelDensity, 1.0);
                float2 p = floor(i.positionWS.xy * pixels) / pixels;
                float fps = max(_AnimationFPS, 1.0);
                float tick = floor(_Time.y * fps) / fps;
                float fallingY = p.y + tick * _FallSpeed; // Pattern travels downward.

                // A stepped, broken curtain of vertical ribbons, never smooth
                // scrolling UVs. Two independent scales prevent static stripes.
                float drift = 0.25 * sin(fallingY * 0.78 + p.x * 0.65);
                float strand = sin((p.x + drift) * _StripeFrequency);
                float fragment = Hash21(floor(float2(p.x * 1.9, fallingY * 1.28)));
                float dropletRow = sin(fallingY * 5.3 + p.x * 2.7);
                half3 water = _DeepColor.rgb;
                water = lerp(water, _MidColor.rgb, step(-0.48, strand));
                water = lerp(water, _HighlightColor.rgb,
                    step(0.37, strand) * step(0.15, fragment));
                water = lerp(water, _HighlightColor.rgb,
                    step(0.72, dropletRow) * step(0.55, strand) * 0.62);

                // Depth intersection silhouettes opaque debris or rock as foam.
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float waterfallEye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float gap = max(0.0, sceneEye - waterfallEye);
                float contact = 1.0 - smoothstep(0.01, max(_FoamWidth, 0.02), gap);
                contact = floor(saturate(contact) * 3.0 + 0.001) / 3.0;

                // Both falls are 24m-wide cubes. Use object-space height, so one
                // material generates foot splashes at y=-3 and y=-33 correctly.
                float bankDistance = 12.0 - abs(p.x);
                float edgeFoam = 1.0 - step(_EdgeFoamWidth, bankDistance);
                float fromBottom = saturate(i.positionOS.y + 0.5);
                float splash = 1.0 - smoothstep(0.0, _SplashHeight, fromBottom);
                splash = floor(saturate(splash) * 4.0 + 0.001) / 4.0;
                float lip = step(0.985, fromBottom);
                float foamBreak = step(0.22,
                    Hash21(floor(float2(p.x * 2.0, fallingY * 1.6))));
                float foam = saturate(max(max(contact, edgeFoam * 0.8),
                    max(splash, lip * 0.62)) * foamBreak);
                water = lerp(water, _FoamColor.rgb, foam);

                // Bright one-cell droplets sparkle on the falling strands.
                Light sun = GetMainLight();
                float flash = Hash21(floor(float2(p.x * 1.7 + 31.0, fallingY * 2.15)));
                float brightness = saturate(0.45 + 0.55 * normalize(sun.direction).y);
                float glint = step(0.977, flash) * step(0.23, strand) *
                    _SparkleStrength * brightness;
                water = lerp(water, _GlintColor.rgb, glint);

                return half4(water, saturate(lerp(_Opacity, 0.98, foam)));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
