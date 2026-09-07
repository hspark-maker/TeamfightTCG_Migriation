// 00_SG_MasterShader 의 UI(Canvas) 이식판.
// Shader Graph 의 Canvas 서브타겟은 Blend(One, OneMinusSrcAlpha) 고정 + 출력 직전 color.rgb *= color.a
// 라서 Additive 를 만들 수 없다. 그래서 UI 용은 HLSL 로 직접 쓴다.
//   _SrcBlend/_DstBlend 를 노출하므로 한 셰이더로 Alpha Blend 와 Additive 를 모두 낸다.
//     Additive    : SrcAlpha(5) / One(1)          <- 기본값
//     Alpha Blend : SrcAlpha(5) / OneMinusSrcAlpha(10)
// UI Mask(스텐실) 와 RectMask2D(클리핑) 를 모두 지원한다.
Shader "VFX/00_UI_MasterShader"
{
    Properties
    {
        [Header(Blend Mode)][Space(4)]
        // Additive    : SrcAlpha / One
        // Alpha Blend : SrcAlpha / OneMinusSrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5    // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1    // One = Additive

        [Space(12)][Header(Color)][Space(4)]
        _Color ("Color", Color) = (1,1,1,1)
        _STR ("STR", Float) = 1

        [Space][Header(01 MainTexture)][Space(4)]
        _01_MainTex ("01. MainTex", 2D) = "white" {}
        _01_MainUVSpeed ("01. MainUVSpeed (UT,VT,US,VS)", Vector) = (1,1,0,0)
        _01_MainTwist ("01. MainTwist", Float) = 0

        [Space][Header(02 Tile)][Space(4)]
        _02_TileTex ("02. TileTex", 2D) = "white" {}
        _02_Tile_UVSpeed ("02. Tile_UVSpeed (UT,VT,US,VS)", Vector) = (1,1,0,0)
        _02_Tile_Twist ("02. Tile_Twist", Float) = 0
        _02_Tile_STR ("02. Tile_STR", Float) = 0

        [Space][Header(03 Dissolve)][Space(4)]
        [Toggle(_03_USE_DISSOLVE)] _03_USE_DISSOLVE ("03. Use_Dissolve", Float) = 0
        _03_DissolveTex ("03. DissolveTex", 2D) = "white" {}
        _03_DissolveUVSpeed ("03. DissolveUVSpeed (UT,VT,US,VS)", Vector) = (1,1,0,0)
        _03_Dissolve_Smooth ("03. Dissolve_Smooth", Float) = 0
        _03_Dissolve_Amount ("03. Dissolve_Amount", Range(0,1)) = 0

        [Space][Header(04 Noise)][Space(4)]
        [Toggle(_04_USE_NOISE)] _04_USE_NOISE ("04. Use_Noise", Float) = 0
        _04_NoiseTex ("04. NoiseTex", 2D) = "white" {}
        _04_Noise_UVSpeed ("04. Noise_UVSpeed (UT,VT,US,VS)", Vector) = (1,1,0,0)
        _04_Noise_STR ("04. Noise_STR", Float) = 0

        [Space][Header(05 Alpha)][Space(4)]
        _05_AlphaTex ("05. AlphaTex", 2D) = "white" {}
        _05_Alpha_UV ("05. Alpha_UV (UT,VT)", Vector) = (1,1,0,0)
        _05_Alpha_STR ("05. Alpha_STR", Float) = 1

        [Space][Header(06 AlphaBlend)][Space(4)]
        [ToggleUI] _06_Use_LuminanceAlpha ("06. Use_LuminanceAlpha", Float) = 0

        // ---- UI 필수 (Mask / RectMask2D 가 자동으로 채운다) ----
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma shader_feature_local _03_USE_DISSOLVE
            #pragma shader_feature_local _04_USE_NOISE

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

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

            sampler2D _01_MainTex;      float4 _01_MainTex_ST;
            sampler2D _02_TileTex;      float4 _02_TileTex_ST;
            sampler2D _03_DissolveTex;  float4 _03_DissolveTex_ST;
            sampler2D _04_NoiseTex;     float4 _04_NoiseTex_ST;
            sampler2D _05_AlphaTex;     float4 _05_AlphaTex_ST;

            fixed4 _Color;
            float  _STR;
            float4 _01_MainUVSpeed;     float _01_MainTwist;
            float4 _02_Tile_UVSpeed;    float _02_Tile_Twist;      float _02_Tile_STR;
            float4 _03_DissolveUVSpeed; float _03_Dissolve_Smooth; float _03_Dissolve_Amount;
            float4 _04_Noise_UVSpeed;   float _04_Noise_STR;
            float4 _05_Alpha_UV;        float _05_Alpha_STR;
            float  _06_Use_LuminanceAlpha;
            float4 _ClipRect;
            float4 _01_MainTex_TexelSize;

            // Shader Graph 의 SG_UVAll 서브그래프와 동일한 식.
            // UI 에는 파티클 스트림이 없으므로 UOf/VOf 는 항상 0 이다.
            float2 SGUVAll(float2 uv, float4 spd, float twist, float2 noise)
            {
                float u = spd.x * uv.x + twist * (spd.y * uv.y) + spd.z * _Time.y;
                float v = spd.y * uv.y + spd.w * _Time.y;
                return float2(u, v) + noise;
            }

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);
                o.texcoord = v.texcoord;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;

                // 04. Noise — 메인 UV 를 밀어낸다. (tex-0.5) 로 중심을 잡아 제자리에서 일그러지게.
                float2 noise = float2(0, 0);
                #ifdef _04_USE_NOISE
                    float2 nUV = SGUVAll(uv, _04_Noise_UVSpeed, 0, float2(0, 0));
                    noise = (tex2D(_04_NoiseTex, TRANSFORM_TEX(nUV, _04_NoiseTex)).rg - 0.5) * _04_Noise_STR;
                #endif

                float2 mUV = SGUVAll(uv, _01_MainUVSpeed, _01_MainTwist, noise);
                float4 mainTex = tex2D(_01_MainTex, TRANSFORM_TEX(mUV, _01_MainTex));

                float2 tUV = SGUVAll(uv, _02_Tile_UVSpeed, _02_Tile_Twist, float2(0, 0));
                float4 tileTex = tex2D(_02_TileTex, TRANSFORM_TEX(tUV, _02_TileTex));

                float4 Tex = tileTex * mainTex * _02_Tile_STR + mainTex;

                // ---- 색: 알파에 의존하지 않는다 ----
                float3 rgb = Tex.rgb * IN.color.rgb * _Color.rgb * _STR;

                // ---- 알파 ----
                float alpha = IN.color.a * mainTex.a * _05_Alpha_STR;

                #ifdef _03_USE_DISSOLVE
                    float2 dUV = SGUVAll(uv, _03_DissolveUVSpeed, 0, float2(0, 0));
                    float dis = tex2D(_03_DissolveTex, TRANSFORM_TEX(dUV, _03_DissolveTex)).r;
                    alpha *= smoothstep(_03_Dissolve_Amount, _03_Dissolve_Amount + _03_Dissolve_Smooth, dis);
                #endif

                float4 aSpd = float4(_05_Alpha_UV.x, _05_Alpha_UV.y, 0, 0);
                float2 aUV = SGUVAll(uv, aSpd, 0, float2(0, 0));
                // 마스크는 R(검은 배경 텍스처) 과 A(투명 배경 텍스처) 를 함께 본다.
                float4 aTex = tex2D(_05_AlphaTex, TRANSFORM_TEX(aUV, _05_AlphaTex));
                alpha *= aTex.r * aTex.a;

                // 06. 알파 채널이 없는 텍스처를 위해 휘도를 알파로
                float lum = saturate(max(max(Tex.r, Tex.g), Tex.b));
                alpha *= lerp(1.0, lum, _06_Use_LuminanceAlpha);

                alpha = saturate(alpha);

                // ---- UI 클리핑 (RectMask2D) ----
                #ifdef UNITY_UI_CLIP_RECT
                    alpha *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                    clip(alpha - 0.001);
                #endif

                // Canvas 패스와 달리 rgb 에 알파를 곱하지 않는다.
                // 블렌드 인자(_SrcBlend=SrcAlpha)가 그 역할을 하므로,
                // _DstBlend 만 One/OneMinusSrcAlpha 로 바꾸면 Additive/AlphaBlend 가 전환된다.
                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
