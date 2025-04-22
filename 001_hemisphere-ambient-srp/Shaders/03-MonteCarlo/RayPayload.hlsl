

#ifndef RAY_PAYLOAD_03
#define RAY_PAYLOAD_03


#define FLAG_SPECULAR_BOUNSE 1

struct RayPayload
{
    float4 energy;
    float4 color;
    float4 albedo;
    float4 worldPos;
    float4 worldNormal;
    float pixelSeed;
    int2 pixelXY;
    uint bounceIndex;
    uint invalidatePrevFrame;
    uint hiddenObjectPath; // the camera ray hitted a hidden object
};

struct RayPayloadShadow
{
    float shadowValue;
};

#endif