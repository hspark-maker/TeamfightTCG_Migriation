// 롱프레스 정보창 뒤 화면을 흐리게 만드는 풀스크린 블러.
// ScreenBlurFeature가 저해상도 사본에 가로(0번)→세로(1번) 두 번 그린다 — 분리형 가우시안이라
// 한 패스에서 NxN을 다 도는 것보다 샘플 수가 훨씬 적다(모바일 예산).
//
// 세로 패스는 알파 블렌드로 **화면 원본 위에** 그린다 — 블러 강도를 0→1로 올릴 때
// 별도 lerp 텍스처 없이 합성 알파만 올리면 되므로 텍스처 하나를 아낀다.
Shader "Hidden/TCG/ScreenBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        #pragma target 2.0
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float4 _BlurStep;      // xy = 한 탭당 UV 간격(가로/세로). 저해상도 RT 크기 기준으로 CPU가 넣는다.
        float  _BlurStrength;  // 0~1. 세로 패스가 원본 위에 얹히는 정도.

        // 5샘플 9탭 가우시안. 오프셋이 정수가 아닌 이유는 bilinear 필터가 이웃 두 텍셀을
        // 한 번에 섞어주기 때문 — 샘플 5번으로 9탭 결과를 얻는 표준 가중치다.
        half4 SampleBlur(float2 _uv, float2 _step)
        {
            const float k_off1 = 1.3846153846;
            const float k_off2 = 3.2307692308;

            half4 t_sum  = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, _uv, 0) * 0.2270270270;
            t_sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, _uv + _step * k_off1, 0) * 0.3162162162;
            t_sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, _uv - _step * k_off1, 0) * 0.3162162162;
            t_sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, _uv + _step * k_off2, 0) * 0.0702702703;
            t_sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, _uv - _step * k_off2, 0) * 0.0702702703;
            return t_sum;
        }
        ENDHLSL

        Pass
        {
            Name "ScreenBlurHorizontal"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings _input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(_input);
                half4 t_color = SampleBlur(_input.texcoord, float2(_BlurStep.x, 0));
                t_color.a = 1;
                return t_color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ScreenBlurVertical"
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings _input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(_input);
                half4 t_color = SampleBlur(_input.texcoord, float2(0, _BlurStep.y));
                t_color.a = _BlurStrength;   // 합성 알파 = 블러가 차오른 정도
                return t_color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
