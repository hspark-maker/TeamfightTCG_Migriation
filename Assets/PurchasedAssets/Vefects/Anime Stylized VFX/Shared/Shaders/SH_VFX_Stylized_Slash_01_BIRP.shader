// Made with Amplify Shader Editor v1.9.7.1
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Vefects/SH_VFX_Stylized_Slash_01_BIRP"
{
	Properties
	{
		[Space(33)][Header(Main Texture)][Space(13)]_Texture("Texture", 2D) = "white" {}
		_TextureChannel("Texture Channel", Vector) = (0,1,0,0)
		_TextureRotation("Texture Rotation", Float) = 0
		_TexturePanSpeed("Texture Pan Speed", Vector) = (0,0,0,0)
		[Space(33)][Header(Distortion)][Space(13)]_DistortionMask("Distortion Mask", 2D) = "white" {}
		_DistortionMaskChannel("Distortion Mask Channel", Vector) = (0,1,0,0)
		_DistortionMaskRotation("Distortion Mask Rotation", Float) = 0
		_DistortionMaskPanSpeed("Distortion Mask Pan Speed", Vector) = (0,0,0,0)
		_DistortionIntensity("Distortion Intensity", Float) = 0
		[Space(33)][Header(Dissolve)][Space(13)]_DissolveMask("Dissolve Mask", 2D) = "white" {}
		_DissolveMaskChannel("Dissolve Mask Channel", Vector) = (0,1,0,0)
		_DissolveMaskRotation("Dissolve Mask Rotation", Float) = 0
		_DissolveMaskPanSpeed("Dissolve Mask Pan Speed", Vector) = (0,0,0,0)
		_DissolveMaskInvert("Dissolve Mask Invert", Range( 0 , 1)) = 0
		_DissolveOffset("Dissolve Offset", Float) = 0
		[Space(33)][Header(Properties)][Space(13)]_EmissionIntensity("Emission Intensity", Float) = 1
		_CoreColor("Core Color", Color) = (1,1,1,0)
		_DifferentCoreColor("Different Core Color", Float) = 0
		_CorePower("Core Power", Float) = 1
		_CoreIntensity("Core Intensity", Float) = 0
		_GlowIntensity("Glow Intensity", Float) = 1
		_AlphaBoldness("Alpha Boldness", Float) = 1
		[Toggle(_CUSTOMPANSWITCH_ON)] _CustomPanSwitch("CustomPanSwitch", Float) = 0
		[Toggle(_MESHVERTEXCOLOR_ON)] _MeshVertexColor("MeshVertexColor", Float) = 0
		[Toggle(_STEP_ON)] _Step("Step", Float) = 0
		_ValueStep("Value Step", Float) = 0
		_ValueStepAdd("Value Step Add", Float) = 0.1
		[Space(33)][Header(Depth Fade)][Space(13)][Toggle(_USEDEPTHFADE_ON)] _UseDepthFade("Use Depth Fade", Float) = 0
		_DepthFadeIntensity("Depth Fade Intensity", Float) = 0
		[Space(33)][Header(Cutout)][Space(13)]_Cutout("Cutout", 2D) = "white" {}
		_CutoutErosion("Cutout Erosion", Float) = 0
		_CutoutErosionSmoothness("Cutout Erosion Smoothness", Float) = 0.05
		_CutoutRotation("Cutout Rotation", Float) = 0
		_CutoutOffset("Cutout Offset", Vector) = (0,0,0,0)
		[Space(33)][Header(AR)][Space(13)]_Cull("Cull", Float) = 2
		_Src("Src", Float) = 5
		_Dst("Dst", Float) = 10
		_ZWrite("ZWrite", Float) = 0
		_ZTest("ZTest", Float) = 2
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] _texcoord2( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent"  "Queue" = "Transparent+0" "IsEmissive" = "true"  }
		Cull [_Cull]
		ZWrite [_ZWrite]
		ZTest [_ZTest]
		Blend [_Src] [_Dst]
		
		CGPROGRAM
		#include "UnityShaderVariables.cginc"
		#include "UnityCG.cginc"
		#pragma target 3.5
		#pragma shader_feature_local _USEDEPTHFADE_ON
		#pragma shader_feature_local _CUSTOMPANSWITCH_ON
		#pragma shader_feature_local _STEP_ON
		#pragma shader_feature_local _MESHVERTEXCOLOR_ON
		#define ASE_VERSION 19701
		#pragma surface surf Unlit keepalpha noshadow 
		#undef TRANSFORM_TEX
		#define TRANSFORM_TEX(tex,name) float4(tex.xy * name##_ST.xy + name##_ST.zw, tex.z, tex.w)
		struct Input
		{
			float4 vertexColor : COLOR;
			float4 uv_texcoord;
			float2 uv2_texcoord2;
			float4 screenPos;
		};

		uniform float _Cull;
		uniform float _Src;
		uniform float _Dst;
		uniform float _ZWrite;
		uniform float _ZTest;
		uniform float4 _CoreColor;
		uniform sampler2D _DissolveMask;
		uniform float4 _DissolveMask_ST;
		uniform float _DissolveMaskRotation;
		uniform float2 _DissolveMaskPanSpeed;
		uniform sampler2D _DistortionMask;
		uniform float4 _DistortionMask_ST;
		uniform float _DistortionMaskRotation;
		uniform float2 _DistortionMaskPanSpeed;
		uniform float4 _DistortionMaskChannel;
		uniform float _DistortionIntensity;
		uniform float4 _DissolveMaskChannel;
		uniform float _DissolveMaskInvert;
		uniform float _DissolveOffset;
		uniform sampler2D _Texture;
		uniform float4 _Texture_ST;
		uniform float _TextureRotation;
		uniform float2 _TexturePanSpeed;
		uniform float4 _TextureChannel;
		uniform float _CorePower;
		uniform float _CoreIntensity;
		uniform float _CutoutErosion;
		uniform float _CutoutErosionSmoothness;
		uniform sampler2D _Cutout;
		uniform float2 _CutoutOffset;
		uniform float _CutoutRotation;
		uniform float _DifferentCoreColor;
		uniform float _EmissionIntensity;
		uniform float _GlowIntensity;
		uniform float _AlphaBoldness;
		uniform float _ValueStep;
		uniform float _ValueStepAdd;
		UNITY_DECLARE_DEPTH_TEXTURE( _CameraDepthTexture );
		uniform float4 _CameraDepthTexture_TexelSize;
		uniform float _DepthFadeIntensity;

		inline half4 LightingUnlit( SurfaceOutput s, half3 lightDir, half atten )
		{
			return half4 ( 0, 0, 0, s.Alpha );
		}

		void surf( Input i , inout SurfaceOutput o )
		{
			float3 temp_output_157_0 = (i.vertexColor).rgb;
			float4 uvs_DissolveMask = i.uv_texcoord;
			uvs_DissolveMask.xy = i.uv_texcoord.xy * _DissolveMask_ST.xy + _DissolveMask_ST.zw;
			float cos112 = cos( radians( _DissolveMaskRotation ) );
			float sin112 = sin( radians( _DissolveMaskRotation ) );
			float2 rotator112 = mul( uvs_DissolveMask.xy - float2( 0.5,0.5 ) , float2x2( cos112 , -sin112 , sin112 , cos112 )) + float2( 0.5,0.5 );
			float2 temp_cast_2 = (0.0).xx;
			#ifdef _CUSTOMPANSWITCH_ON
				float2 staticSwitch85 = i.uv2_texcoord2;
			#else
				float2 staticSwitch85 = temp_cast_2;
			#endif
			float2 CustomUV89 = staticSwitch85;
			float4 uvs_DistortionMask = i.uv_texcoord;
			uvs_DistortionMask.xy = i.uv_texcoord.xy * _DistortionMask_ST.xy + _DistortionMask_ST.zw;
			float cos95 = cos( radians( _DistortionMaskRotation ) );
			float sin95 = sin( radians( _DistortionMaskRotation ) );
			float2 rotator95 = mul( uvs_DistortionMask.xy - float2( 0.5,0.5 ) , float2x2( cos95 , -sin95 , sin95 , cos95 )) + float2( 0.5,0.5 );
			float dotResult100 = dot( tex2D( _DistortionMask, ( rotator95 + uvs_DistortionMask.w + CustomUV89 + ( _Time.y * _DistortionMaskPanSpeed ) ) ) , _DistortionMaskChannel );
			float Disto107 = ( saturate( dotResult100 ) * _DistortionIntensity );
			float dotResult122 = dot( tex2D( _DissolveMask, ( rotator112 + uvs_DissolveMask.w + CustomUV89 + ( _Time.y * _DissolveMaskPanSpeed ) + Disto107 ) ) , _DissolveMaskChannel );
			float temp_output_126_0 = saturate( dotResult122 );
			float lerpResult138 = lerp( temp_output_126_0 , saturate( ( 1.0 - temp_output_126_0 ) ) , _DissolveMaskInvert);
			float4 uvs_Texture = i.uv_texcoord;
			uvs_Texture.xy = i.uv_texcoord.xy * _Texture_ST.xy + _Texture_ST.zw;
			float cos129 = cos( radians( _TextureRotation ) );
			float sin129 = sin( radians( _TextureRotation ) );
			float2 rotator129 = mul( uvs_Texture.xy - float2( 0.5,0.5 ) , float2x2( cos129 , -sin129 , sin129 , cos129 )) + float2( 0.5,0.5 );
			float dotResult140 = dot( tex2D( _Texture, ( rotator129 + ( _Time.y * _TexturePanSpeed ) + CustomUV89 + Disto107 ) ) , _TextureChannel );
			float temp_output_147_0 = ( ( saturate( lerpResult138 ) + i.uv_texcoord.z + _DissolveOffset ) * saturate( dotResult140 ) );
			float temp_output_261_0 = saturate( ( pow( temp_output_147_0 , _CorePower ) * _CoreIntensity ) );
			float2 temp_output_266_0 = ( i.uv_texcoord.xy + _CutoutOffset );
			float cos258 = cos( radians( _CutoutRotation ) );
			float sin258 = sin( radians( _CutoutRotation ) );
			float2 rotator258 = mul( temp_output_266_0 - float2( 0.5,0.5 ) , float2x2( cos258 , -sin258 , sin258 , cos258 )) + float2( 0.5,0.5 );
			float smoothstepResult252 = smoothstep( _CutoutErosion , ( _CutoutErosion + _CutoutErosionSmoothness ) , tex2D( _Cutout, rotator258 ).g);
			float cutout256 = smoothstepResult252;
			float4 lerpResult188 = lerp( float4( temp_output_157_0 , 0.0 ) , _CoreColor , saturate( ( temp_output_261_0 * cutout256 ) ));
			float4 lerpResult217 = lerp( float4( temp_output_157_0 , 0.0 ) , saturate( lerpResult188 ) , _DifferentCoreColor);
			float3 temp_cast_6 = (1.0).xxx;
			#ifdef _MESHVERTEXCOLOR_ON
				float3 staticSwitch159 = temp_output_157_0;
			#else
				float3 staticSwitch159 = temp_cast_6;
			#endif
			float3 temp_output_167_0 = saturate( ( ( i.vertexColor.a * saturate( ( saturate( ( temp_output_261_0 + saturate( ( temp_output_147_0 * _GlowIntensity ) ) ) ) * cutout256 ) ) * staticSwitch159 ) * _AlphaBoldness ) );
			float3 temp_cast_7 = (_ValueStep).xxx;
			float3 temp_cast_8 = (( _ValueStep + _ValueStepAdd )).xxx;
			float3 smoothstepResult170 = smoothstep( temp_cast_7 , temp_cast_8 , temp_output_167_0);
			#ifdef _STEP_ON
				float3 staticSwitch183 = saturate( smoothstepResult170 );
			#else
				float3 staticSwitch183 = temp_output_167_0;
			#endif
			float4 temp_output_234_0 = ( ( saturate( lerpResult217 ) * _EmissionIntensity ) * float4( staticSwitch183 , 0.0 ) );
			float4 ase_screenPos = float4( i.screenPos.xyz , i.screenPos.w + 0.00000000001 );
			float4 ase_screenPosNorm = ase_screenPos / ase_screenPos.w;
			ase_screenPosNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
			float screenDepth213 = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE( _CameraDepthTexture, ase_screenPosNorm.xy ));
			float distanceDepth213 = ( screenDepth213 - LinearEyeDepth( ase_screenPosNorm.z ) ) / ( _DepthFadeIntensity );
			float temp_output_236_0 = saturate( distanceDepth213 );
			#ifdef _USEDEPTHFADE_ON
				float4 staticSwitch238 = ( temp_output_234_0 * temp_output_236_0 );
			#else
				float4 staticSwitch238 = temp_output_234_0;
			#endif
			o.Emission = staticSwitch238.rgb;
			#ifdef _USEDEPTHFADE_ON
				float3 staticSwitch239 = ( staticSwitch183 * temp_output_236_0 );
			#else
				float3 staticSwitch239 = staticSwitch183;
			#endif
			o.Alpha = staticSwitch239.x;
		}

		ENDCG
	}
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=19701
Node;AmplifyShaderEditor.CommentaryNode;81;-5920,-512;Inherit;False;1052.771;508.8153;PanInCustomUV;4;89;85;84;83;PanInCustomUV;0,0,0,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;82;-6112,-1536;Inherit;False;3687.623;895.329;Distortion;18;107;106;102;103;100;98;99;97;96;95;94;93;92;91;90;88;87;86;Distortion;0,0,0,1;0;0
Node;AmplifyShaderEditor.RangedFloatNode;84;-5888,-384;Inherit;False;Constant;_Value0;Value0;26;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;83;-5888,-256;Inherit;False;1;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.StaticSwitch;85;-5504,-384;Inherit;False;Property;_CustomPanSwitch;CustomPanSwitch;24;0;Create;True;0;0;0;False;0;False;0;0;1;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;FLOAT2;0,0;False;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT2;0,0;False;6;FLOAT2;0,0;False;7;FLOAT2;0,0;False;8;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TexturePropertyNode;87;-6016,-1408;Inherit;True;Property;_DistortionMask;Distortion Mask;5;0;Create;True;0;0;0;False;3;Space(33);Header(Distortion);Space(13);False;None;None;False;white;Auto;Texture2D;-1;0;2;SAMPLER2D;0;SAMPLERSTATE;1
Node;AmplifyShaderEditor.RangedFloatNode;86;-5376,-1152;Inherit;False;Property;_DistortionMaskRotation;Distortion Mask Rotation;7;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;91;-4608,-896;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;92;-5376,-1280;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RegisterLocalVarNode;89;-5120,-384;Inherit;False;CustomUV;-1;True;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.Vector2Node;88;-4608,-816;Inherit;False;Property;_DistortionMaskPanSpeed;Distortion Mask Pan Speed;8;0;Create;True;0;0;0;False;0;False;0,0;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.RadiansOpNode;90;-5120,-1152;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;94;-4352,-896;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode;96;-4608,-1024;Inherit;False;89;CustomUV;1;0;OBJECT;;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;93;-5376,-1024;Inherit;False;0;-1;4;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RotatorNode;95;-5120,-1280;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0.5,0.5;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode;97;-4224,-1280;Inherit;False;4;4;0;FLOAT2;0,0;False;1;FLOAT;0;False;2;FLOAT2;0,0;False;3;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SamplerNode;98;-3968,-1408;Inherit;True;Property;_TextureSample5;Texture Sample 5;1;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.Vector4Node;99;-3584,-1280;Inherit;False;Property;_DistortionMaskChannel;Distortion Mask Channel;6;0;Create;True;0;0;0;False;0;False;0,1,0,0;0,1,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.DotProductOpNode;100;-3584,-1408;Inherit;False;2;0;COLOR;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;101;-7248,592;Inherit;False;2848.594;729.824;Dissolve Mask;24;145;143;142;141;138;136;135;133;126;122;120;119;117;116;115;114;113;112;111;110;109;108;105;104;Dissolve Mask;0,0,0,1;0;0
Node;AmplifyShaderEditor.SaturateNode;102;-3456,-1408;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;103;-3072,-1280;Inherit;False;Property;_DistortionIntensity;Distortion Intensity;9;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;104;-6928,832;Inherit;False;Property;_DissolveMaskRotation;Dissolve Mask Rotation;12;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;106;-3072,-1408;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TexturePropertyNode;105;-7200,640;Inherit;True;Property;_DissolveMask;Dissolve Mask;10;0;Create;True;0;0;0;False;3;Space(33);Header(Dissolve);Space(13);False;None;1684e6309dbb146408138a458ba608a2;False;white;Auto;Texture2D;-1;0;2;SAMPLER2D;0;SAMPLERSTATE;1
Node;AmplifyShaderEditor.Vector2Node;108;-6608,1152;Inherit;False;Property;_DissolveMaskPanSpeed;Dissolve Mask Pan Speed;13;0;Create;True;0;0;0;False;0;False;0,0;0.3,-0.1;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.TextureCoordinatesNode;109;-6800,704;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RadiansOpNode;110;-6672,864;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;111;-6592,1072;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;107;-2816,-1408;Inherit;False;Disto;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RotatorNode;112;-6496,720;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0.5,0.5;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;114;-6928,944;Inherit;False;0;-1;4;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;116;-6352,1072;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode;115;-6464,992;Inherit;False;107;Disto;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;113;-6496,896;Inherit;False;89;CustomUV;1;0;OBJECT;;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode;117;-6256,720;Inherit;False;5;5;0;FLOAT2;0,0;False;1;FLOAT;0;False;2;FLOAT2;0,0;False;3;FLOAT2;0,0;False;4;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.CommentaryNode;118;-5984,1392;Inherit;False;1885.642;658.4678;Main Texture;15;146;140;139;137;134;132;131;130;129;128;127;125;124;121;123;Main Texture;0,0,0,1;0;0
Node;AmplifyShaderEditor.Vector4Node;119;-6000,848;Inherit;False;Property;_DissolveMaskChannel;Dissolve Mask Channel;11;0;Create;True;0;0;0;False;0;False;0,1,0,0;0,1,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;120;-6064,640;Inherit;True;Property;_TextureSample3;Texture Sample 3;1;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.DotProductOpNode;122;-5728,704;Inherit;False;2;0;COLOR;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;123;-5824,1648;Inherit;False;Property;_TextureRotation;Texture Rotation;3;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TexturePropertyNode;121;-5936,1440;Inherit;True;Property;_Texture;Texture;1;0;Create;True;0;0;0;False;3;Space(33);Header(Main Texture);Space(13);False;None;00eca836046d25f4886f152c2223931c;False;white;Auto;Texture2D;-1;0;2;SAMPLER2D;0;SAMPLERSTATE;1
Node;AmplifyShaderEditor.SimpleTimeNode;124;-5376,1648;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;125;-5664,1520;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SaturateNode;126;-5504,704;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.Vector2Node;127;-5392,1712;Inherit;False;Property;_TexturePanSpeed;Texture Pan Speed;4;0;Create;True;0;0;0;False;0;False;0,0;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.RadiansOpNode;128;-5584,1648;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RotatorNode;129;-5408,1520;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0.5,0.5;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode;130;-5136,1872;Inherit;False;107;Disto;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;131;-5168,1648;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode;132;-5152,1792;Inherit;False;89;CustomUV;1;0;OBJECT;;False;1;FLOAT2;0
Node;AmplifyShaderEditor.OneMinusNode;133;-5376,800;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;134;-4944,1520;Inherit;False;4;4;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;2;FLOAT2;0,0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;135;-5328,896;Inherit;False;Property;_DissolveMaskInvert;Dissolve Mask Invert;14;0;Create;True;0;0;0;False;0;False;0;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;136;-5200,800;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.Vector4Node;137;-4688,1680;Inherit;False;Property;_TextureChannel;Texture Channel;2;0;Create;True;0;0;0;False;0;False;0,1,0,0;0,1,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.LerpOp;138;-5008,704;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;139;-4784,1456;Inherit;True;Property;_TextureSample1;Texture Sample 1;1;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.DotProductOpNode;140;-4464,1504;Inherit;False;2;0;COLOR;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;141;-4816,896;Inherit;False;0;-1;4;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SaturateNode;143;-4800,784;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;142;-4736,1088;Inherit;False;Property;_DissolveOffset;Dissolve Offset;15;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;257;-6706,2510;Inherit;False;1841.428;1076.581;Cutout;17;258;259;256;252;254;253;255;250;251;246;249;245;247;248;244;266;267;Cutout;0,0,0,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;144;-3968,752;Inherit;False;1533.878;534.1096;Adjustments;14;223;158;155;153;152;151;150;149;148;260;261;262;263;264;Adjustments;0,0,0,1;0;0
Node;AmplifyShaderEditor.SimpleAddOpNode;145;-4560,784;Inherit;False;3;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;146;-4304,1504;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;246;-6656,2944;Inherit;False;Property;_CutoutRotation;Cutout Rotation;34;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;248;-6656,2560;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector2Node;267;-6400,2768;Inherit;False;Property;_CutoutOffset;Cutout Offset;35;0;Create;True;0;0;0;False;0;False;0,0;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;147;-4144,800;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;148;-3856,896;Inherit;False;Property;_CorePower;Core Power;19;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RadiansOpNode;259;-6272,3328;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;266;-6400,2560;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PowerNode;149;-3712,800;Inherit;False;False;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;150;-3904,1120;Inherit;False;Property;_GlowIntensity;Glow Intensity;21;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;151;-3600,912;Inherit;False;Property;_CoreIntensity;Core Intensity;20;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;253;-5888,2816;Inherit;False;Property;_CutoutErosion;Cutout Erosion;32;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;254;-5888,2944;Inherit;False;Property;_CutoutErosionSmoothness;Cutout Erosion Smoothness;33;0;Create;True;0;0;0;False;0;False;0.05;0.05;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RotatorNode;258;-6272,3200;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0.5,0.5;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;152;-3712,1040;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;153;-3424,816;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;255;-5504,2816;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;244;-5888,2560;Inherit;True;Property;_Cutout;Cutout;31;0;Create;True;0;0;0;False;3;Space(33);Header(Cutout);Space(13);False;-1;57a59131c90e9a248aa75dede8b96462;57a59131c90e9a248aa75dede8b96462;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SmoothstepOpNode;252;-5504,2560;Inherit;True;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;261;-3200,816;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;263;-3456,1040;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;256;-5120,2560;Inherit;False;cutout;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;155;-3216,1024;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.VertexColorNode;154;-2688,-256;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SaturateNode;262;-3072,1024;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;265;-2974.576,934.7301;Inherit;False;256;cutout;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;156;-2176,768;Inherit;False;Constant;_Value1;Value1;28;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ComponentMaskNode;157;-2432,-256;Inherit;False;True;True;True;False;1;0;COLOR;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;264;-2928,1024;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;159;-1920,768;Inherit;False;Property;_MeshVertexColor;MeshVertexColor;25;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SaturateNode;158;-2720,1024;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;260;-2928,816;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;160;-1920,512;Inherit;False;3;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RangedFloatNode;161;-1536,640;Inherit;False;Property;_AlphaBoldness;Alpha Boldness;22;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;201;-1920,0;Inherit;False;Property;_CoreColor;Core Color;17;0;Create;True;0;0;0;False;0;False;1,1,1,0;1,1,1,0;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SaturateNode;223;-2720,816;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;165;-2176,1024;Inherit;False;Property;_ValueStep;Value Step;27;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;164;-2176,1152;Inherit;False;Property;_ValueStepAdd;Value Step Add;28;0;Create;True;0;0;0;False;0;False;0.1;0.1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;162;-1536,512;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.LerpOp;188;-1920,-128;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleAddOpNode;166;-1152,1152;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.2;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;167;-1280,512;Inherit;False;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SaturateNode;211;-1664,-128;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;212;-1280,-128;Inherit;False;Property;_DifferentCoreColor;Different Core Color;18;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;242;-816,1344;Inherit;False;324;418.95;Depth Fade;3;219;213;236;Depth Fade;0,0,0,1;0;0
Node;AmplifyShaderEditor.SmoothstepOpNode;170;-1152,1024;Inherit;False;3;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;1,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.LerpOp;217;-1280,-256;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;219;-768,1664;Inherit;False;Property;_DepthFadeIntensity;Depth Fade Intensity;30;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;174;-896,1024;Inherit;False;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SaturateNode;233;-1024,-256;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;193;-896,-128;Inherit;False;Property;_EmissionIntensity;Emission Intensity;16;0;Create;True;0;0;0;False;3;Space(33);Header(Properties);Space(13);False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DepthFade;213;-768,1536;Inherit;False;True;False;False;2;1;FLOAT3;0,0,0;False;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;183;-896,512;Inherit;False;Property;_Step;Step;26;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;224;-896,-256;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SaturateNode;236;-768,1408;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;234;-640,-256;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT3;0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.CommentaryNode;163;-2048,1920;Inherit;False;1535.601;892.1229;Push Particle toward camera direction (no more glow clipping in the ground) | 0=Disabled;9;240;168;169;171;177;173;175;221;172;;0,0,0,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;15;462,-50;Inherit;False;1252;163.3674;Ge Lush was here! <3;5;10;11;12;13;14;Ge Lush was here! <3;0.4902092,0.3301886,1,1;0;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;235;-448,416;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;228;-384,-128;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;10;512,0;Inherit;False;Property;_Cull;Cull;36;0;Create;True;0;0;0;True;3;Space(33);Header(AR);Space(13);False;2;2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;11;768,0;Inherit;False;Property;_Src;Src;37;0;Create;True;0;0;0;True;0;False;5;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;12;1024,0.7006989;Inherit;False;Property;_Dst;Dst;38;0;Create;True;0;0;0;True;0;False;10;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;13;1280,0;Inherit;False;Property;_ZWrite;ZWrite;39;0;Create;True;0;0;0;True;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;239;-384,128;Inherit;False;Property;_UseDepthFade1;Use Depth Fade;29;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Reference;238;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.WorldPosInputsNode;172;-1920,2048;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;221;-1280,2048;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;175;-1664,2048;Inherit;False;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.WorldSpaceCameraPos;173;-1920,2192;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;177;-1280,2176;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.01;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;171;-1280,2304;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;169;-1920,2432;Inherit;False;Property;_CameraDirPush;CameraDirPush;23;0;Create;True;0;0;0;False;0;False;0;-50;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;168;-1920,2560;Inherit;False;3;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RegisterLocalVarNode;240;-1024,2048;Inherit;False;cameraPush;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RangedFloatNode;14;1536,0;Inherit;False;Property;_ZTest;ZTest;40;0;Create;True;0;0;0;True;0;False;2;2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;238;-384,-256;Inherit;False;Property;_UseDepthFade;Use Depth Fade;29;0;Create;True;0;0;0;False;3;Space(33);Header(Depth Fade);Space(13);False;0;0;0;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;COLOR;0,0,0,0;False;0;COLOR;0,0,0,0;False;2;COLOR;0,0,0,0;False;3;COLOR;0,0,0,0;False;4;COLOR;0,0,0,0;False;5;COLOR;0,0,0,0;False;6;COLOR;0,0,0,0;False;7;COLOR;0,0,0,0;False;8;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.Vector2Node;247;-6656,2688;Inherit;False;Constant;_Vector0;Vector 0;44;0;Create;True;0;0;0;False;0;False;0.5,0.5;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.RotatorNode;245;-6272,2560;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;251;-6400,2944;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;360;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;250;-6272,2944;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PiNode;249;-6272,3072;Inherit;False;1;0;FLOAT;2;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;241;0,596;Inherit;False;240;cameraPush;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;269;0,-128;Float;False;True;-1;3;ASEMaterialInspector;0;0;Unlit;Vefects/SH_VFX_Stylized_Slash_01_BIRP;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;Back;0;True;_ZWrite;0;True;_ZTest;False;0;False;;0;False;;False;0;Custom;0.5;True;False;0;True;Transparent;;Transparent;All;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;False;2;5;True;_Src;10;True;_Dst;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;0;-1;-1;-1;0;False;0;0;True;_Cull;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;16;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;16;FLOAT4;0,0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
WireConnection;85;1;84;0
WireConnection;85;0;83;0
WireConnection;92;2;87;0
WireConnection;89;0;85;0
WireConnection;90;0;86;0
WireConnection;94;0;91;0
WireConnection;94;1;88;0
WireConnection;93;2;87;0
WireConnection;95;0;92;0
WireConnection;95;2;90;0
WireConnection;97;0;95;0
WireConnection;97;1;93;4
WireConnection;97;2;96;0
WireConnection;97;3;94;0
WireConnection;98;0;87;0
WireConnection;98;1;97;0
WireConnection;100;0;98;0
WireConnection;100;1;99;0
WireConnection;102;0;100;0
WireConnection;106;0;102;0
WireConnection;106;1;103;0
WireConnection;109;2;105;0
WireConnection;110;0;104;0
WireConnection;107;0;106;0
WireConnection;112;0;109;0
WireConnection;112;2;110;0
WireConnection;114;2;105;0
WireConnection;116;0;111;0
WireConnection;116;1;108;0
WireConnection;117;0;112;0
WireConnection;117;1;114;4
WireConnection;117;2;113;0
WireConnection;117;3;116;0
WireConnection;117;4;115;0
WireConnection;120;0;105;0
WireConnection;120;1;117;0
WireConnection;122;0;120;0
WireConnection;122;1;119;0
WireConnection;125;2;121;0
WireConnection;126;0;122;0
WireConnection;128;0;123;0
WireConnection;129;0;125;0
WireConnection;129;2;128;0
WireConnection;131;0;124;0
WireConnection;131;1;127;0
WireConnection;133;0;126;0
WireConnection;134;0;129;0
WireConnection;134;1;131;0
WireConnection;134;2;132;0
WireConnection;134;3;130;0
WireConnection;136;0;133;0
WireConnection;138;0;126;0
WireConnection;138;1;136;0
WireConnection;138;2;135;0
WireConnection;139;0;121;0
WireConnection;139;1;134;0
WireConnection;140;0;139;0
WireConnection;140;1;137;0
WireConnection;143;0;138;0
WireConnection;145;0;143;0
WireConnection;145;1;141;3
WireConnection;145;2;142;0
WireConnection;146;0;140;0
WireConnection;147;0;145;0
WireConnection;147;1;146;0
WireConnection;259;0;246;0
WireConnection;266;0;248;0
WireConnection;266;1;267;0
WireConnection;149;0;147;0
WireConnection;149;1;148;0
WireConnection;258;0;266;0
WireConnection;258;2;259;0
WireConnection;152;0;147;0
WireConnection;152;1;150;0
WireConnection;153;0;149;0
WireConnection;153;1;151;0
WireConnection;255;0;253;0
WireConnection;255;1;254;0
WireConnection;244;1;258;0
WireConnection;252;0;244;2
WireConnection;252;1;253;0
WireConnection;252;2;255;0
WireConnection;261;0;153;0
WireConnection;263;0;152;0
WireConnection;256;0;252;0
WireConnection;155;0;261;0
WireConnection;155;1;263;0
WireConnection;262;0;155;0
WireConnection;157;0;154;0
WireConnection;264;0;262;0
WireConnection;264;1;265;0
WireConnection;159;1;156;0
WireConnection;159;0;157;0
WireConnection;158;0;264;0
WireConnection;260;0;261;0
WireConnection;260;1;265;0
WireConnection;160;0;154;4
WireConnection;160;1;158;0
WireConnection;160;2;159;0
WireConnection;223;0;260;0
WireConnection;162;0;160;0
WireConnection;162;1;161;0
WireConnection;188;0;157;0
WireConnection;188;1;201;0
WireConnection;188;2;223;0
WireConnection;166;0;165;0
WireConnection;166;1;164;0
WireConnection;167;0;162;0
WireConnection;211;0;188;0
WireConnection;170;0;167;0
WireConnection;170;1;165;0
WireConnection;170;2;166;0
WireConnection;217;0;157;0
WireConnection;217;1;211;0
WireConnection;217;2;212;0
WireConnection;174;0;170;0
WireConnection;233;0;217;0
WireConnection;213;0;219;0
WireConnection;183;1;167;0
WireConnection;183;0;174;0
WireConnection;224;0;233;0
WireConnection;224;1;193;0
WireConnection;236;0;213;0
WireConnection;234;0;224;0
WireConnection;234;1;183;0
WireConnection;235;0;183;0
WireConnection;235;1;236;0
WireConnection;228;0;234;0
WireConnection;228;1;236;0
WireConnection;239;1;183;0
WireConnection;239;0;235;0
WireConnection;221;0;175;0
WireConnection;221;1;177;0
WireConnection;175;0;172;0
WireConnection;175;1;173;0
WireConnection;177;0;171;0
WireConnection;171;0;169;0
WireConnection;171;1;168;2
WireConnection;240;0;221;0
WireConnection;238;1;234;0
WireConnection;238;0;228;0
WireConnection;245;0;266;0
WireConnection;245;1;247;0
WireConnection;245;2;250;0
WireConnection;251;0;246;0
WireConnection;250;0;251;0
WireConnection;250;1;249;0
WireConnection;269;2;238;0
WireConnection;269;9;239;0
ASEEND*/
//CHKSM=F4FB86A339BDD6E1E7CEEE5CD77F76FE8E85928F