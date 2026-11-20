Shader "Beaver/Pixel River Water"
{
    Properties
    {
        _DeepColor ("01 / Deep Teal", Color) = (0.055, 0.20, 0.26, 1)
        _ShallowColor ("02 / Mid Teal", Color) = (0.12, 0.38, 0.42, 1)
        _CrestColor ("03 / Wave Highlight", Color) = (0.29, 0.68, 0.67, 1)
        _FoamColor ("04 / Pixel Foam", Color) = (0.77, 0.94, 0.85, 1)
        _GlintColor ("05 / Glint", Color) = (0.96, 1, 0.88, 1)
        _PixelDensity ("Pixels Per World Unit", Range(1, 12)) = 4
        _AnimationFPS ("Animation Frames Per Second", Range(2, 24)) = 10
        _FlowSpeed ("Flow Speed / Toward -Z", Range(0, 5)) = 1.3
        _RippleScale ("Wave Frequency", Range(0.2, 3)) = 0.7
        _FoamWidth ("Object Intersection Foam", Range(0.04, 2)) = 0.72
        _BankFoamWidth ("Shore Foam Width", Range(0.05, 2)) = 0.55
        _ShallowDepth ("Shallow Color Depth", Range(0.1, 8)) = 2.2
        _SparkleStrength ("Pixel Glint Strength", Range(0, 1)) = 0.8
        _Opacity ("Base Transparency", Range(0.25, 1)) = 0.83
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Pixel River"
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
                half4 _ShallowColor;
                half4 _CrestColor;
                half4 _FoamColor;
                half4 _GlintColor;
                float _PixelDensity;
                float _AnimationFPS;
                float _FlowSpeed;
                float _RippleScale;
                float _FoamWidth;
                float _BankFoamWidth;
                float _ShallowDepth;
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
            };

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // World-anchored square pixels and stepped time prevent smooth
                // noise from fighting pixel-art silhouettes or producing UV seams.
                float pixels = max(_PixelDensity, 1.0);
                float2 p = floor(i.positionWS.xz * pixels) / pixels;
                float fps = max(_AnimationFPS, 1.0);
                float tick = floor(_Time.y * fps) / fps;
                float movingZ = p.y + tick * _FlowSpeed;  // Pattern travels toward -Z.
                float lane = floor(p.x * 0.85);
                float bend = sin(p.x * 0.82 + movingZ * 0.61) * 0.36;
                float wave = sin((movingZ + bend) * 4.1 * _RippleScale +
                    Hash21(float2(lane, 9.0)) * 6.283185);
                wave += 0.26 * sin(p.x * 2.4 - movingZ * 0.91);

                // Three hard palette steps, no smooth color gradient.
                half3 water = _DeepColor.rgb;
                water = lerp(water, _ShallowColor.rgb, step(-0.37, wave));
                water = lerp(water, _CrestColor.rgb, step(0.44, wave));

                // Depth texture contains opaque objects, including rubbish
                // protruding from the water. It does not contain transparent objects.
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float waterEye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float gap = max(0.0, sceneEye - waterEye);
                float shallows = 1.0 - step(_ShallowDepth, gap);
                water = lerp(water, _ShallowColor.rgb,
                    shallows * step(0.14, Hash21(floor(float2(p.x, movingZ) * 1.4))) * 0.28);

                // Chunky, broken-up contact foam and a world-space shore strip.
                float foamDepth = 1.0 - smoothstep(0.01, max(_FoamWidth, 0.02), gap);
                foamDepth = floor(saturate(foamDepth) * 3.0 + 0.001) / 3.0;
                float distanceToBank = 12.0 - abs(p.x);
                float bankFoam = 1.0 - step(_BankFoamWidth, distanceToBank);
                float breakUp = step(0.32,
                    Hash21(floor(float2(p.x * 1.7, movingZ * 1.4))));
                float foam = saturate(max(foamDepth, bankFoam * 0.86) * breakUp);
                water = lerp(water, _FoamColor.rgb, foam);

                // Small square sun flecks, not smooth full-screen specular noise.
                Light sun = GetMainLight();
                float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);
                float3 n = normalize(float3(0.1 * sin(p.x * 3.0), 1.0,
                    0.1 * cos(movingZ * 3.3)));
                float3 reflection = reflect(-normalize(sun.direction), n);
                float sunFacing = saturate(dot(reflection, viewDir));
                float sparkleCell = Hash21(floor(float2(p.x * 1.45, movingZ * 1.9)));
                float sparkle = step(0.981, sparkleCell) *
                    step(-0.1, wave) * _SparkleStrength *
                    (0.24 + 0.76 * sunFacing * sunFacing);
                water = lerp(water, _GlintColor.rgb, saturate(sparkle));

                return half4(water, saturate(lerp(_Opacity, 0.97, foam)));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
