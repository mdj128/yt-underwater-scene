#ifndef PLANT_TERRAIN_DETAIL_PASSES_INCLUDED
#define PLANT_TERRAIN_DETAIL_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    float4 _Color;
    float _Cutoff;
    float _SwayAmplitude;
    float _SwayVertical;
    float _SwaySpeed;
    float _SwayHeightScale;
    float _SwayPhaseJitter;
    float _SwayNoiseStrength;
    float _SwayNoiseScale;
    float _GroundSink;
    float _GroundSlopeSink;
    float _GroundSinkHeightScale;
    float2 _WindDirection;
    float _WindStrength;
    float _WindGustStrength;
CBUFFER_END

struct Attributes
{
    float4  PositionOS  : POSITION;
    float2  UV0         : TEXCOORD0;
    float2  UV1         : TEXCOORD1;
    float3  NormalOS    : NORMAL;
    half4   Color       : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2  UV01            : TEXCOORD0;
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 1);
    half4   Color           : TEXCOORD2;
    half4   LightingFog     : TEXCOORD3;
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4  ShadowCoords    : TEXCOORD4;
#endif
    half4   NormalWS        : TEXCOORD5;
    float3  PositionWS      : TEXCOORD6;
#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion   : TEXCOORD7;
#endif
    float4  PositionCS      : SV_POSITION;

    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

float Hash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p += dot(p, p.yzx + 19.19);
    return frac(p.x * p.y * p.z * 93.53);
}

float3 GetInstanceOriginWS()
{
    return float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
}

float3 ApplyPlantSway(float3 positionOS, float instancePhase, float heightMask)
{
    float time = _Time.y * _SwaySpeed + instancePhase;
    float swayPrimary = sin(time);
    float swaySecondary = cos(time * 1.27 + instancePhase);
    float noise = sin(dot(positionOS.xz, float2(1.358, 1.972)) * _SwayNoiseScale + instancePhase * 1.7);
    float swayNoise = noise * _SwayNoiseStrength;

    float swayCombined = (swayPrimary + swayNoise) * _SwayAmplitude;
    float swayOrtho = (swaySecondary + swayNoise * 0.5) * _SwayAmplitude;
    float vertical = sin(time * 0.63 + instancePhase * 1.13) * _SwayVertical;

    float useDirectional = step(0.001f, _WindStrength);

    float3 originalPos = positionOS;
    originalPos.x += swayCombined * heightMask;
    originalPos.z += swayOrtho * heightMask;

    if (useDirectional > 0.5f)
    {
        float3 windDirWS = float3(_WindDirection.x, 0.0f, _WindDirection.y);
        float lenWS = dot(windDirWS, windDirWS);
        if (lenWS < 1e-4f)
        {
            windDirWS = float3(1.0f, 0.0f, 0.0f);
        }
        windDirWS = normalize(windDirWS);

        float3 windDirOS3 = mul((float3x3)unity_WorldToObject, windDirWS);
        float2 windDirOS = windDirOS3.xz;
        float lenOS = dot(windDirOS, windDirOS);
        if (lenOS < 1e-5f)
        {
            windDirOS = float2(1.0f, 0.0f);
        }
        windDirOS = normalize(windDirOS);

        float gust = sin(time * 0.45 + instancePhase * 0.7) * _WindGustStrength;
        float swayDirectional = (swayCombined + gust) * saturate(_WindStrength);
        float2 perpDir = float2(-windDirOS.y, windDirOS.x);

        positionOS.xz += windDirOS * swayDirectional * heightMask;
        positionOS.xz += perpDir * swayOrtho * heightMask * 0.35f;
    }
    else
    {
        positionOS = originalPos;
    }

    positionOS.y += vertical * heightMask;

    return positionOS;
}

void InitializeInputData(Varyings input, out InputData inputData)
{
    inputData = (InputData)0;

    inputData.positionCS = input.PositionCS;
    inputData.normalWS = half3(0, 1, 0);
    inputData.viewDirectionWS = half3(0, 0, 1);

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.ShadowCoords;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(input.PositionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif

    inputData.fogCoord = input.LightingFog.a;
    inputData.vertexLighting = input.LightingFog.rgb;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.PositionCS);
    inputData.positionWS = input.PositionWS;

#if !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(input.vertexSH,
        GetAbsolutePositionWS(inputData.positionWS),
        input.NormalWS.xyz,
        GetWorldSpaceNormalizeViewDir(inputData.positionWS),
        inputData.positionCS.xy,
        input.probeOcclusion,
        inputData.shadowMask);
