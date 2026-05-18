Shader "Custom/VFX_Flipbook_Sine_Noise_HDR_TilingOK_Curved"
{
    Properties
    {
        _MainTex ("Flipbook Texture", 2D) = "white" {}
        _NoiseTex ("Noise Texture", 2D) = "gray" {}

        _Columns ("Columns", Float) = 4
        _Rows ("Rows", Float) = 4
        _FPS ("FPS", Float) = 16

        _SineSpeed ("Sine Speed", Float) = 2
        _SineAmp ("Sine Amplitude", Range(0,0.1)) = 0.02
        _SineFreq ("Sine Frequency", Float) = 10

        _NoiseStrength ("Noise Strength", Range(0,1)) = 0.2
        _NoiseSpeed ("Noise Speed", Float) = 1

        // ===== NEW: Beam Curve =====
        _CurveStrength ("Curve Strength", Range(0,1)) = 0.3
        _CurveFreq ("Curve Frequency", Float) = 1
        _CurveSpeed ("Curve Speed", Float) = 1

        [HDR]_Color ("HDR Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _NoiseTex;
            float4 _MainTex_ST;

            float _Columns, _Rows, _FPS;
            float _SineSpeed, _SineAmp, _SineFreq;
            float _NoiseStrength, _NoiseSpeed;

            // NEW
            float _CurveStrength, _CurveFreq, _CurveSpeed;

            half4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);

                // Apply Tiling & Offset
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // ---- Flipbook ----
                float frame = floor(_Time.y * _FPS);

                float col = fmod(frame, _Columns);
                float row = fmod(floor(frame / _Columns), _Rows);

                float2 cell = float2(1.0 / _Columns, 1.0 / _Rows);

                float2 uv = i.uv * cell;
                uv.x += col * cell.x;
                uv.y += 1.0 - cell.y - row * cell.y;

                // ===== Curve along beam (uốn cong tổng thể) =====
                float curve = sin(i.uv.y * _CurveFreq + _Time.y * _CurveSpeed);
                uv.x += curve * _CurveStrength * 0.15;

                // ---- Sine Distort (zigzag điện) ----
                float wave = sin(i.uv.y * _SineFreq + _Time.y * _SineSpeed);
                uv.x += wave * _SineAmp;

                // ---- Noise Distort ----
                float2 nUV = float2(i.uv.x, i.uv.y + _Time.y * _NoiseSpeed);
                float n = tex2D(_NoiseTex, nUV).r - 0.5;
                uv += n * _NoiseStrength;

                half4 colTex = tex2D(_MainTex, uv);

                // premultiply để bloom không lem viền
                colTex.rgb *= colTex.a;

                return colTex * _Color;
            }
            ENDCG
        }
    }
}
