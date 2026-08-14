Shader "Custom/MA_Trail"
{
    Properties
    {
        [HDR]_Color("Color", Color) = (1,1,1,1)
        _TEX("TEX", 2D) = "white" {}
        _T_Trail_01Copy("T_Trail_01 - Copy", 2D) = "white" {}
        _Panner("Panner", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "IsEmissive"="true" }
        Cull Off
        ZWrite Off
        Blend One One

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_TEX);
            SAMPLER(sampler_TEX);
            TEXTURE2D(_T_Trail_01Copy);
            SAMPLER(sampler_T_Trail_01Copy);
            float4 _Color;
            float4 _Panner;
            float4 _TEX_ST;
            float4 _T_Trail_01Copy_ST;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = mul(UNITY_MATRIX_MVP, IN.positionOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _TEX);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv_TEX = IN.uv;
                float2 panner = uv_TEX + _Panner.xy * _Time.y;
                float2 uv_T_Trail_01Copy = IN.uv * _T_Trail_01Copy_ST.xy + _T_Trail_01Copy_ST.zw;

                float temp_output = SAMPLE_TEXTURE2D(_TEX, sampler_TEX, panner).r * SAMPLE_TEXTURE2D(_T_Trail_01Copy, sampler_T_Trail_01Copy, uv_T_Trail_01Copy).r;

                float4 finalColor = (_Color * IN.color) * temp_output;
                finalColor.a = IN.color.a * temp_output;

                return finalColor;
            }
            ENDHLSL
        }
    }

    CustomEditor "ASEMaterialInspector"
}

