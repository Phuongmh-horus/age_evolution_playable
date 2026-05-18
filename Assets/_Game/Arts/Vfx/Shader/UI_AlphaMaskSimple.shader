Shader "UI/AlphaMaskRotate"
{
    Properties
    {
        [PerRendererData] _MainTex ("Main (Sprite)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _MaskTex ("Mask (Alpha)", 2D) = "white" {}

        // Vùng mask trên Main UV (x,y,w,h). Thường để (0,0,1,1).
        _MaskRect ("Mask Rect in Main UV (x,y,w,h)", Vector) = (0,0,1,1)

        // Vùng lấy mask trong MaskTex UV (x,y,w,h). Thường để (0,0,1,1).
        _MaskUVRect ("Mask UV Rect (x,y,w,h)", Vector) = (0,0,1,1)

        // Pivot xoay trong không gian 0..1 của mask rect (0.5,0.5 = tâm).
        _MaskPivot ("Mask Pivot (x,y)", Vector) = (0.5,0.5,0,0)

        // Góc xoay ban đầu (độ).
        _MaskAngle ("Mask Angle (Deg)", Float) = 0

        // Tốc độ xoay (độ/giây). Ví dụ 90 = 1 vòng/4s.
        _MaskRotSpeed ("Mask Rotation Speed (Deg/s)", Float) = 90

        // Softness làm mượt theo alpha mask (0..1).
        _Softness ("Softness", Range(0,1)) = 0.0

        // Đảo mask
        _Invert ("Invert", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            sampler2D _MaskTex;
            float4 _MaskRect;
            float4 _MaskUVRect;

            float4 _MaskPivot;      // xy
            float  _MaskAngle;      // deg
            float  _MaskRotSpeed;   // deg/s
            float  _Softness;       // 0..1
            float  _Invert;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            float2 Rotate2D(float2 p, float rad)
            {
                float s = sin(rad);
                float c = cos(rad);
                return float2(c*p.x - s*p.y, s*p.x + c*p.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;

                // --- Map Main UV -> 0..1 trong _MaskRect ---
                float2 rectMin = _MaskRect.xy;
                float2 rectMax = _MaskRect.xy + _MaskRect.zw;

                float2 uv01 = (i.uv - rectMin) / max(rectMax - rectMin, 1e-6);

                // Ngoài vùng rect => mask=0
                float inRect = step(0.0, uv01.x) * step(0.0, uv01.y) * step(uv01.x, 1.0) * step(uv01.y, 1.0);

                // --- Rotate uv01 quanh pivot ---
                float2 pivot = _MaskPivot.xy;          // 0..1
                float angleDeg = _MaskAngle + _MaskRotSpeed * _Time.y;
                float rad = radians(angleDeg);

                float2 p = uv01 - pivot;
                p = Rotate2D(p, rad);
                float2 uvRot = p + pivot;

                // --- Map 0..1 -> MaskTex UV rect ---
                float2 maskUV = _MaskUVRect.xy + uvRot * _MaskUVRect.zw;

                fixed maskA = tex2D(_MaskTex, maskUV).a;
                if (_Invert > 0.5) maskA = 1.0 - maskA;

                // Softness theo alpha (mượt hơn/ít gắt)
                float s = saturate(_Softness);
                float softA = (s <= 0.0001) ? maskA : smoothstep(0.5 - 0.5*s, 0.5 + 0.5*s, maskA);

                col.a *= softA * inRect;
                return col;
            }
            ENDCG
        }
    }
}
