#ifndef BEAVER_FLAT_PIXEL_WATER_INCLUDED
#define BEAVER_FLAT_PIXEL_WATER_INCLUDED

// Shared helpers for flat river surfaces and vertical waterfall curtains.
// All pattern coordinates are snapped in world space: neither shader moves vertices.
float PixelWaterHash(float2 p)
{
    p = frac(p * float2(0.1031, 0.11369));
    p += dot(p, p.yx + 19.19);
    return frac((p.x + p.y) * p.x);
}

float2 PixelWaterSnap(float2 worldCoordinates, float density)
{
    return (floor(worldCoordinates * density) + 0.5) / density;
}

float PixelWaterEyeDepth(float2 uv)
{
    return LinearEyeDepth(SampleSceneDepth(saturate(uv)), _ZBufferParams);
}

// Detect the visible side of an opaque-object / water intersection.
// Neighboring opaque pixels must lie close to the physical water plane.
// This rejects projected silhouettes high above the water and avoids wide
// depth-only halos on surfaces that are merely shallow.
float PixelWaterContact(
    float2 screenUV,
    float3 surfaceWS,
    float surfaceEye,
    float3 planeNormal,
    float planeTolerance,
    float shallowWidth,
    float screenPixelRadius)
{
    float centerEye = PixelWaterEyeDepth(screenUV);
    float visible = step(surfaceEye + 0.012, centerEye);
    float2 screenTexel = screenPixelRadius / _ScaledScreenParams.xy;
    float sharpContour = 0.0;
    float depthContrast = 0.0;

    [unroll]
    for (int k = 0; k < 4; ++k)
    {
        float2 direction = k == 0 ? float2(1, 0) :
                           k == 1 ? float2(-1, 0) :
                           k == 2 ? float2(0, 1) : float2(0, -1);
        float2 nearbyUV = saturate(screenUV + direction * screenTexel);
        float nearbyRawDepth = SampleSceneDepth(nearbyUV);
        float nearbyEye = LinearEyeDepth(nearbyRawDepth, _ZBufferParams);
        depthContrast = max(depthContrast, abs(nearbyEye - centerEye));

        if (nearbyEye < surfaceEye - 0.012)
        {
            float3 objectWS = ComputeWorldSpacePosition(
                nearbyUV, nearbyRawDepth, UNITY_MATRIX_I_VP);
            float distanceToPlane = abs(dot(objectWS - surfaceWS, planeNormal));
            sharpContour = max(sharpContour,
                1.0 - smoothstep(planeTolerance * 0.35,
                                  planeTolerance, distanceToPlane));
        }
    }

    float shallow = 1.0 - smoothstep(0.015, max(0.025, shallowWidth),
                                    max(0.0, centerEye - surfaceEye));
    float shoreline = shallow * smoothstep(0.065, 0.32, depthContrast);
    return saturate(visible * max(sharpContour, shoreline));
}

// Short camera-horizontal glints, with independent cell lifetimes.
// Adapted from the visual logic of 3D pixel-art water, not an ocean wave.
// Drift advects the cells while the quantized time creates deliberate frames.
float PixelWaterGlints(float2 worldXZ, float2 flow, float tick,
                       float density, float cellsPerUnit,
                       float driftSpeed, float appearanceRate)
{
    float2 position = worldXZ - flow * tick * driftSpeed;
    float2 cell = floor(position * cellsPerUnit);
    float2 seed = cell + 13.0;
    float choice = PixelWaterHash(seed);
    float activeCell = step(0.76, choice);

    float2 center = (cell + float2(
        0.22 + 0.56 * PixelWaterHash(seed + 11.0),
        0.22 + 0.56 * PixelWaterHash(seed + 29.0))) / cellsPerUnit;

    // Inverse view column 0 = camera right in world space.
    float2 cameraRight = float2(UNITY_MATRIX_I_V[0][0],
                                 UNITY_MATRIX_I_V[2][0]);
    cameraRight /= max(length(cameraRight), 0.0001);
    float2 offset = position - center;
    float along = abs(dot(offset, cameraRight));
    float across = abs(dot(offset, float2(-cameraRight.y, cameraRight.x)));
    float lengthPixels = lerp(2.5, 6.5, PixelWaterHash(seed + 41.0));
    float stroke = (1.0 - step(lengthPixels * 0.5 / density, along)) *
                   (1.0 - step(0.55 / density, across));

    float phase = frac(tick * appearanceRate + choice);
    float lifetime = smoothstep(0.04, 0.23, phase) *
                     (1.0 - smoothstep(0.66, 0.94, phase));
    return activeCell * stroke * lifetime;
}

#endif
