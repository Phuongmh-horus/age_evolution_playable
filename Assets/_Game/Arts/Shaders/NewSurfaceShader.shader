Shader "FX/LightningSphere_TextureStrip_Flipbook"
{
    Properties
    {
        [HDR]_Tint ("Tint (HDR)", Color) = (2.5,2.5,2.5,1)

        _MainTex ("Lightning Texture Sheet", 2D) = "white" {}
        _NoiseTex ("Noise", 2D) = "gray" {}

        _MainTiling ("Main Tiling (U,V)", Vector) = (1,1,0,0)
        _NoiseTiling ("Noise Tiling (U,V)", Vector) = (1.5,1.5,0,0)

        _ScrollU ("Scroll Around Sphere (U)", Float) = 1.2
        _ScrollV ("Scroll V", Float) = 0.0

        _NoiseScrollU ("Noise Scroll U", Float) = 0.4
        _NoiseScrollV ("Noise Scroll V", Float) = 0.3

        _Distort ("Noise Distortion", Range(0,0.3)) = 0.08

        _FresnelPow ("Fresnel Power", Range(0.5,8)) = 2.5
        _FresnelStrength ("Fresnel Strength", Range(0,3)) = 1.2

        _Intensity ("Lightning Intensity", Range(0,5)) = 1.5
        _Alpha ("Overall Alpha", Range(0,1)) = 1.0

        [Toggle]_UseMeshUV ("Use Mesh UV (Particle/Quad)", Float) = 0

        // ===== FLIPBOOK =====
        _Columns ("Flipbook Columns", Float) = 4
        _Rows ("Flipbook Rows", Float) = 4
        _FPS ("Flipbook FPS", Float) = 20
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _NoiseTex;

            fixed4 _Tint;
            float4 _MainTiling;
            float4 _NoiseTiling;

            float _ScrollU, _ScrollV;
            float _NoiseScrollU, _NoiseScrollV;
            float _Distort;

            float _FresnelPow, _FresnelStrength;
            float _Intensity;
            float _Alpha;
            float _UseMeshUV;

            float _Columns, _Rows, _FPS;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wNrm : TEXCOORD0;
                float3 vDir : TEXCOORD1;
                float2 uv  : TEXCOORD2;
            };

            float2 SphericalUV(float3 n)
            {
                n = normalize(n);
                float u = atan2(n.z, n.x) * (1.0 / (2.0 * UNITY_PI)) + 0.5;
                float v = asin(clamp(n.y, -1.0, 1.0)) * (1.0 / UNITY_PI) + 0.5;
                return float2(u, v);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);

                float3 wPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wNrm = UnityObjectToWorldNormal(v.normal);
                o.vDir = _WorldSpaceCameraPos.xyz - wPos;

                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 N = normalize(i.wNrm);
                float3 V = normalize(i.vDir);

                float2 uvSphere = SphericalUV(N);
                float2 uvMesh   = i.uv;
                float2 uv = lerp(uvSphere, uvMesh, _UseMeshUV);

                // ===== Noise distortion =====
                float2 nuv = uv * _NoiseTiling.xy +
                             float2(_NoiseScrollU, _NoiseScrollV) * _Time.y;

                float n = tex2D(_NoiseTex, nuv).r;
                float2 distort = (n * 2.0 - 1.0) * _Distort;

                // ===== Base UV scroll =====
                float2 luv = (uv + distort) * _MainTiling.xy +
                             float2(_ScrollU, _ScrollV) * _Time.y;

                // ===== Flipbook =====
                float totalFrames = _Columns * _Rows;
                float frame = floor(_Time.y * _FPS);
                frame = fmod(frame, totalFrames);

                float col = fmod(frame, _Columns);
                float row = floor(frame / _Columns);

                float2 cellSize = float2(1.0 / _Columns, 1.0 / _Rows);

                luv = frac(luv); // repeat inside cell
                luv = luv * cellSize + float2(col, (_Rows - 1 - row)) * cellSize;

                float l = tex2D(_MainTex, luv).r;

                // ===== Fresnel =====
                float fres = pow(1.0 - saturate(dot(N, V)), _FresnelPow) * _FresnelStrength;

                float intensity = l * _Intensity * (0.6 + fres);

                fixed4 colOut = _Tint * intensity;
                colOut.a = saturate(intensity) * _Alpha;

                return colOut;
            }
            ENDCG
        }
    }

    FallBack Off
}
