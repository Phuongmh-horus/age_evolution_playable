Shader "Horus/GodRay 1"
{
    Properties
    {
        [Enum(Transparent, 10, Additive, 1)] _DstBlend ("Rendering Mode", Float) = 1
        _Color ("Color", color) = (1, 1, 1, 0.5)
        [Toggle] _FadeX ("Horizontal Fade", float) = 1
        [Toggle] _FadeY ("Vertical Fade", float) = 1
        _Fade ("Fade (xy - horizontal, zw - vertical)", vector) = (0, 1, 0, 1)
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }
        Blend SrcAlpha [_DstBlend]
        ZWrite Off
        Cull Off
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma shader_feature _FADEX_ON
            #pragma shader_feature _FADEY_ON

            #include "MyLib.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };
            
            float4 _Fade;
            fixed4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = _Color;
                #if _FADEX_ON
                col.a *= remap_clamp(i.uv.x, _Fade.x, _Fade.y, 0, 1);
                #endif

                #if _FADEY_ON
                col.a *= remap_clamp(i.uv.y, _Fade.z, _Fade.w, 0, 1);
                #endif
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}