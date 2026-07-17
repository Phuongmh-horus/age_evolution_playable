// Made with Amplify Shader Editor v1.9.6.3
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Piloto Studio/UberFX"
{
	Properties
	{
		[Enum(UnityEngine.Rendering.BlendMode)]_SourceBlendRGB("Blend Mode", Float) = 10
		[Enum(UnityEngine.Rendering.CullMode)]_Culling("Culling", Float) = 0
		_MainTex("Main Texture", 2D) = "white" {}
		_MainTextureChannel("Main Texture Channel", Vector) = (1,1,1,0)
		_MainAlphaChannel("Main Alpha Channel", Vector) = (0,0,0,1)
		_MainTexturePanning("Main Texture Panning ", Vector) = (0,0,0,0)
		_Desaturate("Desaturate? ", Range( 0 , 1)) = 0
		[Toggle(_USESOFTALPHA_ON)] _UseSoftAlpha("Use Soft Particles?", Float) = 0
		_SoftFadeFactor("Soft Fade Factor", Range( 0.1 , 1)) = 0.1
		[Toggle(_USEALPHAOVERRIDE_ON)] _UseAlphaOverride("Use Alpha Override", Float) = 0
		_AlphaOverride("Alpha Override", 2D) = "white" {}
		_AlphaOverrideChannel("Alpha Override Channel", Vector) = (0,0,0,1)
		_AlphaOverridePanning("Alpha Override Panning", Vector) = (0,0,0,0)
		_DetailNoise("Detail Noise", 2D) = "white" {}
		_DetailNoisePanning("Detail Noise Panning", Vector) = (0,0,0,0)
		_DetailDistortionChannel("Detail Distortion Channel", Vector) = (0,0,0,0)
		_DistortionIntensity("Distortion Intensity", Range( 0 , 3)) = 2
		_DetailMultiplyChannel("Detail Multiply Channel", Vector) = (0,0,0,0)
		_MultiplyNoiseDesaturation("Multiply Noise Desaturation", Range( 0 , 1)) = 1
		_DetailAdditiveChannel("Detail Additive Channel", Vector) = (0,0,0,0)
		_DetailDisolveChannel("Detail Disolve Channel", Vector) = (1,0,0,0)
		_DetailVertexOffsetChannel("Detail Vertex Offset Channel", Vector) = (1,0,0,0)
		[Toggle(_USERAMP_ON)] _UseRamp("Use Color Ramping?", Float) = 0
		[HDR]_WhiteColor("Highs", Color) = (1,0.8950032,0,0)
		_MiddlePointPos("Middle Point Position", Range( -1 , 0.99)) = 0.5
		[HDR]_MidColor("Middles", Color) = (1,0.4447915,0,0)
		_MiddlePointPos1("Middle Point Position 2", Range( -1 , 0.99)) = 0.5
		[HDR]_LastColor("Lows", Color) = (1,0,0,0)
		[Toggle(_USEUVOFFSET_ON)] _UseUVOffset("Use UV Offset", Float) = 0
		[Toggle(_FRESNEL_ON)] _Fresnel("Fresnel", Float) = 0
		_FresnelPower("Fresnel Power", Float) = 1
		_FresnelScale("Fresnel Scale", Float) = 1
		[HDR]_FresnelColor("Fresnel Color", Color) = (1,1,1,1)
		[Toggle(_DISABLEEROSION_ON)] _DisableErosion("Disable Erosion", Float) = 0
		[Toggle(_USEPIXELATION_ON)] _UsePixelation("Use Pixelation", Float) = 0
		_Resolution("Resolution", Vector) = (64,64,0,0)

	}
	
	SubShader
	{
		
		
		Tags { "Queue"="Transparent" "RenderType"="Transparent" }
	LOD 100

		CGINCLUDE
		#pragma target 3.0
		ENDCG
		Blend SrcAlpha [_SourceBlendRGB]
		AlphaToMask Off
		Cull [_Culling]
		ColorMask RGBA
		ZWrite Off
		ZTest LEqual
		Offset 0 , 0
		
		
		
		Pass
		{
			Name "Unlit"

			CGPROGRAM

			

			#ifndef UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX
			//only defining to not throw compilation error over Unity 5.5
			#define UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input)
			#endif
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#include "UnityCG.cginc"
			#include "UnityShaderVariables.cginc"
			#define ASE_NEEDS_FRAG_COLOR
			#define ASE_NEEDS_FRAG_WORLD_POSITION
			#pragma shader_feature_local _FRESNEL_ON
			#pragma shader_feature_local _USERAMP_ON
			#pragma shader_feature_local _USEPIXELATION_ON
			#pragma shader_feature_local _USEUVOFFSET_ON
			#pragma shader_feature_local _DISABLEEROSION_ON
			#pragma shader_feature_local _USESOFTALPHA_ON
			#pragma shader_feature_local _USEALPHAOVERRIDE_ON


			struct appdata
			{
				float4 vertex : POSITION;
				float4 color : COLOR;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord : TEXCOORD0;
				float3 ase_normal : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			
			struct v2f
			{
				float4 vertex : SV_POSITION;
				#ifdef ASE_NEEDS_FRAG_WORLD_POSITION
				float3 worldPos : TEXCOORD0;
				#endif
				float4 ase_color : COLOR;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_texcoord4 : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			uniform float _Culling;
			uniform float _SourceBlendRGB;
			uniform sampler2D _DetailNoise;
			uniform float2 _DetailNoisePanning;
			uniform float4 _DetailNoise_ST;
			uniform float4 _DetailVertexOffsetChannel;
			uniform sampler2D _MainTex;
			uniform float2 _MainTexturePanning;
			uniform float4 _MainTex_ST;
			uniform float2 _Resolution;
			uniform float4 _DetailDistortionChannel;
			uniform float _DistortionIntensity;
			uniform float4 _MainTextureChannel;
			uniform float _Desaturate;
			uniform float4 _DetailMultiplyChannel;
			uniform float _MultiplyNoiseDesaturation;
			uniform float4 _DetailAdditiveChannel;
			uniform float4 _LastColor;
			uniform float4 _MidColor;
			uniform float _MiddlePointPos;
			uniform float _MiddlePointPos1;
			uniform float4 _WhiteColor;
			uniform float4 _DetailDisolveChannel;
			uniform sampler2D _AlphaOverride;
			uniform float2 _AlphaOverridePanning;
			uniform float4 _AlphaOverride_ST;
			uniform float4 _AlphaOverrideChannel;
			uniform float _UseAlphaOverride;
			uniform float4 _MainAlphaChannel;
			UNITY_DECLARE_DEPTH_TEXTURE( _CameraDepthTexture );
			uniform float4 _CameraDepthTexture_TexelSize;
			uniform float _SoftFadeFactor;
			uniform float4 _FresnelColor;
			uniform float _FresnelScale;
			uniform float _FresnelPower;

			
			v2f vert ( appdata v )
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				UNITY_TRANSFER_INSTANCE_ID(v, o);

				float4 texCoord429 = v.ase_texcoord1;
				texCoord429.xy = v.ase_texcoord1.xy * float2( 1,1 ) + float2( 0,0 );
				float2 uv_DetailNoise = v.ase_texcoord * _DetailNoise_ST.xy + _DetailNoise_ST.zw;
				float2 panner80 = ( 1.0 * _Time.y * _DetailNoisePanning + uv_DetailNoise);
				float4 tex2DNode79 = tex2Dlod( _DetailNoise, float4( panner80, 0, 0.0) );
				float4 break17_g214 = tex2DNode79;
				float4 appendResult18_g214 = (float4(break17_g214.x , break17_g214.y , break17_g214.z , break17_g214.w));
				float4 clampResult19_g214 = clamp( ( appendResult18_g214 * _DetailVertexOffsetChannel ) , float4( 0,0,0,0 ) , float4( 1,1,1,1 ) );
				float4 break2_g214 = clampResult19_g214;
				float clampResult20_g214 = clamp( ( break2_g214.x + break2_g214.y + break2_g214.z + break2_g214.w ) , 0.0 , 1.0 );
				float VertexOffset434 = clampResult20_g214;
				
				float4 ase_clipPos = UnityObjectToClipPos(v.vertex);
				float4 screenPos = ComputeScreenPos(ase_clipPos);
				o.ase_texcoord3 = screenPos;
				float3 ase_worldNormal = UnityObjectToWorldNormal(v.ase_normal);
				o.ase_texcoord4.xyz = ase_worldNormal;
				
				o.ase_color = v.color;
				o.ase_texcoord1 = v.ase_texcoord;
				o.ase_texcoord2 = v.ase_texcoord1;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord4.w = 0;
				float3 vertexValue = float3(0, 0, 0);
				#if ASE_ABSOLUTE_VERTEX_POS
				vertexValue = v.vertex.xyz;
				#endif
				vertexValue = ( ( texCoord429.z * VertexOffset434 ) * v.ase_normal );
				#if ASE_ABSOLUTE_VERTEX_POS
				v.vertex.xyz = vertexValue;
				#else
				v.vertex.xyz += vertexValue;
				#endif
				o.vertex = UnityObjectToClipPos(v.vertex);

				#ifdef ASE_NEEDS_FRAG_WORLD_POSITION
				o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
				#endif
				return o;
			}
			
			fixed4 frag (v2f i ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
				fixed4 finalColor;
				#ifdef ASE_NEEDS_FRAG_WORLD_POSITION
				float3 WorldPosition = i.worldPos;
				#endif
				float2 uv_MainTex = i.ase_texcoord1.xy * _MainTex_ST.xy + _MainTex_ST.zw;
				float2 break455 = uv_MainTex;
				float2 appendResult454 = (float2(( break455.x - ( fmod( break455.x , ( 1.0 / _Resolution ).x ) - ( ( 1.0 / _Resolution ).x * 0.5 ) ) ) , ( break455.y - ( fmod( break455.y , 0.0 ) - ( 0.0 * 0.5 ) ) )));
				#ifdef _USEPIXELATION_ON
				float2 staticSwitch440 = appendResult454;
				#else
				float2 staticSwitch440 = uv_MainTex;
				#endif
				float2 uv_DetailNoise = i.ase_texcoord1.xy * _DetailNoise_ST.xy + _DetailNoise_ST.zw;
				float2 panner80 = ( 1.0 * _Time.y * _DetailNoisePanning + uv_DetailNoise);
				float4 tex2DNode79 = tex2D( _DetailNoise, panner80 );
				float4 break17_g202 = tex2DNode79;
				float4 appendResult18_g202 = (float4(break17_g202.x , break17_g202.y , break17_g202.z , break17_g202.w));
				float4 clampResult19_g202 = clamp( ( appendResult18_g202 * _DetailDistortionChannel ) , float4( 0,0,0,0 ) , float4( 1,1,1,1 ) );
				float4 break2_g202 = clampResult19_g202;
				float clampResult20_g202 = clamp( ( break2_g202.x + break2_g202.y + break2_g202.z + break2_g202.w ) , 0.0 , 1.0 );
				float DistortionNoise90 = clampResult20_g202;
				float temp_output_284_0 = ( DistortionNoise90 * _DistortionIntensity );
				float2 temp_cast_1 = (temp_output_284_0).xx;
				float4 texCoord397 = i.ase_texcoord2;
				texCoord397.xy = i.ase_texcoord2.xy * float2( 1,1 ) + float2( 0,0 );
				float2 appendResult400 = (float2(texCoord397.x , texCoord397.y));
				#ifdef _USEUVOFFSET_ON
				float2 staticSwitch402 = ( temp_output_284_0 + appendResult400 );
				#else
				float2 staticSwitch402 = temp_cast_1;
				#endif
				float2 UVModifiers204 = staticSwitch402;
				float2 panner22 = ( 1.0 * _Time.y * _MainTexturePanning + ( staticSwitch440 + UVModifiers204 ));
				float4 tex2DNode6 = tex2D( _MainTex, panner22 );
				float4 break376 = tex2DNode6;
				float4 break379 = _MainTextureChannel;
				float4 appendResult375 = (float4(( break376.r * break379.x ) , ( break376.g * break379.y ) , ( break376.b * break379.z ) , ( break376.a * break379.w )));
				float4 MainTexInfo25 = appendResult375;
				float3 desaturateInitialColor166 = MainTexInfo25.xyz;
				float desaturateDot166 = dot( desaturateInitialColor166, float3( 0.299, 0.587, 0.114 ));
				float3 desaturateVar166 = lerp( desaturateInitialColor166, desaturateDot166.xxx, _Desaturate );
				float4 break364 = ( _DetailMultiplyChannel * tex2DNode79 );
				float4 appendResult365 = (float4(break364.x , break364.y , break364.z , break364.w));
				float3 desaturateInitialColor362 = appendResult365.xyz;
				float desaturateDot362 = dot( desaturateInitialColor362, float3( 0.299, 0.587, 0.114 ));
				float3 desaturateVar362 = lerp( desaturateInitialColor362, desaturateDot362.xxx, _MultiplyNoiseDesaturation );
				float3 temp_cast_5 = (1.0).xxx;
				float3 temp_cast_6 = (1.0).xxx;
				float3 ifLocalVar106 = 0;
				if( ( _DetailMultiplyChannel.x + _DetailMultiplyChannel.y + _DetailMultiplyChannel.z + _DetailMultiplyChannel.w ) <= 0.0 )
				ifLocalVar106 = temp_cast_6;
				else
				ifLocalVar106 = desaturateVar362;
				float3 MultiplyNoise92 = ifLocalVar106;
				float4 break156 = ( _DetailAdditiveChannel * tex2DNode79 );
				float4 appendResult155 = (float4(break156.x , break156.y , break156.z , break156.w));
				float3 desaturateInitialColor191 = appendResult155.xyz;
				float desaturateDot191 = dot( desaturateInitialColor191, float3( 0.299, 0.587, 0.114 ));
				float3 desaturateVar191 = lerp( desaturateInitialColor191, desaturateDot191.xxx, 1.0 );
				float3 AdditiveNoise91 = desaturateVar191;
				float3 PreRamp210 = desaturateVar166;
				float3 temp_cast_10 = (_MiddlePointPos).xxx;
				float3 clampResult218 = clamp( ( PreRamp210 - temp_cast_10 ) , float3( 0,0,0 ) , float3( 1,1,1 ) );
				float temp_output_215_0 = ( 1.0 - _MiddlePointPos );
				float3 temp_cast_11 = (temp_output_215_0).xxx;
				float3 temp_output_219_0 = (float3( 0,0,0 ) + (clampResult218 - float3( 0,0,0 )) * (float3( 1,1,1 ) - float3( 0,0,0 )) / (temp_cast_11 - float3( 0,0,0 )));
				float3 temp_cast_12 = (_MiddlePointPos1).xxx;
				float3 temp_cast_13 = (temp_output_215_0).xxx;
				float4 lerpResult220 = lerp( _LastColor , _MidColor , float4( (float3( 0,0,0 ) + (( PreRamp210 * ( temp_output_219_0 - temp_cast_12 ) ) - float3( 0,0,0 )) * (float3( 1,1,1 ) - float3( 0,0,0 )) / (temp_cast_13 - float3( 0,0,0 ))) , 0.0 ));
				float3 temp_cast_15 = (temp_output_215_0).xxx;
				float4 lerpResult225 = lerp( _MidColor , _WhiteColor , float4( temp_output_219_0 , 0.0 ));
				float4 lerpResult226 = lerp( lerpResult220 , lerpResult225 , float4( PreRamp210 , 0.0 ));
				float4 break230 = lerpResult226;
				float4 appendResult231 = (float4(break230.r , break230.g , break230.b , PreRamp210.x));
				float4 PostRamp232 = appendResult231;
				#ifdef _USERAMP_ON
				float4 staticSwitch236 = PostRamp232;
				#else
				float4 staticSwitch236 = float4( ( ( desaturateVar166 * MultiplyNoise92 ) + AdditiveNoise91 ) , 0.0 );
				#endif
				float4 texCoord71 = i.ase_texcoord1;
				texCoord71.xy = i.ase_texcoord1.xy * float2( 1,1 ) + float2( 0,0 );
				float4 temp_output_39_0 = ( i.ase_color * staticSwitch236 * ( texCoord71.z + 1.0 ) );
				float4 texCoord258 = i.ase_texcoord1;
				texCoord258.xy = i.ase_texcoord1.xy * float2( 1,1 ) + float2( 0,0 );
				float2 _Vector0 = float2(-0.25,1);
				float temp_output_414_0 = (_Vector0.x + (( texCoord258.w + -1.0 ) - 0.0) * (_Vector0.y - _Vector0.x) / (1.0 - 0.0));
				float4 break17_g211 = tex2DNode79;
				float4 appendResult18_g211 = (float4(break17_g211.x , break17_g211.y , break17_g211.z , break17_g211.w));
				float4 clampResult19_g211 = clamp( ( appendResult18_g211 * _DetailDisolveChannel ) , float4( 0,0,0,0 ) , float4( 1,1,1,1 ) );
				float4 break2_g211 = clampResult19_g211;
				float clampResult20_g211 = clamp( ( break2_g211.x + break2_g211.y + break2_g211.z + break2_g211.w ) , 0.0 , 1.0 );
				float DisolveNoise275 = clampResult20_g211;
				float smoothstepResult416 = smoothstep( temp_output_414_0 , ( temp_output_414_0 + 0.25 ) , DisolveNoise275);
				#ifdef _DISABLEEROSION_ON
				float staticSwitch417 = 1.0;
				#else
				float staticSwitch417 = saturate( smoothstepResult416 );
				#endif
				float2 uv_AlphaOverride = i.ase_texcoord1.xy * _AlphaOverride_ST.xy + _AlphaOverride_ST.zw;
				float2 panner44 = ( 1.0 * _Time.y * _AlphaOverridePanning + uv_AlphaOverride);
				float4 break2_g205 = ( tex2D( _AlphaOverride, panner44 ) * _AlphaOverrideChannel );
				float AlphaOverride49 = saturate( ( break2_g205.x + break2_g205.y + break2_g205.z + break2_g205.w ) );
				// Alpha Override fix:
				// Use the material float directly, so SetFloat("_UseAlphaOverride", 1) works
				// even if the shader keyword _USEALPHAOVERRIDE_ON was not enabled by script/editor.
				float alphaOverrideEnabled313 = step( 0.5, _UseAlphaOverride );
				float2 panner33 = ( 1.0 * _Time.y * _MainTexturePanning + ( UVModifiers204 + staticSwitch440 ));
				float4 break2_g210 = ( tex2D( _MainTex, panner33 ) * _MainAlphaChannel );
				float MainAlpha30 = saturate( ( break2_g210.x + break2_g210.y + break2_g210.z + break2_g210.w ) );
				// OFF: use MainAlpha. ON: replace MainAlpha with AlphaOverride texture mask.
				float temp_output_55_0 = lerp( MainAlpha30, AlphaOverride49, alphaOverrideEnabled313 );
				float4 screenPos = i.ase_texcoord3;
				float4 ase_screenPosNorm = screenPos / screenPos.w;
				ase_screenPosNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
				float screenDepth199 = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE( _CameraDepthTexture, ase_screenPosNorm.xy ));
				float distanceDepth199 = abs( ( screenDepth199 - LinearEyeDepth( ase_screenPosNorm.z ) ) / ( _SoftFadeFactor ) );
				#ifdef _USESOFTALPHA_ON
				float staticSwitch198 = ( temp_output_55_0 * saturate( distanceDepth199 ) );
				#else
				float staticSwitch198 = temp_output_55_0;
				#endif
				float temp_output_396_0 = ( ( staticSwitch417 * staticSwitch198 ) * i.ase_color.a );
				float3 ase_worldViewDir = UnityWorldSpaceViewDir(WorldPosition);
				ase_worldViewDir = normalize(ase_worldViewDir);
				float3 ase_worldNormal = i.ase_texcoord4.xyz;
				float fresnelNdotV406 = dot( ase_worldNormal, ase_worldViewDir );
				float fresnelNode406 = ( 0.0 + _FresnelScale * pow( max( 1.0 - fresnelNdotV406 , 0.0001 ), _FresnelPower ) );
				float4 lerpResult410 = lerp( temp_output_39_0 , _FresnelColor , fresnelNode406);
				#ifdef _FRESNEL_ON
				float4 staticSwitch403 = ( temp_output_396_0 * lerpResult410 );
				#else
				float4 staticSwitch403 = temp_output_39_0;
				#endif
				float4 break458 = staticSwitch403;
				float4 appendResult459 = (float4(break458.r , break458.g , break458.b , temp_output_396_0));
				
				
				finalColor = appendResult459;
				return finalColor;
			}
			ENDCG
		}
	}
	// CustomEditor "ASEMaterialInspector"
	
	Fallback Off
}