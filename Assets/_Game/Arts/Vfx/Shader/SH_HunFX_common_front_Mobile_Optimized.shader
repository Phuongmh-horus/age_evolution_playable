Shader "HunFX/SH_HunFX_common_front_Mobile_Optimized"
{
    Properties
    {
        _TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5)
        _MainTex ("Particle Texture", 2D) = "white" {}
        _InvFade ("Soft Particles Factor", Range(0.01,3.0)) = 1.0

        [HDR]_color1("color1", Color) = (2,2,2,1)
        [HDR]_color2("color2", Color) = (0,0,0,1)
        _ColorIntensity("ColorIntensity", Float) = 1

        _MainTexture("MainTexture", 2D) = "white" {}
        _MainTex_uv("MainTex_uv", Vector) = (0,0,1,1)
        [Toggle]_MainTex_UV_invert("MainTex_UV_invert", Float) = 0
        _MainTex_speed("MainTex_speed", Vector) = (0,0,0,0)

        _DissolveTex("DissolveTex", 2D) = "white" {}
        _DissolveTex_uv("DissolveTex_uv", Vector) = (0,0,1,1)
        _DissolveTex_speed("DissolveTex_speed", Vector) = (0,0,0,0)
        _Smoothstep("Smoothstep", Vector) = (0,1,0,0)
        [Toggle]_DissTex_UV_invert("DissTex_UV_invert", Float) = 0

        _VOTex("VOTex", 2D) = "white" {}
        _VOTex_uv("VOTex_uv", Vector) = (0,0,1,1)
        _VOTex_speed("VOTex_speed", Vector) = (0,0,0,0)
        _VOintensity("VOintensity", Float) = 0

        [Toggle(_USEFRESNEL_ON)] _UseFresnel("UseFresnel", Float) = 0
        _Fresnel_pow("Fresnel_pow", Float) = 5

        _Mask("Mask", 2D) = "white" {}
        _DistTex("DistTex", 2D) = "white" {}
        _DistTex_uv_speed("DistTex_uv_speed", Vector) = (1,1,0,0)
        _Distort_intensity("Distort_intensity", Range(0,0.5)) = 0
        _Mask_udrl("Mask_udrl", Vector) = (0,0,0,0)

        [Toggle(_USEPOSXSCROLL_ON)] _UsePosXScroll("UsePosXScroll", Float) = 0

        _SubDissolveTex("SubDissolveTex", 2D) = "white" {}
        [Toggle]_UseSubDissTex("UseSubDissTex", Float) = 0
        _SubDissolveTex_multi("SubDissolveTex_multi", Float) = 1
        _SubDissolveTex_add("SubDissolveTex_add", Float) = 0

        [Toggle]_UseSoftParticle("Use Soft Particle", Float) = 0
        [HideInInspector] _texcoord("", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
        }

        LOD 100

        Pass
        {
            Name "FORWARD_UNLIT_MOBILE"

            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            Cull Back
            Lighting Off
            ZWrite Off
            ZTest LEqual

            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma shader_feature_local _USEPOSXSCROLL_ON
            #pragma shader_feature_local _USEFRESNEL_ON

            #include "UnityCG.cginc"

            sampler2D _MainTexture;
            sampler2D _DissolveTex;
            sampler2D _Mask;
            sampler2D _DistTex;
            sampler2D _SubDissolveTex;
            sampler2D _VOTex;

            float4 _MainTexture_ST;
            float4 _Mask_ST;
            float4 _SubDissolveTex_ST;

            fixed4 _TintColor;
            half4 _color1;
            half4 _color2;

            half _ColorIntensity;

            half4 _MainTex_uv;
            half _MainTex_UV_invert;
            half2 _MainTex_speed;

            half4 _DissolveTex_uv;
            half2 _DissolveTex_speed;
            half2 _Smoothstep;
            half _DissTex_UV_invert;

            half4 _DistTex_uv_speed;
            half _Distort_intensity;

            half4 _Mask_udrl;

            half _UseSubDissTex;
            half _SubDissolveTex_multi;
            half _SubDissolveTex_add;

            half4 _VOTex_uv;
            half2 _VOTex_speed;
            half _VOintensity;

            half _Fresnel_pow;

            half _UseSoftParticle;
            half _InvFade;

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float4 texcoord : TEXCOORD0;
                float4 custom1 : TEXCOORD1;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                half4 uv0 : TEXCOORD0;
                half4 custom1 : TEXCOORD1;
                half3 worldNormal : TEXCOORD2;
                half3 viewDir : TEXCOORD3;
                float4 projPos : TEXCOORD4;
                UNITY_FOG_COORDS(5)
            };

            inline half2 ApplyUV(half2 uv, half4 uvData, half2 speed)
            {
                return ((uv + uvData.xy) * uvData.zw) + speed * _Time.y;
            }

            inline half2 MaybeInvertUV(half2 uv, half enabled)
            {
                return lerp(uv, uv.yx, step(0.5h, enabled));
            }

            inline half SafeSmoothstep(half edge0, half edge1, half x)
            {
                half denom = max(abs(edge1 - edge0), 0.0001h);
                half t = saturate((x - edge0) / denom);
                return t * t * (3.0h - 2.0h * t);
            }

            v2f vert(appdata v)
            {
                v2f o;

                half2 voUV = ApplyUV(v.texcoord.xy, _VOTex_uv, _VOTex_speed);
                half vo = tex2Dlod(_VOTex, float4(voUV, 0, 0)).r;

                v.vertex.xyz += v.normal * (vo * _VOintensity);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv0 = v.texcoord;
                o.custom1 = v.custom1;

                half3 worldNormal = UnityObjectToWorldNormal(v.normal);
                half3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                o.worldNormal = worldNormal;
                o.viewDir = UnityWorldSpaceViewDir(worldPos);

                o.projPos = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.projPos.z);

                UNITY_TRANSFER_FOG(o, o.pos);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half2 baseUV = i.uv0.xy;

                half2 posScroll;
                #ifdef _USEPOSXSCROLL_ON
                    posScroll = half2(i.custom1.y, 0);
                #else
                    posScroll = half2(0, i.custom1.y);
                #endif

                half2 distUV = baseUV * _DistTex_uv_speed.xy + _DistTex_uv_speed.zw * _Time.y;
                half distort = tex2D(_DistTex, distUV).r * _Distort_intensity;

                half2 mainUV = ApplyUV(baseUV + distort + posScroll, _MainTex_uv, _MainTex_speed);
                mainUV = MaybeInvertUV(mainUV, _MainTex_UV_invert);

                fixed4 mainTex = tex2D(_MainTexture, mainUV);

                half2 dissUV = ApplyUV(baseUV + distort + posScroll, _DissolveTex_uv, _DissolveTex_speed);
                dissUV = MaybeInvertUV(dissUV, _DissTex_UV_invert);

                half dissolve = tex2D(_DissolveTex, dissUV).g;

                if (_UseSubDissTex > 0.5h)
                {
                    half2 subUV = baseUV * _SubDissolveTex_ST.xy + _SubDissolveTex_ST.zw;
                    half sub = tex2D(_SubDissolveTex, subUV).r;
                    dissolve -= sub * _SubDissolveTex_multi + _SubDissolveTex_add;
                }

                half dissolveMask = SafeSmoothstep(_Smoothstep.x, _Smoothstep.y, dissolve - i.custom1.x);

                half alpha = mainTex.a * i.color.a * dissolveMask;

                #ifdef _USEFRESNEL_ON
                    half3 n = normalize(i.worldNormal);
                    half3 v = normalize(i.viewDir);
                    half fresnel = pow(saturate(1.0h - dot(n, v)), _Fresnel_pow);
                    alpha *= fresnel;
                #endif

                half2 maskUV = baseUV * _Mask_ST.xy + _Mask_ST.zw;
                half mask = tex2D(_Mask, maskUV).r;

                half u = baseUV.x;
                half v = baseUV.y;

                half edgeDown  = SafeSmoothstep(0.0h, max(_Mask_udrl.y, 0.0001h), v);
                half edgeUp    = SafeSmoothstep(0.0h, max(_Mask_udrl.x, 0.0001h), 1.0h - v);
                half edgeLeft  = SafeSmoothstep(0.0h, max(_Mask_udrl.w, 0.0001h), u);
                half edgeRight = SafeSmoothstep(0.0h, max(_Mask_udrl.z, 0.0001h), 1.0h - u);

                half edgeMask = edgeDown * edgeUp * edgeLeft * edgeRight;

                alpha *= mask * edgeMask;

                if (_UseSoftParticle > 0.5h)
                {
                    float sceneZ = LinearEyeDepth(
                        SAMPLE_DEPTH_TEXTURE_PROJ(
                            _CameraDepthTexture,
                            UNITY_PROJ_COORD(i.projPos)
                        )
                    );

                    float partZ = i.projPos.z;
                    half fade = saturate(_InvFade * (sceneZ - partZ));
                    alpha *= fade;
                }

                half colorIntensity = _ColorIntensity + i.custom1.z;
                half4 gradientColor = lerp(_color2, _color1, mainTex.r);

                fixed3 rgb = gradientColor.rgb * colorIntensity * i.color.rgb * _TintColor.rgb;
                fixed4 col = fixed4(rgb, alpha * _TintColor.a);

                UNITY_APPLY_FOG(i.fogCoord, col);

                return col;
            }

            ENDCG
        }
    }

    CustomEditor "ASEMaterialInspector"
    Fallback Off
}
