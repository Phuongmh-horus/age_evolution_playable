Shader "Custom/SpriteHorizontalFill"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0

        // Biến điều khiển Fill (0 là rỗng, 1 là đầy)
        _FillAmount ("Fill Amount", Range(0, 1)) = 1.0
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
            #pragma multi_compile _ PIXELSNAP_ON
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            fixed4 _Color;
            float _FillAmount;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap (OUT.vertex);
                #endif
                return OUT;
            }

            sampler2D _MainTex;
            sampler2D _AlphaTex;
            float _AlphaSplitEnabled;

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord);
                c *= IN.color;
                c.rgb *= c.a; // Premultiply Alpha để tránh viền trắng

                // --- LOGIC FILL ---
                // IN.texcoord.x chạy từ 0 (trái) đến 1 (phải)
                // Nếu tọa độ X của pixel hiện tại lớn hơn FillAmount -> Ẩn nó đi (Alpha = 0)

                // Cách 1: Dùng step (Tối ưu GPU, không dùng if)
                // step(edge, x) trả về 1 nếu x >= edge, ngược lại 0
                // Ở đây ta muốn: Nếu u.x <= FillAmount thì hiện (1), ngược lại ẩn (0)
                float visible = step(IN.texcoord.x, _FillAmount);

                c.a *= visible;
                c.rgb *= visible;

                return c;
            }
        ENDCG
        }
    }
}
