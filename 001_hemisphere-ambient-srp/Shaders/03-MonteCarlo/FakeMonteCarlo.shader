Shader "Custom SRP/Fake Monte Carlo Material"
{
    Properties
    {
    	// TODO: revisar si es necesario el ShaderPropertyFlags.MainTexture
    	// https://docs.unity3d.com/ScriptReference/Rendering.ShaderPropertyFlags.MainTexture.html
        [Header(Albedo characteristics)]
        [MainColor] _BaseColor("Albedo Color", Color) = (1.0, 0.0, 1.0, 1.0)
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}

        [Header(Specular characteristics)]
        [MainColor] _SpecularColor("Specular Color", Color) = (1.0, 0.0, 1.0, 1.0)
        [MainTexture] _SpecularMap ("Specular", 2D) = "white" {}
        // [MainColor] _EmissionColor("Emission Color", Color) = (0.0, 0.0, 0.0, 0.0) // esta comentado porque por ahora no soportamos materiales emisivos
        // [KeywordEnum(DIFFUSE, MIRROR, TRANSPARENT)] _GI_STATE("Surface Type", float) = 0

        [PerRendererData] _InvalidatePrevFrame ("InvalidatePrevFrame", Int ) = 0

        [Header(Surface characteristics)]
        _SpecTrans("Specular Transparency",Range(0,1)) = 1
        _Roughness("Roughness",Range(0.02,1)) = 0
        _Metallic("Metalness",Range(0,1)) = 0
    }

    SubShader
    {
        Tags {  "LightMode" = "SceneViewLightMode" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #include "../SRPUtils.hlsl"
            ENDCG
        }
    }


    SubShader
    {
        Pass
        {
        // SetShaderPass must use this name in order to execute the ray tracing shaders from this Pass.
            Name "Test"

            Tags{ "LightMode" = "RayTracing" }

            HLSLPROGRAM

            #pragma shader_feature _GI_STATE_DIFFUSE _GI_STATE_MIRROR _GI_STATE_TRANSPARENT

            #pragma raytracing test

            #include "UnityRaytracingMeshUtils.cginc"
            #include "RayPayload.hlsl"

            #ifndef PI
            #define PI 3.141592653589f
            #endif

            #ifndef TWO_PI
            #define TWO_PI 6.2831853071796f
            #endif

            #ifndef ONE_OVER_PI
            #define ONE_OVER_PI 0.318309886184f
            #endif

            // Specifies minimal reflectance for dielectrics (when metalness is zero)
            // Nothing has lower reflectance than 2%, but we use 4% to have consistent results with UE4, Frostbite, et al.
            #ifndef MIN_DIELECTRICS_F0
            #define MIN_DIELECTRICS_F0 0.04f
            #endif

            cbuffer MonteCarloShaderCB {
                uint frameNumber; // TODO: evaluar is es mejor pasar este valor o directamente un seed usando System.Random()
                Texture2D _BaseMap;
                float4 _BaseMap_ST;
                float4 _BaseColor;
                Texture2D _SpecularMap;
                float4 _SpecularMap_ST;
                float4 _SpecularColor;
                SamplerState sampler_linear_repeat;
                RaytracingAccelerationStructure g_SceneAccelStruct;

                int _InvalidatePrevFrame;

                float _SpecTrans;
                float _Roughness;
                float _Metallic;
                float3 g_sun_direction;
                float g_sun_intensity;
                uint g_max_recursion_depth;
            }

            struct AttributeData
            {
                float2 barycentrics;
            };


            struct HitInfo
            {
                // float3 position;
                float3 normal;
                float2 uv;
            };

            float rand(inout RayPayload payload) //(float2 pixel, uint depth, inout float seed)
            {
                float result = frac(sin(payload.pixelSeed / 100.0f * dot(payload.pixelXY, float2(12.9898f, 78.233f))) * 43758.5453f);
                payload.pixelSeed += /*frameNumber + payload.bounceIndex +*/ 1 ;
                return result;
            }

            HitInfo FetchVertex(uint vertexIndex)
            {
                HitInfo v;
                // v.position  = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
                v.normal    = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
                v.uv        = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
                return v;
            }

            HitInfo GetInterpolatedVertices(float3 barycentrics)
            {
                uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

                HitInfo v0 = FetchVertex(triangleIndices.x);
                HitInfo v1 = FetchVertex(triangleIndices.y);
                HitInfo v2 = FetchVertex(triangleIndices.z);

                HitInfo v;
                #define INTERPOLATE_ATTRIBUTE(attr) v.attr = v0.attr * barycentrics.x + v1.attr * barycentrics.y + v2.attr * barycentrics.z
                // INTERPOLATE_ATTRIBUTE(position);
                INTERPOLATE_ATTRIBUTE(normal);
                INTERPOLATE_ATTRIBUTE(uv);
                return v;
            }

            //return the roation matrix
            float3x3 GetTransformMatrix(float3 normal)
            {
                // Choose a helper vector for the cross product
                float3 helper = float3(1, 0, 0);
                if (abs(normal.x) > 0.99f)
                    helper = float3(0, 0, 1);

                // Generate vectors
                float3 tangent = normalize(cross(normal, helper));
                float3 binormal = normalize(cross(normal, tangent));
                return float3x3(tangent, binormal, normal);
            }


            // importance sampling sobre una esfera
            // ver: 13.6.3 COSINE-WEIGHTED HEMISPHERE SAMPLING
            // Justificación:
            // * cuando el cos(angulo con la normal) ~> 1, se utiliza el rayo que rebota: sale todo para atrás
            // * cuando el cos(angulo con la normal) ~> 0, el rayo no aporta, por lo que se quiere evitar tirar rayos en esa dirección
            // * se quieren rayos "más hacia la normal"
            //
            // Código extraido de:
            // * http://three-eyed-games.com/2018/05/12/gpu-path-tracing-in-unity-part-2/
            // * https://blog.thomaspoulet.fr/uniform-sampling-on-unit-hemisphere/
            // * https://corysimon.github.io/articles/uniformdistn-on-sphere/
            float3 SampleHemisphere(float3 normal, float alpha, inout RayPayload payload)
            {
                // Sample the hemisphere, where alpha determines the kind of the sampling
                // float cosTheta = pow(rand(payload), 1.0f / (alpha + 1.0f));
                float cosTheta = sqrt(1.0f - pow(rand(payload), 2));
                float sinTheta = sqrt(1.0f - cosTheta * cosTheta);
                float phi = TWO_PI * rand(payload);
                float3 tangentSpaceDir = float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);

                // Transform direction to be centered around whatever noraml we need
                return mul(tangentSpaceDir, GetTransformMatrix(normal));
            }

            //SampleHemisphere alpha = 1
            float CosinSamplingPDF(float NdotL)
            {
                return NdotL * ONE_OVER_PI;
            }

            float ShootShadowRay(in float3 worldPosition, in float3 Li){
                    RayDesc shadowRay;
                    shadowRay.Origin = worldPosition;
                    shadowRay.Direction = Li;
                    shadowRay.TMin = 0.001f;
                    shadowRay.TMax = 1e20f;

                    RayPayloadShadow payloadShadow;
                    payloadShadow.shadowValue = 0;

                    const uint missShaderForShadowRay = 0;
                    const uint totalHitGroups = 2;
                    TraceRay(g_SceneAccelStruct, RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER /*| RAY_FLAG_CULL_BACK_FACING_TRIANGLES*/, 0xFF, 1, 2, missShaderForShadowRay, shadowRay, payloadShadow);

                    return payloadShadow.shadowValue;
            }


            // GGX: es una "microfacet distribution"
            //
            // Ver:
            // * https://www.cs.cornell.edu/~srm/publications/EGSR07-btdf.pdf
            // * https://schuttejoe.github.io/post/ggximportancesamplingpart1/
            // * https://learnopengl.com/PBR/IBL/Specular-IBL
            //
            // Código:
            // * https://learnopengl.com/code_viewer_gh.php?code=src/6.pbr/2.2.1.ibl_specular/2.2.1.brdf.fs
            //
            // we'll generate sample vectors biased towards the general reflection orientation of
            // the microsurface halfway vector based on the surface's roughness
            //
            //TODO: no se usa el parámetro V
            float3 ImportanceSampleGGX(float2 Xi, float3 N, float3 V, float roughness)
            {
                float a = roughness * roughness;

                float phi = TWO_PI * Xi.x;
                float cosTheta = sqrt((1.0 - Xi.y) / (1.0 + (a * a - 1.0) * Xi.y));
                float sinTheta = sqrt(1.0 - cosTheta * cosTheta);

                // from spherical coordinates to cartesian coordinates
                float3 H;
                H.x = cos(phi) * sinTheta;
                H.y = sin(phi) * sinTheta;
                H.z = cosTheta;

                // from tangent-space vector to world-space sample vector
                float3 up = abs(N.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
                float3 tangent = normalize(cross(up, N));
                float3 bitangent = cross(N, tangent);

                float3 halfVec = tangent * H.x + bitangent * H.y + N * H.z;
                halfVec = normalize(halfVec);

                return halfVec;

            }

            float ImportanceSampleGGX_PDF(float NDF, float NdotH, float VdotH)
            {
                //ImportanceSampleGGX pdf
                //pdf = D * NoH / (4 * VoH)
                return NDF * NdotH / (4 * VdotH);

            }

            //TODO: en vez de recibir float3 I y N como parámetro, que reciba 1 float con IdotN
            //calculate fresnel
            float Calculatefresnel(const float3 I, const float3 N, const float3 ior)
            {
                float kr;
                float cosi = clamp(-1, 1, dot(I, N));
                float etai = 1, etat = ior;
                if (cosi > 0)
                {
                    //std::swap(etai, etat);
                    float temp = etai;
                    etai = etat;
                    etat = temp;
                }
                // Compute sini using Snell's law
                float sint = etai / etat * sqrt(max(0.f, 1 - cosi * cosi));

                // Total internal reflection
                if (sint >= 1)
                {
                    kr = 1;
                }
                else
                {
                    float cost = sqrt(max(0.f, 1 - sint * sint));
                    cosi = abs(cosi);
                    float Rs = ((etat * cosi) - (etai * cost)) / ((etat * cosi) + (etai * cost));
                    float Rp = ((etai * cosi) - (etat * cost)) / ((etai * cosi) + (etat * cost));
                    kr = (Rs * Rs + Rp * Rp) / 2;
                }
                return kr;
                // As a consequence of the conservation of energy, transmittance is given by:
                // kt = 1 - kr;
            }

            // Ver: https://learnopengl.com/PBR/Theory  - Normal distribution function
            //TODO: en vez de recibir float3 N y H como parámetro, que reciba 1 float con NdotH
            float DistributionGGX(float3 N, float3 H, float roughness)
            {
                float a = roughness * roughness;
                float a2 = a * a;
                float NdotH = max(dot(N, H), 0.0);
                float NdotH2 = NdotH * NdotH;

                float nom = a2;
                float denom = (NdotH2 * (a2 - 1.0) + 1.0);
                denom = PI * denom * denom;

                return nom / denom;
            }

            // Ver: https://learnopengl.com/PBR/Theory  - Fresnel equation
            float3 FresnelSchlick(float cosTheta, float3 F0)
            {
                return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
            }

            // Ver: https://learnopengl.com/PBR/Theory  - Geometry function
            //TODO: no pasar el roughness como parámetro
            float GeometrySchlickGGX(float NdotV, float roughness)
            {
                float r = (roughness + 1.0);
                float k = (r * r) / 8.0;

                float nom = NdotV;
                float denom = NdotV * (1.0 - k) + k;

                return nom / denom;
            }

            // Ver: https://learnopengl.com/PBR/Theory  - Geometry function
            // NOTA: en el link anterior se usa:
            //    float NdotV = max(dot(N, V), 0.0);
            //    float NdotL = max(dot(N, L), 0.0);
            // Hay que revisar si son equivalentes y encontrar la fuente del abs(dot(N,V))
            //
            //TODO: en vez de recibir float3 N,V y L como parámetro, que reciba 2 float con NdotV y NdotL
            //TODO: no pasar el roughness como parámetro
            float GeometrySmith(float3 N, float3 V, float3 L, float roughness)
            {
                float NdotV = abs(dot(N, V));
                float NdotL = abs(dot(N, L));
                float ggx2 = GeometrySchlickGGX(NdotV, roughness);
                float ggx1 = GeometrySchlickGGX(NdotL, roughness);

                return ggx1 * ggx2;
            }

            // TODO: obtener info de donde sacamos la función
            //TODO: en vez de recibir float3 N,V y L como parámetro, que reciba 2 float con NdotV y NdotL
            float3 SpecularBRDF(float D, float G, float3 F, float3 V, float3 L, float3 N)
            {
                float NdotL = abs(dot(N, L));
                float NdotV = abs(dot(N, V));

                //specualr
                //Microfacet specular = D * G * F / (4 * NoL * NoV)
                float3 nominator = D * G * F;
                float denominator = 4.0 * NdotV * NdotL + 0.001;
                float3 specularBrdf = nominator / denominator;

                return specularBrdf;
            }

            float3 DiffuseBRDF(float3 albedo)
            {
                return albedo * ONE_OVER_PI;
            }

            //TODO: en vez de recibir float3 V,L,N y H como parámetro, que reciba 4 float con NdotV, NdotL, VdotH y LdotH
            float3 RefractionBTDF(float D, float G, float3 F, float3 V, float3 L, float3 N, float3 H, float etaIn, float etaOut)
            { //Not reciprocal! be careful about direction!

                float NdotL = abs(dot(N, L));
                float NdotV = abs(dot(N, V));

                float VdotH = abs(dot(V, H));
                float LdotH = abs(dot(L, H));


                float term1 = VdotH * LdotH / (NdotV * NdotL);
                float3 term2 = etaOut * etaOut * (1 - F) * G * D;

                float term3 = (etaIn * VdotH + etaOut * LdotH) * (etaIn * VdotH + etaOut * LdotH) + 0.001f;

                float3 refractionBrdf = term1 * term2 / term3;

                return refractionBrdf;
            }

            [shader("closesthit")]
            void ClosestHitMain(inout RayPayload payload : SV_RayPayload, in AttributeData attribs : SV_IntersectionAttributes)
            {
                // parámetros
                // * payload ~> lo definimos nosotros: color normal posición etc
                //              lo utilizamos para compartir información entre los distintos niveles de recursión
                // * attribs ~> se utiliza para el cálculo de coordenadas baricentricas

                float initialEnergy = payload.energy;

                // obtenemos valores interpolados de los vertices según la posición dentro del triángulo
                float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
                HitInfo v = GetInterpolatedVertices(barycentricCoords);

                // v utiliza coordenadas del objeto, pasamos la normal a coordenadas del mundo para comparar con el resto
                float3 faceNormal = normalize(mul((float3x3)ObjectToWorld(),v.normal));

                // ajustamos la normal para que la normal y el rayo estén en el mismo semiespacio (el triángulo es el plano que separa)
                bool isFrontFace = (HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE);
                faceNormal = isFrontFace ? faceNormal : -faceNormal;


                // Light In direction (Light -> hit position)
                // obtenemos la dirección normalizada donde se encuentra el sol
                float3 Li = normalize(-g_sun_direction);
                // calculamos en que posición del mundo chocó el rayo con la geometría
                float3 worldPosition = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();

                // se calcula el color sin iluminación (albedo) de la superficie: color base del material * color de la textura
                float4 albedoColor = _BaseMap.SampleLevel(sampler_linear_repeat, v.uv * _BaseMap_ST.xy + _BaseMap_ST.zw, 0) * _BaseColor;
                // se calcula el color para ser utilizado en el calculo del color especular (brillo)
                float4 specularColor = _SpecularMap.SampleLevel(sampler_linear_repeat, v.uv * _SpecularMap_ST.xy + _SpecularMap_ST.zw, 0) * _SpecularColor;

                if( payload.bounceIndex == 0) {
                    // setea el payload para el rayo que viene desde la camara para guardar la info en texturas (g-buffer)
                    payload.worldPos = float4(worldPosition, 0);
                    payload.worldNormal = float4( faceNormal, 0);
                    payload.albedo = albedoColor;

                    // esto deshabilita la acumulación con el frame anterior en el denoiser
                    // el valor de _InvalidatePrevFrame corresponde a cada objeto e indica si este se está moviendo o no
                    payload.invalidatePrevFrame = _InvalidatePrevFrame;

                    payload.hiddenObjectPath = 1;
                }
                else if (payload.hiddenObjectPath == 0){
                    // payload.color = 0;
                    payload.energy = 0;
                    initialEnergy = 0;
                }

                // calcula la probabilidad de que:
                // * para BRDF: el reflejo sea difuso o especular
                // * para BSDF: el reflejo es total (perfecto) o es transparencia
                float roulette = rand(payload);

                //used to blend BTDF and BRDF -> (p)BTDF + (1-p)BRDF
                // _SpecTrans is (p)

                // calculamos la probabilidad de que la interacción con la superficie sea de reflexión o reflexión y refracción
                float blender = rand(payload);

                RayDesc nextRay;
                nextRay.Origin = worldPosition;
                //do not set 'nextRay.Direction' just yet
                nextRay.TMin = 0.001f;
                nextRay.TMax = 1e20f;

                // queremos saber si la superficie solo refleja BRDF (bidirectional reflectance distribution function)
                // o refleja y transmite BSDF (bidirectional scattering distribution function)
                // https://en.wikipedia.org/wiki/Bidirectional_scattering_distribution_function
                //
                // _SpecTrans (Specular Transparency) es 0 para todos los materiales y de esa manera evitamos superficies
                // transparentes (¿nos está dando problemas con el denoiser?)
                //
                // cuando se utilizan transparencias la acumulación realizada por el denoiser requiere mayor cantidad de cálculos
                // para converger cuando ocurre un movimiento.
                // por ello es que se decidió evitar materiales transparentes en una primera etapa
                // Una alternativa es poder deshabilitar la acumulación que realiza el denoiser para los pixeles transparentes

                if (blender < 1 - _SpecTrans){ // BRDF
                    // la interacción con la superficie es solo de reflexión

                    float3 reflectionDir;

                    // se quiere saber si es un rebote difuso o especular, por lo que calculamos la proporción según el tipo de superficie
                    // que depende de que tan metálico sea el material
                    // Ver: Physically Based Shading at Disney ~> the metallic-ness (0 = dielectric, 1 = metallic).
                    //   dielectric ~> baja conductividad de la energía  ~> más difuso
                    //   metallic ~> alta conductividad  ~> más espejado
                    float diffuseRatio = 0.5 * (1.0 - _Metallic);
                    float specularRatio = 1 - diffuseRatio;

                    // Lo: outgoing light direction
                    float3 V = normalize(-1*WorldRayDirection());

                    if (roulette < diffuseRatio) {
                        // sample diffuse

                        //cosin sample
                        reflectionDir = SampleHemisphere(faceNormal, 1.0f, payload);
                    }
                    else{
                        //sample specular

                        float3 halfVec = ImportanceSampleGGX(float2(rand(payload), rand(payload)), faceNormal, V, _Roughness);
                        //isosceles
                        reflectionDir = 2.0 * dot(V, halfVec) * halfVec - V;
                    }

                        reflectionDir = normalize(reflectionDir);

                    /* calcular specularBRDF */
                    float3 L = reflectionDir;
                    float3 H = normalize(V + L);

                    float NdotL = abs(dot(faceNormal, L));
                    float NdotH = abs(dot(faceNormal, H));
                    float VdotH = abs(dot(V, H));

                    float NdotV = abs(dot(faceNormal, V));

                    float3 F0 = MIN_DIELECTRICS_F0;
                    F0 = lerp(F0 * specularColor, albedoColor, _Metallic);

                    float NDF = DistributionGGX(faceNormal, H, _Roughness);
                    float G = GeometrySmith(faceNormal, V, L, _Roughness);
                    float3 F = FresnelSchlick(max(dot(H, V), 0.0), F0);

                    float3 kS = F;
                    float3 kD = 1.0 - kS;
                    kD *= 1.0 - _Metallic;

                    float3 specularBrdf = SpecularBRDF(NDF, G, F, V, L, faceNormal);

                    //hemisphere sampling pdf
                    //pdf = 1 / (2 * PI)
                    //float speccualrPdf = 1 / (2 * PI);

                    //ImportanceSampleGGX pdf
                    //pdf = D * NoH / (4 * VoH)
                    float speccualrPdf = ImportanceSampleGGX_PDF(NDF, NdotH, VdotH);

                    //diffuse
                    //Lambert diffuse = diffuse / PI
                    float3 diffuseBrdf = DiffuseBRDF(albedoColor);

                    //cosin sample pdf = N dot L / PI
                    float diffusePdf = CosinSamplingPDF(NdotL);

                    float3 totalBrdf = (diffuseBrdf * kD + specularBrdf) * NdotL;
                    float totalPdf = diffuseRatio * diffusePdf + specularRatio * speccualrPdf;

                    // --> preparo el rayo recursivo
                    nextRay.Direction = reflectionDir;
                    if (totalPdf > 0.0)
                    {
                        payload.energy *= float4(totalBrdf / totalPdf,0);
                    }
                }
                else{
                    // [BTDF]
                    bool fromOutside = dot(WorldRayDirection(), faceNormal) < 0;
                    float3 N = fromOutside ? faceNormal : -faceNormal;

                    float etai = 1;
                    float etat = 1.55;

                    float3 V = normalize(-1*WorldRayDirection());
                    float3 H = ImportanceSampleGGX(float2(rand(payload), rand(payload)), N, V, _Roughness);


                    float3 F0 = MIN_DIELECTRICS_F0;
                    F0 = F0 * specularColor;
                    float3 F = FresnelSchlick(max(dot(H, V), 0.0), F0);


                    float kr = Calculatefresnel(WorldRayDirection(), faceNormal, 1.55);

                    float specularRatio = kr;
                    float refractionRatio = 1 - kr;

                    float3 L;

                    if (roulette <= specularRatio)
                    {
                        // --> preparo el rayo recursivo
                        L = reflect(WorldRayDirection(), H);
                        nextRay.Direction = L;
                    }
                    else
                    {
                        float eta = fromOutside ? etai / etat : etat / etai;
                        L = normalize(refract(WorldRayDirection(), H, eta));

                        // --> preparo el rayo recursivo
                        nextRay.Direction = L;

                        //L = N;
                        if (!fromOutside)
                        {
                                //since the BTDF is not reciprocal, we need to invert the direction of our vectors.
                            float3 temp = L;
                            L = V;
                            V = temp;

                            N = -N;
                            H = -H;
                        }
                    }

                    float NdotL = abs(dot(N, L));
                    // float NdotV = abs(dot(N, V)); //no se usa

                    float NdotH = abs(dot(N, H));
                    float VdotH = abs(dot(V, H));
                    // float LdotH = abs(dot(L, H)); //no se usa


                    float NDF = DistributionGGX(N, H, _Roughness);
                    float G = GeometrySmith(N, V, L, _Roughness);

                    //specualr

                    float3 specularBrdf = SpecularBRDF(NDF, G, F, V, L, N);

                    //ImportanceSampleGGX pdf
                    //pdf = D * NoH / (4 * VoH)
                    float speccualrPdf = ImportanceSampleGGX_PDF(NDF, NdotH, VdotH);

                    //refraction
                    float etaOut = etat;
                    float etaIn = etai;

                    float3 refractionBtdf = RefractionBTDF(NDF, G, F, V, L, N, H, etaIn, etaOut);
                    float refractionPdf = ImportanceSampleGGX_PDF(NDF, NdotH, VdotH);

                    //BSDF = BRDF + BTDF
                    float3 totalBrdf = (specularBrdf + refractionBtdf * /*TODO: verificar hit.transColor*/ specularColor) * NdotL;
                    float totalPdf = specularRatio * speccualrPdf + refractionRatio * refractionPdf;
                    if (totalPdf > 0.0)
                    {
                        payload.energy *= float4(totalBrdf / totalPdf, 0);
                    }
                }

                // return hit.emission;
                // payload.color += payload.energy * 0;// NOTE: no material has emission

                float cosNL = dot(Li, faceNormal);
                if( cosNL > 0 ){
                    payload.color += initialEnergy * cosNL * albedoColor * ShootShadowRay(worldPosition, Li)*g_sun_intensity;
                }

                if( payload.bounceIndex < g_max_recursion_depth) {
                    //RAYO RECURSIVO
                    payload.bounceIndex += 1;
                    uint missShaderIndex = 1;
                    TraceRay(g_SceneAccelStruct, RAY_FLAG_NONE, 0xFF, 0, 2, missShaderIndex, nextRay, payload);
                }

            }
            ENDHLSL
        }
    }
}