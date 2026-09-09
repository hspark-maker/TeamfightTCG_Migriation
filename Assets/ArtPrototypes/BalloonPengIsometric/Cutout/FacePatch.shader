Shader "BalloonPeng/FacePatch"
{
    Properties
    {
        _BaseMap ("Expression Atlas", 2D) = "white" {}
        _PatchPixels ("Patch Size In Source Pixels", Vector) = (48, 48, 0, 0)
        _FeatherPixels ("Edge Feather In Source Pixels", Float) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "FacePatch"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _PatchPixels;
                float _FeatherPixels;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 atlasUV : TEXCOORD0;
                float2 patchUV : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 atlasUV : TEXCOORD0;
                float2 patchUV : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.atlasUV = input.atlasUV;
                output.patchUV = input.patchUV;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.atlasUV);
                float2 edgePixels = min(input.patchUV, 1 - input.patchUV) * _PatchPixels.xy;
                float feather = smoothstep(0, max(_FeatherPixels, 0.001), min(edgePixels.x, edgePixels.y));
                color.a *= feather;
                return color;
            }
            ENDHLSL
        }
    }
}
