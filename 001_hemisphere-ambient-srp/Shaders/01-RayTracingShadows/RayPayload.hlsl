#ifndef RAY_PAYLOAD_01
#define RAY_PAYLOAD_01

struct [raypayload] RayPayload
{
    float4 color;
    float rayTHit;
    float isMiss;
};

struct [raypayload] RayPayloadShadow
{
    float shadowValue;
};

#endif