#else
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, input.NormalWS.xyz);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
#endif

#if defined(DEBUG_DISPLAY)
    inputData.uv = input.UV01;
#if defined(USE_APV_PROBE_OCCLUSION)
    inputData.probeOcclusion = input.probeOcclusion;
#endif
#endif
}

half4 UniversalTerrainLit(half3 albedo, half alpha, InputData inputData)
{
#if defined(DEBUG_DISPLAY)
    half4 debugColor;
    if (CanDebugOverrideOutputColor(inputData, albedo, alpha, debugColor))
    {
        return debugColor;
    }
#endif

    half3 lighting = inputData.vertexLighting;
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    lighting *= MainLightRealtimeShadow(inputData.shadowCoord);
#endif

    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_GLOBAL_ILLUMINATION))
    {
        lighting += inputData.bakedGI;
    }

    half4 color = half4(albedo * lighting, alpha);
    return color;
}

Varyings TerrainLitVertexSway(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 instanceOrigin = GetInstanceOriginWS();
    float phaseNoise = Hash(instanceOrigin);
    float instancePhase = (phaseNoise - 0.5f) * _SwayPhaseJitter * 6.2831853f;
    half tipWeight = saturate(input.Color.a);

    float3 positionOS = input.PositionOS.xyz;

    float baseMask = saturate(1.0f - positionOS.y * _GroundSinkHeightScale);
    float radial = length(positionOS.xz);
    float sinkAmount = (_GroundSink + radial * _GroundSlopeSink) * baseMask;
    positionOS.y -= sinkAmount;

    float heightMask = max(tipWeight, saturate(positionOS.y * _SwayHeightScale));
    positionOS = ApplyPlantSway(positionOS, instancePhase, heightMask);

    output.UV01 = TRANSFORM_TEX(input.UV0, _MainTex);
    OUTPUT_LIGHTMAP_UV(input.UV1, unity_LightmapST, output.staticLightmapUV);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(positionOS);
    half3 colorRGB = input.Color.rgb * _Color.rgb;
    half colorA = _Color.a;
    output.Color = half4(colorRGB, colorA);
    output.PositionCS = vertexInput.positionCS;

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.ShadowCoords = GetShadowCoord(vertexInput);
#endif

    half3 NormalWS = TransformObjectToWorldNormal(input.NormalOS);
    OUTPUT_SH4(vertexInput.positionWS, NormalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);

    Light mainLight = GetMainLight();
    half3 attenuatedLightColor = mainLight.color * mainLight.distanceAttenuation;
    half3 diffuseColor = half3(0, 0, 0);

    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_MAIN_LIGHT))
    {
        diffuseColor += LightingLambert(attenuatedLightColor, mainLight.direction, NormalWS);
    }

#if (defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)) && !defined(USE_CLUSTER_LIGHT_LOOP)
    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_ADDITIONAL_LIGHTS))
    {
        int pixelLightCount = GetAdditionalLightsCount();
        for (int i = 0; i < pixelLightCount; ++i)
        {
            Light light = GetAdditionalLight(i, vertexInput.positionWS);
            half3 additionalColor = light.color * light.distanceAttenuation;
            diffuseColor += LightingLambert(additionalColor, light.direction, NormalWS);
        }
    }
#endif

    output.LightingFog.xyz = diffuseColor;
    output.LightingFog.w = ComputeFogFactor(output.PositionCS.z);

    output.NormalWS = half4(NormalWS, 0.0h);
    output.PositionWS = vertexInput.positionWS;

    return output;
}

half4 TerrainLitForwardFragmentSway(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    InputData inputData;
    InitializeInputData(input, inputData);
    SETUP_DEBUG_TEXTURE_DATA_FOR_TERRAIN(inputData);

    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.UV01);
    half4 color = half4(tex.rgb * input.Color.rgb, tex.a * input.Color.a);
    clip(color.a - _Cutoff);

    half4 lit = UniversalTerrainLit(color.rgb, color.a, inputData);
    lit.rgb = MixFog(lit.rgb, inputData.fogCoord);
    return lit;
}

float4 ShadowCasterFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.UV01);
    half alpha = tex.a * input.Color.a;
    clip(alpha - _Cutoff);
    return 0;
}

float4 DepthOnlyFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.UV01);
    half alpha = tex.a * input.Color.a;
    clip(alpha - _Cutoff);
    return 0;
}

#endif // PLANT_TERRAIN_DETAIL_PASSES_INCLUDED
