Shader "Beaver/Pixel River Water"
{
    Properties
    {
        _DeepColor ("01 / Quiet Water", Color) = (0.065, 0.25, 0.29, 1)
        _ShallowColor ("02 / Shallow Water", Color) = (0.12, 0.39, 0.42, 1)
        _CrestColor ("03 / Wake Tint", Color) = (0.29, 0.68, 0.67, 1)
        _FoamColor ("04 / Contact Foam", Color) = (0.77, 0.94, 0.85, 1)
        _GlintColor ("05 / Rare Wake Glint", Color) = (0.96, 1, 0.88, 1)
        _PixelDensity ("Pixels Per World Unit", Range(1, 12)) = 4
        _AnimationFPS ("Foam Animation FPS", Range(2, 24)) = 10
        _FlowSpeed ("Foam Advection", Range(0, 5)) = 1.3
        _FoamWidth ("Scene Depth Contact Width", Range(0.04, 2)) = 0.45
        _BankFoamWidth ("Quiet Shore Foam", Range(0.05, 2)) = 0.28
        _ShallowDepth ("Shallow Tint Depth", Range(0.1, 8)) = 1.8
        _WakeRadiusScale ("Object Foam Radius", Range(0.8, 2.5)) = 1.15
        _WakeTailLength ("Downstream Trail Length", Range(0.5, 6)) = 2.8
        _WakeFoamDensity ("Foam Density", Range(0, 1)) = 0.58
        _SparkleStrength ("Wake-only Glints", Range(0, 1)) = 0.24
        _Opacity ("Base Transparency", Range(0.25, 1)) = 0.83
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Quiet Water and Object Wakes"
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

            #define MAX_RIVER_WAKES 24
            // SetGlobalVectorArray updates these outside UnityPerMaterial.
            int _RiverWakeCount;
            float4 _RiverWakeObjects[MAX_RIVER_WAKES]; // world x,z + half-width/length
            float4 _RiverWakeMotion[MAX_RIVER_WAKES];  // relative speed, seed, flow speed

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _ShallowColor;
                half4 _CrestColor;
                half4 _FoamColor;
                half4 _GlintColor;
                float _PixelDensity;
                float _AnimationFPS;
                float _FlowSpeed;
                float _FoamWidth;
                float _BankFoamWidth;
                float _ShallowDepth;
                float _WakeRadiusScale;
                float _WakeTailLength;
                float _WakeFoamDensity;
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
                float pixelDensity = max(1.0, _PixelDensity);
                float2 p = floor(i.positionWS.xz * pixelDensity) / pixelDensity;
                float tick = floor(_Time.y * max(1.0, _AnimationFPS)) /
                    max(1.0, _AnimationFPS);

                // No full-surface oscillation, stripe, ripple or animated normal.
                // Depth adds only a restrained static shallow tint.
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceEye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float depthDelta = sceneEye - surfaceEye;
                half shallow = 1.0 - smoothstep(0.1, max(0.2, _ShallowDepth), depthDelta);
                half3 water = lerp(_DeepColor.rgb, _ShallowColor.rgb, shallow * 0.16);

                float foam = 0.0;
                float wakeGlow = 0.0;

                [loop]
                for (int j = 0; j < MAX_RIVER_WAKES; j++)
                {
                    if (j >= _RiverWakeCount) break;
                    float4 obj = _RiverWakeObjects[j];
                    float4 motion = _RiverWakeMotion[j];
                    float2 delta = p - obj.xy;
                    float2 radius = max(obj.zw * _WakeRadiusScale, float2(0.35, 0.35));
                    float slip = saturate(motion.x / 4.0);

                    // Early spatial reject: unaffected water has no work to do.
                    float tailLength = _WakeTailLength + slip * 1.7;
                    if (abs(delta.x) > radius.x * 2.3 ||
                        delta.y > radius.y * 2.0 ||
                        delta.y < -radius.y * (tailLength + 1.0))
                        continue;

                    // Pixelated, broken crescent around the floating hull.
                    float r = length(delta / radius);
                    float crescent = smoothstep(0.74, 0.97, r) *
                        (1.0 - smoothstep(1.10, 1.39, r));
                    crescent *= lerp(0.72, 1.0, smoothstep(-0.6, 0.7, delta.y / radius.y));

                    // Separate fluid speeds on both sides of the obstacle:
                    // slow inner eddies, fast outer streamlines. No sine waves.
                    float localFlow = _FlowSpeed * (0.65 + motion.z * 0.09);
                    float sideSpeed = delta.x < 0.0 ? 0.52 : 1.28;
                    float drift = tick * localFlow * sideSpeed;
                    float2 cell = floor(float2(delta.x, delta.y + drift) *
                        pixelDensity * 0.74 + motion.y);
                    float foamNoise = Hash21(cell);
                    float splitNoise = Hash21(cell + float2(17.7, 43.1));

                    float rim = crescent * step(1.0 - _WakeFoamDensity, foamNoise);

                    // Two short, sparse downstream trails; broken clusters
                    // progressively disperse into the downstream current.
                    float behind = -delta.y / radius.y;
                    float sideOffset = radius.x * (0.92 + behind * 0.08);
                    float sideDistance = abs(abs(delta.x) - sideOffset);
                    float lanes = 1.0 - smoothstep(radius.x * 0.20,
                        radius.x * 0.48, sideDistance);
                    float fade = smoothstep(0.65, 1.25, behind) *
                        (1.0 - smoothstep(tailLength * 0.62, tailLength, behind));
                    float wake = lanes * fade *
                        step(0.73, splitNoise) * (0.62 + slip * 0.30);

                    foam = max(foam, max(rim, wake));
                    wakeGlow = max(wakeGlow, max(crescent * 0.4, lanes * fade * 0.36));
                }

                // Weak, stationary depth contact and narrow shoreline foam.
                // These never animate by themselves, so empty water stays calm.
                float depthFoam = smoothstep(0.0, 0.05, depthDelta) *
                    (1.0 - smoothstep(0.1, max(0.15, _FoamWidth), depthDelta));
                float bankDistance = 12.0 - abs(p.x);
                float bankFoam = (1.0 - smoothstep(0.01,
                    max(_BankFoamWidth, 0.05), bankDistance)) * 0.22;
                float staticBreak = step(0.58, Hash21(floor(p * pixelDensity * 0.5)));
                foam = saturate(max(foam, max(depthFoam * 0.55, bankFoam * staticBreak)));

                // Wake color and occasional square glints are localized as well.
                water = lerp(water, _CrestColor.rgb, saturate(wakeGlow * 0.28));
                water = lerp(water, _FoamColor.rgb, foam);
                float glintNoise = Hash21(floor(float2(p.x, p.y + tick * _FlowSpeed)
                    * pixelDensity * 0.8 + 71.0));
                float glint = step(0.985, glintNoise) *
                    step(0.3, wakeGlow) * _SparkleStrength;
                water = lerp(water, _GlintColor.rgb, glint);
                return half4(water, saturate(lerp(_Opacity, 0.97, foam)));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
