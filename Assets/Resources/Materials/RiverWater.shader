Shader "Beaver/Pixel River Water"
{
    Properties
    {
        _DeepColor ("01 / Calm Water", Color) = (0.18, 0.51, 0.56, 1)
        _ShallowColor ("02 / Light Water", Color) = (0.26, 0.65, 0.66, 1)
        _CrestColor ("03 / Wake Tint", Color) = (0.44, 0.76, 0.72, 1)
        _FoamColor ("04 / Fine Contact Foam", Color) = (0.89, 0.97, 0.91, 1)
        _GlintColor ("05 / Rare Wake Glint", Color) = (0.97, 1, 0.93, 1)
        _PixelDensity ("Pixels Per World Unit", Range(1, 12)) = 4
        _AnimationFPS ("Foam Animation FPS", Range(2, 24)) = 10
        _FlowSpeed ("Foam Advection", Range(0, 5)) = 1.3
        _FoamWidth ("Thin Contact Depth Range", Range(0.03, 0.5)) = 0.16
        _BankFoamWidth ("Shore Foam Width", Range(0.02, 0.5)) = 0.12
        _ShallowDepth ("Shallow Tint Depth", Range(0.1, 8)) = 1.8
        _WakeRadiusScale ("Wake Spread", Range(0.8, 2.5)) = 1.10
        _WakeTailLength ("Wake Length", Range(0.5, 6)) = 1.8
        _WakeFoamDensity ("Wake Density", Range(0, 1)) = 0.42
        _SparkleStrength ("Wake-only Glints", Range(0, 1)) = 0.12
        _Opacity ("Water Transparency", Range(0.25, 1)) = 0.83
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Calm Water and Thin Silhouette Foam"
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
            int _RiverWakeCount;
            float4 _RiverWakeObjects[MAX_RIVER_WAKES]; // World xz, collider half-extents xz.
            float4 _RiverWakeMotion[MAX_RIVER_WAKES];  // Slip speed, seed, current.

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

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }
            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // Screen-space silhouette detection: unlike the old circular mask,
            // this follows the depth edges of every opaque object, not just the
            // first 24 objects whose wake data was uploaded.
            float ThinIntersection(float2 uv, float3 surfaceWS, float surfaceEye,
                                   float sceneEye, float depthRange)
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
                float frontEdge = visibleWater * (1.0 - step(surfaceEye - 0.025, minEye));
                if (frontEdge > 0.0)
                {
                    // Avoid projected silhouettes from objects floating high
                    // above the water: this is a waterline, not an object outline.
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
                float pixelDensity = max(1.0, _PixelDensity);
                float2 p = floor(i.positionWS.xz * pixelDensity) / pixelDensity;
                float fps = max(1.0, _AnimationFPS);
                float tick = floor(_Time.y * fps) / fps;

                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float surfaceEye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float depthDelta = sceneEye - surfaceEye;

                // Almost flat, bright water. No repeating wave or full-field noise.
                half shallow = 1.0 - smoothstep(0.18,
                    max(0.2, _ShallowDepth), depthDelta);
                half3 water = lerp(_DeepColor.rgb, _ShallowColor.rgb, shallow * 0.23);

                // Actual screen-depth contour, not a fake ring around AABB centers.
                float fineContact = ThinIntersection(
                    uv, i.positionWS, surfaceEye, sceneEye, _FoamWidth);
                float bankDistance = 12.0 - abs(i.positionWS.x);
                float shore = (1.0 - smoothstep(0.01,
                    max(0.03, _BankFoamWidth), bankDistance)) * 0.20;
                float foam = max(fineContact * 0.85, shore);
                float trailing = 0;

                // Only sparse wakes use the selected object list. Contact foam
                // above is not capped, and the wake cannot fill an object's center.
                [loop]
                for (int j = 0; j < MAX_RIVER_WAKES; j++)
                {
                    if (j >= _RiverWakeCount) break;
                    float4 body = _RiverWakeObjects[j];
                    float4 motion = _RiverWakeMotion[j];
                    float2 delta = p - body.xy;
                    float2 halfSize = max(body.zw * _WakeRadiusScale,
                        float2(0.35, 0.35));
                    float aft = -delta.y / halfSize.y;
                    if (aft < 0.82 || aft > _WakeTailLength + 0.82 ||
                        abs(delta.x) > halfSize.x * 2.3)
                        continue;

                    // Slim, separated trails along either side of the body,
                    // not a round crescent or a solid patch at its center.
                    float side = sign(delta.x);
                    float laneX = halfSize.x * (1.02 + aft * 0.09);
                    float offset = abs(abs(delta.x) - laneX);
                    float filament = 1.0 - smoothstep(0.045, 0.19, offset);
                    float fade = smoothstep(0.82, 1.12, aft) *
                        (1.0 - smoothstep(_WakeTailLength * 0.67 + 0.82,
                            _WakeTailLength + 0.82, aft));
                    float slip = saturate(motion.x / 4.0);
                    float sideSpeed = side < 0 ? 0.72 : 1.26;
                    float2 advected = floor(float2(
                        p.x * pixelDensity * 0.72 + motion.y,
                        (p.y + tick * _FlowSpeed * sideSpeed) *
                        pixelDensity * 0.6));
                    float fragment = step(1.0 - _WakeFoamDensity,
                        Hash21(advected));
                    float trail = filament * fade * fragment *
                        (0.20 + 0.20 * slip);
                    trailing = max(trailing, trail);
                }

                foam = saturate(max(foam, trailing));
                water = lerp(water, _CrestColor.rgb, trailing * 0.18);
                water = lerp(water, _FoamColor.rgb, foam * 0.85);

                // Glints only appear in localized trails, never on empty water.
                float glint = step(0.987, Hash21(floor(
                    (p + float2(0, tick * _FlowSpeed)) * pixelDensity)))
                    * step(0.14, trailing) * _SparkleStrength;
                water = lerp(water, _GlintColor.rgb, glint);
                return half4(water, saturate(lerp(_Opacity, 0.95, foam)));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
