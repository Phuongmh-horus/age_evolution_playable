Shader "OmniShade/Standard_Luna"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Brightness ("Brightness", Range(0,25)) = 1
        _Contrast ("Contrast", Range(0,25)) = 1
        _Saturation ("Saturation", Range(0,2)) = 1
        [Toggle] _IgnoreMainTexAlpha ("Ignore Main Texture Alpha", Float) = 0

        [HeaderGroup(Diffuse)]
        [Toggle(DIFFUSE)] _Diffuse ("Enable Diffuse", Float) = 1
        _DiffuseWrap ("Diffuse Softness", Range(-1,1)) = 0
        _DiffuseBrightness ("Diffuse Brightness", Range(0,25)) = 1
        _DiffuseContrast ("Diffuse Contrast", Range(0.01,25)) = 1

        [HeaderGroup(Occlusion Map)]
        _LightmapTex ("Occlusion Map", 2D) = "white" {}
        _LightmapColor ("Occlusion Color", Color) = (1,1,1,1)
        _LightmapBrightness ("Occlusion Brightness", Range(0,25)) = 1
        [KeywordEnum(UV1, UV2)] _LightmapUV ("Occlusion UV", Float) = 0

        [HeaderGroup(Emissive)]
        [HDR] _Emissive ("Emissive Color", Color) = (0,0,0,0)
        _EmissiveTex ("Emissive Map", 2D) = "white" {}

        [HeaderGroup(Vertex Colors)]
        [Toggle(VERTEX_COLORS)] _VertexColors ("Enable Vertex Colors", Float) = 0
        _VertexColorsAmount ("Vertex Colors Amount", Range(0,1)) = 1
        _VertexColorsContrast ("Vertex Colors Contrast", Range(0,25)) = 1

        [HeaderGroup(Transparency Mask)]
        _TransparencyMaskTex ("Transparency Mask", 2D) = "white" {}
        _TransparencyMaskAmount ("Mask Amount", Range(0,25)) = 1
        _TransparencyMaskContrast ("Mask Contrast", Range(0,25)) = 1

        [HeaderGroup(Environment)]
        _AmbientBrightness ("Ambient Brightness", Range(0,25)) = 1
        [Toggle(FOG)] _Fog ("Enable Fog", Float) = 1

        [HeaderGroup(Culling And Blending)]
        [Enum(Opaque,0, Transparent,1, Transparent Additive,2, Transparent Additive Alpha,3, Opaque Cutout,4)]
        _Preset ("Culling And Blend Preset", Float) = 0

        [Header(Culling)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Culling", Float) = 2
        [Enum(Off,0, On,1)] _ZWrite ("Z Write", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Z Test", Float) = 4
        _ZOffset ("Depth Offset", Range(-5,5)) = 0
        [Toggle(CUTOUT)] _Cutout ("Cutout Transparency", Float) = 0
        _CutoutCutoff ("Cutoff", Range(0,1)) = 0.5

        [Header(Blending)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SourceBlend ("Source Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DestBlend ("Dest Blend", Float) = 0
        [Enum(UnityEngine.Rendering.BlendOp)] _BlendOp ("Blend Mode", Float) = 0
    }

    SubShader
    {
        Name "Luna Shader"
        Pass
        {
            Name "ForwardBase"
            Tags { "LightMode"="ForwardBase" }

            Cull [_Cull]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Blend [_SourceBlend][_DestBlend]
            BlendOp [_BlendOp]

            CGPROGRAM
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #pragma target 3.0
            #pragma exclude_renderers gles
            #pragma vertex vert
            #pragma fragment frag
            //#pragma multi_compile_fog

            #pragma multi_compile_local _ DIFFUSE
            #pragma shader_feature_local LIGHT_MAP
            #pragma multi_compile_local _LIGHTMAPUV_UV1 _LIGHTMAPUV_UV2
            #pragma multi_compile_local _ EMISSIVE_MAP
            #pragma shader_feature_local VERTEX_COLORS
            #pragma shader_feature_local TRANSPARENCY_MASK
            #pragma multi_compile_local _ FOG
            #pragma shader_feature_local CUTOUT

            sampler2D _MainTex; float4 _MainTex_ST;
            half4 _Color;
            half _Brightness, _Contrast, _Saturation, _IgnoreMainTexAlpha;

            half _DiffuseWrap, _DiffuseBrightness, _DiffuseContrast;

            sampler2D _LightmapTex; float4 _LightmapTex_ST;
            half4 _LightmapColor; half _LightmapBrightness;

            half4 _Emissive;
            sampler2D _EmissiveTex; float4 _EmissiveTex_ST;

            half _VertexColorsAmount, _VertexColorsContrast;

            sampler2D _TransparencyMaskTex; float4 _TransparencyMaskTex_ST;
            half _TransparencyMaskAmount, _TransparencyMaskContrast;

            half _AmbientBrightness, _CutoutCutoff, _ZOffset;

            struct appdata {
                float4 vertex: POSITION;
                float3 normal: NORMAL;
                float2 uv: TEXCOORD0;
                float2 uv2: TEXCOORD1;
                fixed4 color: COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f {
                float4 pos: SV_POSITION;
                float2 uv: TEXCOORD0;
                float2 uv2: TEXCOORD1;
                half3 worldNormal: TEXCOORD2;
                fixed4 vertexColor: COLOR;
                UNITY_FOG_COORDS(3)
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);

                #if defined(ZOFFSET)
                    o.pos.z += _ZOffset * 0.001;
                #endif

                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv2 = v.uv2;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.vertexColor = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            inline half3 ApplySaturation(half3 c, half sat)
            {
                half luma = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
                return lerp(half3(luma,luma,luma), c, sat);
            }

            half4 frag(v2f i) : SV_Target
            {
                // In Linear projects, tex2D(sRGB texture) returns Linear already.
                half4 col = (half4)tex2D(_MainTex, i.uv) * _Color;

                if (_IgnoreMainTexAlpha > 0.5h) col.a = _Color.a;

                col.rgb = ((col.rgb - 0.5h) * _Contrast + 0.5h) * _Brightness;
                col.rgb = ApplySaturation(col.rgb, _Saturation);

                #if defined(VERTEX_COLORS)
                    half3 vertCol = pow((half3)i.vertexColor.rgb, _VertexColorsContrast);
                    col.rgb = lerp(col.rgb, col.rgb * vertCol, _VertexColorsAmount);
                #endif

                half3 lightColor = (half3)_LightColor0.rgb;
                half3 ambientColor = (half3)unity_AmbientSky.rgb;

                #if defined(DIFFUSE)
                    half3 n = normalize(i.worldNormal);
                    half3 ldir = normalize((half3)_WorldSpaceLightPos0.xyz);
                    half ndl = dot(n, ldir);
                    half diff = saturate((ndl + _DiffuseWrap) / (1.0h + _DiffuseWrap));
                    diff = pow(diff, _DiffuseContrast) * _DiffuseBrightness;
                    col.rgb *= diff * lightColor + ambientColor * _AmbientBrightness;
                #else
                    col.rgb *= ambientColor * _AmbientBrightness;
                #endif

                #if defined(LIGHT_MAP)
                    float2 luv = i.uv;
                    #if defined(_LIGHTMAPUV_UV2)
                        luv = i.uv2;
                    #endif
                    half3 lm = (half3)tex2D(_LightmapTex, luv * _LightmapTex_ST.xy + _LightmapTex_ST.zw).rgb;
                    col.rgb *= lm * (half3)_LightmapColor.rgb * _LightmapBrightness;
                #endif

                #if defined(EMISSIVE_MAP)
                    half3 e = (half3)tex2D(_EmissiveTex, i.uv * _EmissiveTex_ST.xy + _EmissiveTex_ST.zw).rgb;
                    col.rgb += e * (half3)_Emissive.rgb;
                #else
                    col.rgb += (half3)_Emissive.rgb;
                #endif

                #if defined(TRANSPARENCY_MASK)
                    half mask = (half)tex2D(_TransparencyMaskTex, i.uv * _TransparencyMaskTex_ST.xy + _TransparencyMaskTex_ST.zw).r;
                    mask = pow(mask, _TransparencyMaskContrast) * _TransparencyMaskAmount;
                    col.a *= mask;
                #endif

                #if defined(CUTOUT)
                    clip(col.a - _CutoutCutoff);
                #endif

                #if defined(FOG)
                    UNITY_APPLY_FOG(i.fogCoord, col);
                #endif

                // Return in project color space the way Unity expects.
                // In Linear projects, Unity will handle output conversion appropriately per platform.
                return col;
            }
            ENDCG
        }
    }

    Fallback "Standard"
}