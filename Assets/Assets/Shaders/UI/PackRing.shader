// 팩 뒤 방사광 + 개봉 충격파 링을 한 그래픽에서 그린다.
//
// 두 역할을 한 셰이더에 담은 이유는 노드를 하나로 유지하기 위해서다 —
// 링만을 위한 UI 자식을 새로 두면 그 노드의 자리·크기·정렬을 또 따로 관리해야 하고,
// 평소에는 아무것도 안 그리는 빈 오브젝트가 계층에 남는다.
//
//   평소: 스프라이트 그대로(_RingStrength = 0). 회전·밝기는 PackShellRig가 쥔다.
//   개봉: 스프라이트 위에 링을 얹는다. 반지름이 자라며 팍 퍼진다.
//
// 링은 아트가 아니라 UV 거리 함수다 — 굵기·반지름·선명도를 런타임에 바꿔야 하는데
// 그걸 스프라이트로 만들면 단계마다 그림이 필요하다.
Shader "UI/PackRing"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        // 가산 합성으로 갈 수 있게 블렌드를 재질 단으로 뺐다 — 알파 합성은 아무리 밝혀도 배경 색에 눌린다.
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        // 최종 밝기 배수. 가산 합성에서 1을 넘기면 심지가 하얗게 탄다.
        _Intensity ("Intensity", Range(0,4)) = 1

        [Header(Ring)]
        // 링의 중심(그래픽 UV). 팩 입구에 맞춰야 "봉지에서 터져 나왔다"가 된다.
        _RingCenter ("Ring Center (uv)", Vector) = (0.5, 0.75, 0, 0)
        _RingRadius ("Ring Radius", Range(0,1.5)) = 0
        _RingWidth ("Ring Width", Range(0.001,0.5)) = 0.05
        // 굵기 안에서의 감쇠. 높을수록 가는 심지에 넓은 번짐이 붙는다.
        _RingSharp ("Ring Sharpness", Range(0.5,8)) = 2.5
        _RingStrength ("Ring Strength", Range(0,1)) = 0
        _RingColor ("Ring Color", Color) = (1,1,1,1)
        // 그래픽이 정사각이 아닐 때 원을 유지한다(가로/세로 비).
        _RingAspect ("Ring Aspect (w/h)", Float) = 1
        // 스프라이트(평소 방사광) 몫. 링과 알파를 나눠 쥔다 — 그래픽 알파 하나로 둘을 함께 조절하면
        // 링을 보이려고 알파를 올리는 순간 꺼 둔 방사광까지 같이 살아난다.
        _SpriteAmount ("Sprite Amount", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend [_SrcBlend] [_DstBlend]
        ColorMask [_ColorMask]

        Pass
        {
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            float4 _RingCenter, _RingColor;
            float _RingRadius, _RingWidth, _RingSharp, _RingStrength, _RingAspect, _SpriteAmount, _Intensity;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 col = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;
                col.a *= _SpriteAmount;   // 방사광 몫. 링은 아래에서 따로 더한다.

                if (_RingStrength > 0.0001)
                {
                    // 세로를 기준으로 가로를 보정한다 — 그래픽이 납작해도 원은 원으로 남는다.
                    float2 d = IN.texcoord - _RingCenter.xy;
                    d.x *= max(_RingAspect, 1e-4);
                    float r = length(d);

                    // 반지름에서 멀어질수록 옅어진다. 제곱해 심지를 가늘게, 번짐을 넓게.
                    float ring = saturate(1.0 - abs(r - _RingRadius) / max(_RingWidth, 1e-4));
                    ring = pow(ring, _RingSharp) * _RingStrength * _RingColor.a;

                    // 알파 합성이라 색은 섞고 알파는 더한다 — 링이 배경을 덮는 게 아니라 얹혀야 한다.
                    col.rgb = lerp(col.rgb, _RingColor.rgb, saturate(ring));
                    col.a = saturate(col.a + ring);
                }

                col.rgb *= _Intensity;   // 밝기 배수는 마지막에 — 그림과 절차적 링이 함께 탄다.

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
        ENDCG
        }
    }
}
