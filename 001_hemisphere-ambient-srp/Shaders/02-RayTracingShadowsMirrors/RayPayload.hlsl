

#ifndef RAY_PAYLOAD_02
#define RAY_PAYLOAD_02

struct [raypayload] RayPayload
{
    float4 color;
    uint bounceIndex;
};

struct [raypayload] RayPayloadShadow
{
    float shadowValue;
};

#endif