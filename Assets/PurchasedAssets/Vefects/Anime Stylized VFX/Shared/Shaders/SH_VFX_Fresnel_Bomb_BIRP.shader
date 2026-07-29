// Made with Amplify Shader Editor v1.9.7.1
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Vefects/SH_VFX_Fresnel_Bomb_BIRP"
{
	Properties
	{
		[Space(33)][Header(Dissolve)][Space(13)]_DissolveTexture("Dissolve Texture", 2D) = "white" {}
		_DissolveUVScale("Dissolve UV Scale", Vector) = (1,1,0,0)
		_DissolveUVSpeed("Dissolve UV Speed", Vector) = (0,0.3,0,0)
		_DissolveInvert("Dissolve Invert", Range( 0 , 1)) = 0
		_DissolveEro("Dissolve Ero", Float) = 0
		_ColorIn("Color In", Color) = (1,1,1,0)
		_ColorExt("Color Ext", Color) = (0,0.6901961,1,0)
		_FrBias1("Fr Bias", Float) = 0
		_FrScale1("Fr Scale", Float) = 1
		_FrColorScale("Fr Color Scale", Float) = 1
		_FrColorBias("Fr Color Bias", Float) = 0
		_FrPower1("Fr Power", Float) = 1
		_FrColorPower("Fr Color Power", Float) = 1
		_CorePower("Core Power", Float) = 1
		_CoreIntensity("Core Intensity", Float) = 0.6
		_GlowIntensity("Glow Intensity", Float) = 1
		_AddDiss("Add Diss", Float) = -0.690476
		_CoreColorDifferent("Core Color Different", Float) = 0.5
		_Brightness("Brightness", Float) = 1
		[Space(33)][Header(AR)][Space(13)]_Cull("Cull", Float) = 2
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
		CGINCLUDE
		#include "UnityShaderVariables.cginc"
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 3.5
		#define ASE_VERSION 19701
		#undef TRANSFORM_TEX
		#define TRANSFORM_TEX(tex,name) float4(tex.xy * name##_ST.xy + name##_ST.zw, tex.z, tex.w)
		struct Input
		{
			float4 vertexColor : COLOR;
			float3 worldPos;
			float3 worldNormal;
			float4 uv_texcoord;
		};

		uniform float _Cull;
		uniform float _Src;
		uniform float _Dst;
		uniform float _ZWrite;
		uniform float _ZTest;
		uniform float4 _ColorIn;
		uniform float4 _ColorExt;
		uniform float _FrColorBias;
		uniform float _FrColorScale;
		uniform float _FrColorPower;
		uniform float _CoreColorDifferent;
		uniform float _Brightness;
		uniform sampler2D _DissolveTexture;
		uniform float2 _DissolveUVSpeed;
		uniform float2 _DissolveUVScale;
		uniform float _DissolveInvert;
		uniform float _DissolveEro;
		uniform float _CorePower;
		uniform float _CoreIntensity;
		uniform float _GlowIntensity;
		uniform float _FrBias1;
		uniform float _FrScale1;
		uniform float _FrPower1;
		uniform float _AddDiss;

		inline half4 LightingUnlit( SurfaceOutput s, half3 lightDir, half atten )
		{
			return half4 ( 0, 0, 0, s.Alpha );
		}

		void surf( Input i , inout SurfaceOutput o )
		{
			float3 ase_worldPos = i.worldPos;
			float3 ase_viewVectorWS = ( _WorldSpaceCameraPos.xyz - ase_worldPos );
			float3 ase_viewDirWS = normalize( ase_viewVectorWS );
			float3 ase_worldNormal = i.worldNormal;
			float fresnelNdotV39 = dot( ase_worldNormal, ase_viewDirWS );
			float fresnelNode39 = ( _FrColorBias + _FrColorScale * pow( 1.0 - fresnelNdotV39, _FrColorPower ) );
			float temp_output_43_0 = saturate( fresnelNode39 );
			float4 lerpResult38 = lerp( _ColorIn , _ColorExt , temp_output_43_0);
			float4 lerpResult57 = lerp( i.vertexColor , lerpResult38 , _CoreColorDifferent);
			o.Emission = ( lerpResult57 * _Brightness ).rgb;
			float2 panner19 = ( 1.0 * _Time.y * _DissolveUVSpeed + ( i.uv_texcoord.xy * _DissolveUVScale ));
			float4 tex2DNode16 = tex2D( _DissolveTexture, panner19 );
			float4 lerpResult23 = lerp( tex2DNode16 , ( 1.0 - tex2DNode16 ) , _DissolveInvert);
			float4 temp_output_30_0 = ( saturate( lerpResult23 ) + ( i.uv_texcoord.z + _DissolveEro ) );
			float4 temp_cast_1 = (_CorePower).xxxx;
			float4 temp_output_52_0 = saturate( ( ( pow( temp_output_30_0 , temp_cast_1 ) * _CoreIntensity ) + ( temp_output_30_0 * _GlowIntensity ) ) );
			float fresnelNdotV62 = dot( ase_worldNormal, ase_viewDirWS );
			float fresnelNode62 = ( _FrBias1 + _FrScale1 * pow( 1.0 - fresnelNdotV62, _FrPower1 ) );
			float temp_output_78_0 = saturate( fresnelNode62 );
			float4 temp_cast_2 = (temp_output_78_0).xxxx;
			float4 temp_cast_3 = (temp_output_43_0).xxxx;
			o.Alpha = saturate( ( ( i.vertexColor.a * saturate( step( temp_output_52_0 , temp_cast_2 ) ) ) + step( ( ( i.vertexColor.a + temp_output_52_0 ) + _AddDiss ) , temp_cast_3 ) ) ).r;
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf Unlit keepalpha fullforwardshadows 

		ENDCG
		Pass
		{
			Name "ShadowCaster"
			Tags{ "LightMode" = "ShadowCaster" }
			ZWrite On
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.5
			#pragma multi_compile_shadowcaster
			#pragma multi_compile UNITY_PASS_SHADOWCASTER
			#pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2
			#include "HLSLSupport.cginc"
			#if ( SHADER_API_D3D11 || SHADER_API_GLCORE || SHADER_API_GLES || SHADER_API_GLES3 || SHADER_API_METAL || SHADER_API_VULKAN )
				#define CAN_SKIP_VPOS
			#endif
			#include "UnityCG.cginc"
			#include "Lighting.cginc"
			#include "UnityPBSLighting.cginc"
			sampler3D _DitherMaskLOD;
			struct v2f
			{
				V2F_SHADOW_CASTER;
				float4 customPack1 : TEXCOORD1;
				float3 worldPos : TEXCOORD2;
				float3 worldNormal : TEXCOORD3;
				half4 color : COLOR0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};
			v2f vert( appdata_full v )
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID( v );
				UNITY_INITIALIZE_OUTPUT( v2f, o );
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( o );
				UNITY_TRANSFER_INSTANCE_ID( v, o );
				Input customInputData;
				float3 worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				half3 worldNormal = UnityObjectToWorldNormal( v.normal );
				o.worldNormal = worldNormal;
				o.customPack1.xyzw = customInputData.uv_texcoord;
				o.customPack1.xyzw = v.texcoord;
				o.worldPos = worldPos;
				TRANSFER_SHADOW_CASTER_NORMALOFFSET( o )
				o.color = v.color;
				return o;
			}
			half4 frag( v2f IN
			#if !defined( CAN_SKIP_VPOS )
			, UNITY_VPOS_TYPE vpos : VPOS
			#endif
			) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( IN );
				Input surfIN;
				UNITY_INITIALIZE_OUTPUT( Input, surfIN );
				surfIN.uv_texcoord = IN.customPack1.xyzw;
				float3 worldPos = IN.worldPos;
				half3 worldViewDir = normalize( UnityWorldSpaceViewDir( worldPos ) );
				surfIN.worldPos = worldPos;
				surfIN.worldNormal = IN.worldNormal;
				surfIN.vertexColor = IN.color;
				SurfaceOutput o;
				UNITY_INITIALIZE_OUTPUT( SurfaceOutput, o )
				surf( surfIN, o );
				#if defined( CAN_SKIP_VPOS )
				float2 vpos = IN.pos;
				#endif
				half alphaRef = tex3D( _DitherMaskLOD, float3( vpos.xy * 0.25, o.Alpha * 0.9375 ) ).a;
				clip( alphaRef - 0.01 );
				SHADOW_CASTER_FRAGMENT( IN )
			}
			ENDCG
		}
	}
	Fallback "Diffuse"
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=19701
Node;AmplifyShaderEditor.TextureCoordinatesNode;17;-6272,0;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector2Node;20;-6272,128;Inherit;False;Property;_DissolveUVScale;Dissolve UV Scale;2;0;Create;True;0;0;0;False;0;False;1,1;1,1;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;18;-6016,0;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.Vector2Node;21;-5760,128;Inherit;False;Property;_DissolveUVSpeed;Dissolve UV Speed;3;0;Create;True;0;0;0;False;0;False;0,0.3;0,0.3;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.PannerNode;19;-5760,0;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SamplerNode;16;-5376,0;Inherit;True;Property;_DissolveTexture;Dissolve Texture;1;0;Create;True;0;0;0;False;3;Space(33);Header(Dissolve);Space(13);False;-1;8e9f6e86d964cef4db775abb71619b05;8e9f6e86d964cef4db775abb71619b05;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.OneMinusNode;26;-4992,128;Inherit;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;29;-4608,128;Inherit;False;Property;_DissolveInvert;Dissolve Invert;4;0;Create;True;0;0;0;False;0;False;0;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;23;-4608,0;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.TexCoordVertexDataNode;31;-4224,256;Inherit;False;0;4;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;33;-4224,512;Inherit;False;Property;_DissolveEro;Dissolve Ero;5;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;28;-4352,0;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleAddOpNode;32;-3968,256;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;30;-3968,0;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;49;-3712,-128;Inherit;False;Property;_CorePower;Core Power;14;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode;45;-3712,0;Inherit;False;False;2;0;COLOR;0,0,0,0;False;1;FLOAT;1;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;51;-3712,384;Inherit;False;Property;_GlowIntensity;Glow Intensity;16;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;50;-3456,-128;Inherit;False;Property;_CoreIntensity;Core Intensity;15;0;Create;True;0;0;0;False;0;False;0.6;0.6;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;46;-3456,0;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;47;-3712,256;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;63;-2176,128;Inherit;False;Property;_FrScale1;Fr Scale;9;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;64;-2176,256;Inherit;False;Property;_FrBias1;Fr Bias;8;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;65;-2176,384;Inherit;False;Property;_FrPower1;Fr Power;12;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;48;-3200,0;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.FresnelNode;62;-1920,128;Inherit;True;Standard;WorldNormal;ViewDir;False;False;5;0;FLOAT3;0,0,1;False;4;FLOAT3;0,0,0;False;1;FLOAT;0;False;2;FLOAT;1;False;3;FLOAT;5;False;1;FLOAT;0
Node;AmplifyShaderEditor.VertexColorNode;53;-3072,-640;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;41;-2944,1536;Inherit;False;Property;_FrColorBias;Fr Color Bias;11;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;40;-2944,1408;Inherit;False;Property;_FrColorScale;Fr Color Scale;10;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;42;-2944,1664;Inherit;False;Property;_FrColorPower;Fr Color Power;13;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;52;-2944,0;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SaturateNode;78;-1536,128;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;54;-2688,-128;Inherit;False;2;2;0;FLOAT;0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;56;-2816,384;Inherit;False;Property;_AddDiss;Add Diss;17;0;Create;True;0;0;0;False;0;False;-0.690476;-0.690476;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.FresnelNode;39;-2560,1408;Inherit;False;Standard;WorldNormal;ViewDir;False;False;5;0;FLOAT3;0,0,1;False;4;FLOAT3;0,0,0;False;1;FLOAT;0;False;2;FLOAT;1;False;3;FLOAT;5;False;1;FLOAT;0
Node;AmplifyShaderEditor.StepOpNode;74;-1280,0;Inherit;False;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SaturateNode;43;-2176,1408;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;55;-2560,256;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SaturateNode;76;-1152,0;Inherit;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.ColorNode;35;-2944,896;Inherit;False;Property;_ColorIn;Color In;6;0;Create;True;0;0;0;False;0;False;1,1,1,0;1,1,1,0;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ColorNode;37;-2944,1152;Inherit;False;Property;_ColorExt;Color Ext;7;0;Create;True;0;0;0;False;0;False;0,0.6901961,1,0;0,0.6897702,1,0;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.StepOpNode;44;-1920,896;Inherit;True;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;66;-896,-128;Inherit;False;2;2;0;FLOAT;0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.LerpOp;38;-2560,896;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;58;-2176,-512;Inherit;False;Property;_CoreColorDifferent;Core Color Different;18;0;Create;True;0;0;0;False;0;False;0.5;0.5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;15;462,-50;Inherit;False;1252;163.3674;Ge Lush was here! <3;5;10;11;12;13;14;Ge Lush was here! <3;0.4902092,0.3301886,1,1;0;0
Node;AmplifyShaderEditor.SimpleAddOpNode;79;-640,126.5639;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.LerpOp;57;-2176,-640;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;61;-1920,-384;Inherit;False;Property;_Brightness;Brightness;19;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;10;512,0;Inherit;False;Property;_Cull;Cull;21;0;Create;True;0;0;0;True;3;Space(33);Header(AR);Space(13);False;2;2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;11;768,0;Inherit;False;Property;_Src;Src;22;0;Create;True;0;0;0;True;0;False;5;5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;12;1024,0.7006989;Inherit;False;Property;_Dst;Dst;23;0;Create;True;0;0;0;True;0;False;10;10;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;13;1280,0;Inherit;False;Property;_ZWrite;ZWrite;24;0;Create;True;0;0;0;True;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;14;1536,0;Inherit;False;Property;_ZTest;ZTest;25;0;Create;True;0;0;0;True;0;False;2;2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;59;-1920,-640;Inherit;True;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.DepthFade;69;-1792,512;Inherit;False;True;False;True;2;1;FLOAT3;0,0,0;False;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;71;-1536,512;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;72;-1392,512;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;68;-2048,512;Inherit;False;Property;_DepthFade;Depth Fade;20;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;77;-1536,384;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;70;-1408,128;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;80;-384,128;Inherit;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;82;0,0;Float;False;True;-1;3;ASEMaterialInspector;0;0;Unlit;Vefects/SH_VFX_Fresnel_Bomb_BIRP;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;Back;0;True;_ZWrite;0;True;_ZTest;False;0;False;;0;False;;False;0;Custom;0.5;True;True;0;False;Transparent;;Transparent;All;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;True;1;5;True;_Src;10;True;_Dst;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;0;-1;-1;-1;0;False;0;0;True;_Cull;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;16;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;16;FLOAT4;0,0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
WireConnection;18;0;17;0
WireConnection;18;1;20;0
WireConnection;19;0;18;0
WireConnection;19;2;21;0
WireConnection;16;1;19;0
WireConnection;26;0;16;0
WireConnection;23;0;16;0
WireConnection;23;1;26;0
WireConnection;23;2;29;0
WireConnection;28;0;23;0
WireConnection;32;0;31;3
WireConnection;32;1;33;0
WireConnection;30;0;28;0
WireConnection;30;1;32;0
WireConnection;45;0;30;0
WireConnection;45;1;49;0
WireConnection;46;0;45;0
WireConnection;46;1;50;0
WireConnection;47;0;30;0
WireConnection;47;1;51;0
WireConnection;48;0;46;0
WireConnection;48;1;47;0
WireConnection;62;1;64;0
WireConnection;62;2;63;0
WireConnection;62;3;65;0
WireConnection;52;0;48;0
WireConnection;78;0;62;0
WireConnection;54;0;53;4
WireConnection;54;1;52;0
WireConnection;39;1;41;0
WireConnection;39;2;40;0
WireConnection;39;3;42;0
WireConnection;74;0;52;0
WireConnection;74;1;78;0
WireConnection;43;0;39;0
WireConnection;55;0;54;0
WireConnection;55;1;56;0
WireConnection;76;0;74;0
WireConnection;44;0;55;0
WireConnection;44;1;43;0
WireConnection;66;0;53;4
WireConnection;66;1;76;0
WireConnection;38;0;35;0
WireConnection;38;1;37;0
WireConnection;38;2;43;0
WireConnection;79;0;66;0
WireConnection;79;1;44;0
WireConnection;57;0;53;0
WireConnection;57;1;38;0
WireConnection;57;2;58;0
WireConnection;59;0;57;0
WireConnection;59;1;61;0
WireConnection;69;0;68;0
WireConnection;71;0;69;0
WireConnection;72;0;71;0
WireConnection;77;0;69;0
WireConnection;70;0;78;0
WireConnection;70;1;77;0
WireConnection;80;0;79;0
WireConnection;82;2;59;0
WireConnection;82;9;80;0
ASEEND*/
//CHKSM=F0BD626DB4498F074C7BD066F9FF8E28CB67E468