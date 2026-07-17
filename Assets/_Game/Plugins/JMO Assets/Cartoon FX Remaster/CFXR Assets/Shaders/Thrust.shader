Shader "Custom/RPG/ThrustEffect_FinalSoft_CustomData"
{
    Properties
    {
        [Header(Blend Settings)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 5 // 5 = SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 10 // 10 = OneMinusSrcAlpha

        [Space(10)]
        [Header(Main Textures)]
        _MainTex ("Main Mask", 2D) = "white" {}
        _NoiseTex ("Flow Noise", 2D) = "gray" {}
        _DissolveTex ("Dissolve Tex", 2D) = "gray" {}

        [HDR]_ColorInner ("Inner Color", Color) = (1.0, 0.85, 0.35, 1)
        [HDR]_ColorOuter ("Outer Color", Color) = (1.0, 0.45, 0.10, 1)
        [HDR]_EdgeColor  ("Dissolve Edge Color", Color) = (1.0, 0.95, 0.65, 1)

        _NoiseScroll1 ("Noise Scroll 1 (X,Y)", Vector) = (0, -2.0, 0, 0)
        _NoiseScroll2 ("Noise Scroll 2 (X,Y)", Vector) = (0, -3.2, 0, 0)
        _NoiseTiling1 ("Noise Tiling 1", Vector) = (1.0, 2.0, 0, 0)
        _NoiseTiling2 ("Noise Tiling 2", Vector) = (2.0, 3.0, 0, 0)
        _NoiseStrength ("Noise Strength", Range(0,2)) = 0.7

        _Opacity ("Opacity", Range(0,5)) = 1.5
        _LengthFade ("Length Fade", Range(0.1,6)) = 1.8
        _TipFade ("Tip Fade", Range(0.1,6)) = 2.0
        _MainMaskPower ("Main Mask Power", Range(0.1,4)) = 1.0

        _ViewEdgeFade ("View Edge Fade", Range(0,1)) = 0.2

        _FresnelPower ("Fresnel Power", Range(0.1,8)) = 2.0
        _FresnelColorStrength ("Fresnel Color Strength", Range(0,3)) = 0.35

        _VertexWobble ("Vertex Wobble", Range(0,0.5)) = 0.03
        _VertexSpeed ("Vertex Speed", Float) = 3.0
        _VertexFrequency ("Vertex Frequency", Float) = 6.0

        _Dissolve ("Base Dissolve", Range(0,1)) = 0
        _CustomDissolveMultiplier ("Custom Dissolve Multiplier", Range(0,2)) = 1
        _DissolveSoftness ("Dissolve Softness", Range(0.001,0.5)) = 0.08
        _EdgeIntensity ("Edge Intensity", Range(0,5)) = 1.5
        
        _DissolveScroll ("Dissolve Scroll (X,Y)", Vector) = (0, -0.5, 0, 0)
        
        _DirectionalDissolve ("Directional Dissolve", Range(0,1)) = 0.6
        _DissolveDirection ("Dissolve Direction", Range(0,1)) = 1

        _InvertY ("Invert UV Y", Range(0,1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        // [MỚI] Sử dụng biến thay vì code cứng
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Back
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _NoiseTex;
            sampler2D _DissolveTex;

            float4 _MainTex_ST;
            float4 _NoiseTex_ST;
            float4 _DissolveTex_ST;

            fixed4 _ColorInner, _ColorOuter, _EdgeColor;

            float4 _NoiseScroll1, _NoiseScroll2;
            float4 _NoiseTiling1, _NoiseTiling2;
            float _NoiseStrength;

            float _Opacity, _LengthFade, _TipFade, _MainMaskPower;
            float _ViewEdgeFade;
            float _FresnelPower, _FresnelColorStrength;

            float _VertexWobble, _VertexSpeed, _VertexFrequency;

            float _Dissolve, _CustomDissolveMultiplier, _DissolveSoftness;
            float _EdgeIntensity, _DirectionalDissolve, _DissolveDirection, _InvertY;
            float4 _DissolveScroll;

            struct appdata
            {
                float4 vertex    : POSITION;
                float3 normal    : NORMAL;
                float4 color     : COLOR;      
                float4 texcoord0 : TEXCOORD0;  
                float2 texcoord1 : TEXCOORD1;  
            };

            struct v2f
            {
                float4 pos           : SV_POSITION;
                float4 color         : COLOR;
                float3 uv_custom     : TEXCOORD0; 
                float3 worldPos      : TEXCOORD1;
                float3 worldNorm     : TEXCOORD2;
                float3 viewDir       : TEXCOORD3;
            };

            v2f vert(appdata v)
            {
                v2f o;

                float2 uv = TRANSFORM_TEX(v.texcoord0.xy, _MainTex);
                float customDissolve = v.texcoord0.z;

                if (_InvertY > 0.5)
                    uv.y = 1.0 - uv.y;

                float angleWave = sin(uv.x * 6.2831853 * _VertexFrequency + _Time.y * _VertexSpeed);
                float lengthMask = pow(saturate(1.0 - uv.y), 1.2);

                float3 localOffset = v.normal * angleWave * _VertexWobble * lengthMask;
                float4 localPos = v.vertex + float4(localOffset, 0.0);

                o.pos = UnityObjectToClipPos(localPos);
                
                o.uv_custom.xy = uv;
                o.uv_custom.z = customDissolve;

                o.worldPos = mul(unity_ObjectToWorld, localPos).xyz;
                o.worldNorm = UnityObjectToWorldNormal(v.normal);
                o.viewDir = _WorldSpaceCameraPos.xyz - o.worldPos; 
                o.color = v.color; 

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv_custom.xy;
                float customDissolve = i.uv_custom.z;

                float2 noiseUV1 = uv * _NoiseTiling1.xy + (_NoiseScroll1.xy * _Time.y);
                float2 noiseUV2 = uv * _NoiseTiling2.xy + (_NoiseScroll2.xy * _Time.y);

                float noise1 = tex2D(_NoiseTex, noiseUV1).r;
                float noise2 = tex2D(_NoiseTex, noiseUV2).r;
                float flowNoise = lerp(noise1, noise2, 0.5);
                flowNoise = lerp(1.0, flowNoise, _NoiseStrength);

                float mainMask = tex2D(_MainTex, uv).r;
                mainMask = pow(saturate(mainMask), _MainMaskPower);

                float lengthFade = pow(saturate(1.0 - uv.y), _LengthFade);
                float tipFade = saturate(1.0 - pow(uv.y, _TipFade));

                float3 N = normalize(i.worldNorm);
                float3 V = normalize(i.viewDir);
                float fresnel = pow(1.0 - saturate(dot(N, V)), _FresnelPower);

                fixed3 col = lerp(_ColorOuter.rgb, _ColorInner.rgb, lengthFade);

                float alpha = mainMask * flowNoise * lengthFade * tipFade;
                alpha *= _Opacity;

                alpha *= lerp(1.0, 1.0 - fresnel, _ViewEdgeFade);
                col += _EdgeColor.rgb * fresnel * _FresnelColorStrength * lengthFade;

                float2 dissolveUV = TRANSFORM_TEX(uv, _DissolveTex);
                dissolveUV += (_DissolveScroll.xy * _Time.y);

                float dissolveNoise = tex2D(_DissolveTex, dissolveUV).r;
                float directionalY = lerp(uv.y, 1.0 - uv.y, _DissolveDirection);
                float dissolveTex = lerp(dissolveNoise, directionalY, _DirectionalDissolve);

                float finalDissolve = saturate(_Dissolve + customDissolve * _CustomDissolveMultiplier);
                float dissolveFactor = 1.0 - finalDissolve;

                float dissolveMask = smoothstep(
                    dissolveFactor - _DissolveSoftness,
                    dissolveFactor + _DissolveSoftness,
                    dissolveTex
                );

                alpha *= dissolveMask;

                float edgeBand = smoothstep(dissolveFactor - _DissolveSoftness * 0.5, dissolveFactor, dissolveTex) -
                                 smoothstep(dissolveFactor, dissolveFactor + _DissolveSoftness * 0.5, dissolveTex);

                col += _EdgeColor.rgb * edgeBand * _EdgeIntensity;

                col *= i.color.rgb;
                alpha *= i.color.a;

                float finalAlpha = saturate(alpha);
                return fixed4(col * finalAlpha, finalAlpha);
            }
            ENDCG
        }
    }
}