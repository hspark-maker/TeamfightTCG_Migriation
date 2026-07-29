// Made with Amplify Shader Editor v1.9.7.1
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Vefects/SH_VFX_Vefects_Distortion_Slash_01_BIRP"
{
	Properties
	{
		[Space(33)][Header(Cookie Cutter)][Space(13)]_CookieCutter("Cookie Cutter", 2D) = "white" {}
		_CutoutMaskSelector("Cutout Mask Selector", Vector) = (0,1,0,0)
		[Space(33)][Header(Cutout)][Space(13)]_Cutout("Cutout", 2D) = "white" {}
		_CutoutErosion("Cutout Erosion", Float) = 0
		_CutoutErosionSmoothness("Cutout Erosion Smoothness", Float) = 0.05
		_CutoutRotation("Cutout Rotation", Float) = 0
		_CutoutOffset("Cutout Offset", Vector) = (0,0,0,0)
		[Space(33)][Header(Distortion Noise)][Space(13)]_DistortionNoise("Distortion Noise", 2D) = "white" {}
		_DistortionNoiseSelector("Distortion Noise Selector", Vector) = (0,1,0,0)
		_DistUVS("Dist UV S", Vector) = (1,1,0,0)
		_DistUVP("Dist UV P", Vector) = (0,0,0,0)
		_DistortionLerp("Distortion Lerp", Float) = 1
		[Space(33)][Header(Distortion Dist Noise)][Space(13)]_DistortionDist("Distortion Dist", 2D) = "white" {}
		_DistortionDistSelector("Distortion Dist Selector", Vector) = (0,1,0,0)
		_DistDistUVS("Dist Dist UV S", Vector) = (1,1,0,0)
		_DistDistUVP("Dist Dist UV P", Vector) = (0,0,0,0)
		_DistortionDistLerp("Distortion Dist Lerp", Float) = 0.1
		[Space(33)][Header(Depth Fade)][Space(13)][Toggle(_USEDEPTHFADE_ON)] _UseDepthFade("Use Depth Fade", Float) = 0
		_DepthFadeIntensity("Depth Fade Intensity", Float) = 0
		[Space(33)][Header(AR)][Space(13)]_Cull("Cull", Float) = 0
		_Src("Src", Float) = 5
		_Dst("Dst", Float) = 10
		_ZWrite("ZWrite", Float) = 0
		_ZTest("ZTest", Float) = 2
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent"  "Queue" = "Transparent+0" "IsEmissive" = "true"  }
		Cull [_Cull]
		ZWrite [_ZWrite]
		ZTest [_ZTest]
		Blend [_Src] [_Dst]
		
		GrabPass{ }
		CGPROGRAM
		#include "UnityShaderVariables.cginc"
		#include "UnityCG.cginc"
		#pragma target 3.0
		#pragma shader_feature_local _USEDEPTHFADE_ON
		#define ASE_VERSION 19701
		#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
		#define ASE_DECLARE_SCREENSPACE_TEXTURE(tex) UNITY_DECLARE_SCREENSPACE_TEXTURE(tex);
		#else
		#define ASE_DECLARE_SCREENSPACE_TEXTURE(tex) UNITY_DECLARE_SCREENSPACE_TEXTURE(tex)
		#endif
		#pragma surface surf Unlit keepalpha noshadow 
		#undef TRANSFORM_TEX
		#define TRANSFORM_TEX(tex,name) float4(tex.xy * name##_ST.xy + name##_ST.zw, tex.z, tex.w)
		struct Input
		{
			float4 screenPos;
			float4 uv_texcoord;
			float4 vertexColor : COLOR;
		};

		uniform float _Cull;
		uniform float _Src;
		uniform float _Dst;
		uniform float _ZWrite;
		uniform float _ZTest;
		ASE_DECLARE_SCREENSPACE_TEXTURE( _GrabTexture )
		uniform sampler2D _DistortionNoise;
		uniform float2 _DistUVP;
		uniform float2 _DistUVS;
		uniform sampler2D _DistortionDist;
		uniform float2 _DistDistUVP;
		uniform float2 _DistDistUVS;
		uniform float4 _DistortionDistSelector;
		uniform float _DistortionDistLerp;
		uniform float4 _DistortionNoiseSelector;
		uniform float _CutoutErosion;
		uniform float _CutoutErosionSmoothness;
		uniform sampler2D _Cutout;
		uniform float2 _CutoutOffset;
		uniform float _CutoutRotation;
		uniform sampler2D _CookieCutter;
		uniform float4 _CookieCutter_ST;
		uniform float4 _CutoutMaskSelector;
		uniform float _DistortionLerp;
		UNITY_DECLARE_DEPTH_TEXTURE( _CameraDepthTexture );
		uniform float4 _CameraDepthTexture_TexelSize;
		uniform float _DepthFadeIntensity;


inline float4 ASE_ComputeGrabScreenPos( float4 pos )
{
	#if UNITY_UV_STARTS_AT_TOP
	float scale = -1.0;
	#else
	float scale = 1.0;
	#endif
	float4 o = pos;
	o.y = pos.w * 0.5f;
	o.y = ( pos.y - o.y ) * _ProjectionParams.x * scale + o.y;
	return o;
}


		inline half4 LightingUnlit( SurfaceOutput s, half3 lightDir, half atten )
		{
			return half4 ( 0, 0, 0, s.Alpha );
		}

		void surf( Input i , inout SurfaceOutput o )
		{
			float4 ase_screenPos = float4( i.screenPos.xyz , i.screenPos.w + 0.00000000001 );
			float4 ase_grabScreenPos = ASE_ComputeGrabScreenPos( ase_screenPos );
			float4 ase_grabScreenPosNorm = ase_grabScreenPos / ase_grabScreenPos.w;
			float2 appendResult18 = (float2(ase_grabScreenPosNorm.r , ase_grabScreenPosNorm.g));
			float2 panner35 = ( 1.0 * _Time.y * _DistUVP + ( i.uv_texcoord.xy * _DistUVS ));
			float2 panner50 = ( 1.0 * _Time.y * _DistDistUVP + ( i.uv_texcoord.xy * _DistDistUVS ));
			float dotResult55 = dot( tex2D( _DistortionDist, panner50 ) , _DistortionDistSelector );
			float2 temp_cast_1 = (saturate( dotResult55 )).xx;
			float2 lerpResult59 = lerp( float2( 0,0 ) , temp_cast_1 , _DistortionDistLerp);
			float dotResult37 = dot( tex2D( _DistortionNoise, ( panner35 + lerpResult59 ) ) , _DistortionNoiseSelector );
			float2 temp_output_81_0 = ( i.uv_texcoord.xy + _CutoutOffset );
			float cos68 = cos( radians( _CutoutRotation ) );
			float sin68 = sin( radians( _CutoutRotation ) );
			float2 rotator68 = mul( temp_output_81_0 - float2( 0.5,0.5 ) , float2x2( cos68 , -sin68 , sin68 , cos68 )) + float2( 0.5,0.5 );
			float smoothstepResult71 = smoothstep( _CutoutErosion , ( _CutoutErosion + _CutoutErosionSmoothness ) , tex2D( _Cutout, rotator68 ).g);
			float cutout72 = smoothstepResult71;
			float2 uv_CookieCutter = i.uv_texcoord * _CookieCutter_ST.xy + _CookieCutter_ST.zw;
			float dotResult41 = dot( tex2D( _CookieCutter, uv_CookieCutter ) , _CutoutMaskSelector );
			float2 temp_cast_4 = (saturate( ( saturate( dotResult37 ) * saturate( ( cutout72 * saturate( dotResult41 ) ) ) ) )).xx;
			float4 ase_screenPosNorm = ase_screenPos / ase_screenPos.w;
			ase_screenPosNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
			float screenDepth89 = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE( _CameraDepthTexture, ase_screenPosNorm.xy ));
			float distanceDepth89 = ( screenDepth89 - LinearEyeDepth( ase_screenPosNorm.z ) ) / ( _DepthFadeIntensity );
			#ifdef _USEDEPTHFADE_ON
				float staticSwitch92 = ( i.uv_texcoord.z * saturate( distanceDepth89 ) );
			#else
				float staticSwitch92 = i.uv_texcoord.z;
			#endif
			float2 lerpResult21 = lerp( float2( 0,0 ) , temp_cast_4 , ( _DistortionLerp * staticSwitch92 ));
			float4 screenColor12 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_GrabTexture,( appendResult18 + lerpResult21 ));
			o.Emission = screenColor12.rgb;
			o.Alpha = i.vertexColor.a;
		}

		ENDCG
	}
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=19701
Node;AmplifyShaderEditor.TextureCoordinatesNode;48;-4608,640;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector2Node;52;-4352,512;Inherit;False;Property;_DistDistUVS;Dist Dist UV S;15;0;Create;True;0;0;0;False;0;False;1,1;1,1;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.CommentaryNode;62;-3376,1536;Inherit;False;1841.428;1076.581;Cutout;17;77;76;75;74;73;72;71;70;69;68;67;66;65;64;63;81;84;Cutout;0,0,0,1;0;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;49;-4352,640;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.Vector2Node;51;-4096,511;Inherit;False;Property;_DistDistUVP;Dist Dist UV P;16;0;Create;True;0;0;0;False;0;False;0,0;-0.1,-0.2;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.PannerNode;50;-4096,640;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;63;-3328,1968;Inherit;False;Property;_CutoutRotation;Cutout Rotation;6;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;64;-3328,1584;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector2Node;84;-3072,1792;Inherit;False;Property;_CutoutOffset;Cutout Offset;7;0;Create;True;0;0;0;False;0;False;0,0;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.Vector4Node;54;-3712,896;Inherit;False;Property;_DistortionDistSelector;Distortion Dist Selector;14;0;Create;True;0;0;0;False;0;False;0,1,0,0;1,0,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;53;-3840,640;Inherit;True;Property;_DistortionDist;Distortion Dist;13;0;Create;True;0;0;0;False;3;Space(33);Header(Distortion Dist Noise);Space(13);False;-1;0b5dfa661a91adf4683f6d112b1c0a2d;0b5dfa661a91adf4683f6d112b1c0a2d;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RadiansOpNode;65;-2944,2352;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;81;-3072,1584;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DotProductOpNode;55;-3456,640;Inherit;True;2;0;COLOR;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;32;-3456,256;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector2Node;34;-3200,128;Inherit;False;Property;_DistUVS;Dist UV S;10;0;Create;True;0;0;0;False;0;False;1,1;2,2;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.RangedFloatNode;66;-2560,1840;Inherit;False;Property;_CutoutErosion;Cutout Erosion;4;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;67;-2560,1968;Inherit;False;Property;_CutoutErosionSmoothness;Cutout Erosion Smoothness;5;0;Create;True;0;0;0;False;0;False;0.05;0.05;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RotatorNode;68;-2944,2224;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0.5,0.5;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SaturateNode;56;-3200,640;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;33;-3200,256;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.Vector2Node;36;-2944,128;Inherit;False;Property;_DistUVP;Dist UV P;11;0;Create;True;0;0;0;False;0;False;0,0;0.1,-0.1;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.Vector2Node;57;-2944,512;Inherit;False;Constant;_Vector1;Vector 0;3;0;Create;True;0;0;0;False;0;False;0,0;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.RangedFloatNode;60;-2816,768;Inherit;False;Property;_DistortionDistLerp;Distortion Dist Lerp;17;0;Create;True;0;0;0;False;0;False;0.1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;69;-2176,1840;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;70;-2560,1584;Inherit;True;Property;_Cutout;Cutout;3;0;Create;True;0;0;0;False;3;Space(33);Header(Cutout);Space(13);False;-1;57a59131c90e9a248aa75dede8b96462;57a59131c90e9a248aa75dede8b96462;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.Vector4Node;39;-2304,1280;Inherit;False;Property;_CutoutMaskSelector;Cutout Mask Selector;2;0;Create;True;0;0;0;False;0;False;0,1,0,0;0,1,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.PannerNode;35;-2944,256;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.LerpOp;59;-2688,512;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SmoothstepOpNode;71;-2176,1584;Inherit;True;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;14;-2432,1024;Inherit;True;Property;_CookieCutter;Cookie Cutter;1;0;Create;True;0;0;0;False;3;Space(33);Header(Cookie Cutter);Space(13);False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.CommentaryNode;87;-1120,1568;Inherit;False;324;418.95;Depth Fade;3;90;89;88;Depth Fade;0,0,0,1;0;0
Node;AmplifyShaderEditor.DotProductOpNode;41;-2048,1024;Inherit;True;2;0;COLOR;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;58;-2688,256;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;72;-1792,1584;Inherit;False;cutout;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;88;-1072,1888;Inherit;False;Property;_DepthFadeIntensity;Depth Fade Intensity;19;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;10;-2432,256;Inherit;True;Property;_DistortionNoise;Distortion Noise;8;0;Create;True;0;0;0;False;3;Space(33);Header(Distortion Noise);Space(13);False;-1;0b5dfa661a91adf4683f6d112b1c0a2d;0b5dfa661a91adf4683f6d112b1c0a2d;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.Vector4Node;38;-2304,512;Inherit;False;Property;_DistortionNoiseSelector;Distortion Noise Selector;9;0;Create;True;0;0;0;False;0;False;0,1,0,0;0,0,1,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SaturateNode;79;-1792,1280;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;80;-1792,1152;Inherit;False;72;cutout;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.DepthFade;89;-1072,1760;Inherit;False;True;False;False;2;1;FLOAT3;0,0,0;False;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.DotProductOpNode;37;-2048,256;Inherit;True;2;0;COLOR;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;78;-1536,1152;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TexCoordVertexDataNode;47;-1152,1024;Inherit;False;0;4;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SaturateNode;90;-1072,1632;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;13;-1792,256;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;40;-1792,1024;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;91;-896,1408;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;43;-1664,384;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;45;-1152,512;Inherit;False;Property;_DistortionLerp;Distortion Lerp;12;0;Create;True;0;0;0;False;0;False;1;0.2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;92;-896,1280;Inherit;False;Property;_UseDepthFade;Use Depth Fade;18;0;Create;True;0;0;0;False;3;Space(33);Header(Depth Fade);Space(13);False;0;0;0;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GrabScreenPosition;17;-1792,0;Inherit;False;0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SaturateNode;44;-1408,384;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.Vector2Node;20;-1408,128;Inherit;False;Constant;_Vector0;Vector 0;3;0;Create;True;0;0;0;False;0;False;0,0;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;61;-896,512;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;18;-1536,0;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.LerpOp;21;-1152,256;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.CommentaryNode;31;590,-50;Inherit;False;1238;166;AR;5;22;23;24;29;30;AR;0,0,0,1;0;0
Node;AmplifyShaderEditor.SimpleAddOpNode;19;-1152,0;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;22;640,0;Inherit;False;Property;_Cull;Cull;20;0;Create;True;0;0;0;True;3;Space(33);Header(AR);Space(13);False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;23;896,0;Inherit;False;Property;_Src;Src;21;0;Create;True;0;0;0;True;0;False;5;5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;24;1152,0;Inherit;False;Property;_Dst;Dst;22;0;Create;True;0;0;0;True;0;False;10;10;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;29;1408,0;Inherit;False;Property;_ZWrite;ZWrite;23;0;Create;True;0;0;0;True;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;30;1664,0;Inherit;False;Property;_ZTest;ZTest;24;0;Create;True;0;0;0;True;0;False;2;2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ScreenColorNode;12;-896,0;Inherit;False;Global;_GrabScreen0;Grab Screen 0;2;0;Create;True;0;0;0;False;0;False;Object;-1;False;False;False;False;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.VertexColorNode;46;-896,768;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector2Node;73;-3328,1712;Inherit;False;Constant;_Vector2;Vector 0;44;0;Create;True;0;0;0;False;0;False;0.5,0.5;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.SimpleDivideOpNode;75;-3072,1968;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;360;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;76;-2944,1968;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PiNode;77;-2944,2096;Inherit;False;1;0;FLOAT;2;False;1;FLOAT;0
Node;AmplifyShaderEditor.RotatorNode;74;-2816,1584;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;86;0,0;Float;False;True;-1;2;ASEMaterialInspector;0;0;Unlit;Vefects/SH_VFX_Vefects_Distortion_Slash_01_BIRP;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;Back;0;True;_ZWrite;0;True;_ZTest;False;0;False;;0;False;;False;0;Custom;0.5;True;False;0;True;Transparent;;Transparent;All;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;False;2;5;True;_Src;10;True;_Dst;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;0;-1;-1;-1;0;False;0;0;True;_Cull;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;16;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;16;FLOAT4;0,0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
WireConnection;49;0;48;0
WireConnection;49;1;52;0
WireConnection;50;0;49;0
WireConnection;50;2;51;0
WireConnection;53;1;50;0
WireConnection;65;0;63;0
WireConnection;81;0;64;0
WireConnection;81;1;84;0
WireConnection;55;0;53;0
WireConnection;55;1;54;0
WireConnection;68;0;81;0
WireConnection;68;2;65;0
WireConnection;56;0;55;0
WireConnection;33;0;32;0
WireConnection;33;1;34;0
WireConnection;69;0;66;0
WireConnection;69;1;67;0
WireConnection;70;1;68;0
WireConnection;35;0;33;0
WireConnection;35;2;36;0
WireConnection;59;0;57;0
WireConnection;59;1;56;0
WireConnection;59;2;60;0
WireConnection;71;0;70;2
WireConnection;71;1;66;0
WireConnection;71;2;69;0
WireConnection;41;0;14;0
WireConnection;41;1;39;0
WireConnection;58;0;35;0
WireConnection;58;1;59;0
WireConnection;72;0;71;0
WireConnection;10;1;58;0
WireConnection;79;0;41;0
WireConnection;89;0;88;0
WireConnection;37;0;10;0
WireConnection;37;1;38;0
WireConnection;78;0;80;0
WireConnection;78;1;79;0
WireConnection;90;0;89;0
WireConnection;13;0;37;0
WireConnection;40;0;78;0
WireConnection;91;0;47;3
WireConnection;91;1;90;0
WireConnection;43;0;13;0
WireConnection;43;1;40;0
WireConnection;92;1;47;3
WireConnection;92;0;91;0
WireConnection;44;0;43;0
WireConnection;61;0;45;0
WireConnection;61;1;92;0
WireConnection;18;0;17;1
WireConnection;18;1;17;2
WireConnection;21;0;20;0
WireConnection;21;1;44;0
WireConnection;21;2;61;0
WireConnection;19;0;18;0
WireConnection;19;1;21;0
WireConnection;12;0;19;0
WireConnection;75;0;63;0
WireConnection;76;0;75;0
WireConnection;76;1;77;0
WireConnection;74;0;81;0
WireConnection;74;1;73;0
WireConnection;74;2;76;0
WireConnection;86;2;12;0
WireConnection;86;9;46;4
ASEEND*/
//CHKSM=48FE7CC1B9EE51343748F0BA1741E5DE0787F1DA