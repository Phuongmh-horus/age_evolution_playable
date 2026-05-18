Shader "Horus/BackgroundTiling2D_Radial_Width"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)

        _ScrollXSpeed("Scroll X", Range(-5,5)) = 1.0
        _ScrollYSpeed("Scroll Y", Range(-5,5)) = 0.0
        _Tiling("Tiling (X,Y)", Vector) = (1,1,0,0)

        _GradientCenter("Gradient Center", Color) = (1,1,1,1)
        _GradientEdge("Gradient Edge", Color) = (0,0,0,1)
        _MainTexOpacity("MainTex Opacity", Range(0,1)) = 1.0

        _GradientWidth("Gradient Width", Range(0.01, 5)) = 1.0

        // ✅ Offset tâm gradient (UV gốc)
        _GradientOffset ("Gradient Offset (XY)", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            fixed4 _Color;
            float _ScrollXSpeed;
            float _ScrollYSpeed;
            float4 _Tiling;

            fixed4 _GradientCenter;
            fixed4 _GradientEdge;
            float  _GradientWidth;
            float4 _GradientOffset;

            float _MainTexOpacity;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;        // UV đã tiling + scroll (texture)
                float2 baseUV : TEXCOORD1;    // ✅ UV gốc (gradient)
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);

                // ✅ UV gốc cho gradient (0–1)
                o.baseUV = v.uv;

                // ✅ UV cho texture (giữ nguyên logic cũ)
                float2 uv = v.uv;
                uv *= _Tiling.xy;
                uv.x += _Time.y * _ScrollXSpeed;
                uv.y += _Time.y * _ScrollYSpeed;
                o.uv = uv;

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // =========================
                // TEXTURE (GIỮ NGUYÊN)
                // =========================
                fixed4 textureColor = tex2D(_MainTex, frac(i.uv));
                textureColor *= _Color;
                textureColor.a *= _MainTexOpacity;

                // =========================
                // GRADIENT (KHÔNG DÍNH TILING / SCROLL)
                // =========================

                // UV gốc 0–1 của mesh
                float2 uv01 = i.baseUV;

                // Center về (0,0)
                float2 centeredUV = uv01 - 0.5;

                // Apply Offset
                centeredUV -= _GradientOffset.xy;

                // Radial distance
                float dist = length(centeredUV) / max(_GradientWidth, 0.0001);
                float t = saturate(dist);

                fixed4 gradientBackground = lerp(_GradientCenter, _GradientEdge, t);

                // =========================
                // FINAL COMPOSE
                // =========================
                fixed4 finalColor;
                finalColor.rgb = lerp(
                    gradientBackground.rgb,
                    textureColor.rgb,
                    textureColor.a
                );
                finalColor.a = 1.0;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "Unlit/Texture"
}
