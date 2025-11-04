Shader "Plant/Terrain/PlantDetail"
{
    Properties
    {
        _MainTex("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _Color("Color Tint", Color) = (1,1,1,1)
        _Cutoff("Alpha Clip Threshold", Range(0.01, 1)) = 0.4

        _SwayAmplitude("Lateral Sway Amplitude", Range(0, 0.5)) = 0.12
        _SwayVertical("Vertical Bob Amplitude", Range(0, 0.2)) = 0.02
        _SwaySpeed("Primary Sway Speed", Range(0, 4)) = 0.8
        _SwayHeightScale("Height Influence", Range(0.1, 5)) = 1.4
        _SwayPhaseJitter("Instance Phase Jitter", Range(0, 4)) = 1.2
        _SwayNoiseStrength("Noise Strength", Range(0, 1)) = 0.35
        _SwayNoiseScale("Noise Scale", Range(0.01, 4)) = 0.6
        _GroundSink("Base Ground Sink", Range(0, 0.3)) = 0.04
        _GroundSlopeSink("Slope Sink Scale", Range(0, 0.6)) = 0.12
        _GroundSinkHeightScale("Sink Height Falloff", Range(0.1, 8)) = 3.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
        }
        LOD 200

        Cull Off
        ZWrite On
        AlphaToMask On

        Pass
        {
            Name "ForwardLit"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex TerrainLitVertexSway
            #pragma fragment TerrainLitForwardFragmentSway

            // Lighting and feature keywords to match TerrainDetailLit
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile _ DEBUG_DISPLAY

            #pragma multi_compile_instancing

            #include "PlantTerrainDetailPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex TerrainLitVertexSway
            #pragma fragment ShadowCasterFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_vertex _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_vertex _ _MAIN_LIGHT_SHADOWS

            #include "PlantTerrainDetailPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex TerrainLitVertexSway
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            #include "PlantTerrainDetailPasses.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
