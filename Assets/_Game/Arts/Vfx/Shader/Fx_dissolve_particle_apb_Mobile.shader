Shader "Optimized/Fx_dissolve_particle_apb_Mobile"
{
    Properties
    {
        [NoScaleOffset]Main_tex("Texture2D", 2D) = "white" {}
        Vector1_930B327D("Highlight_Min", Float) = -10
        Vector1_270105AC("Highlight_Max", Float) = -1
        [ToggleUI]Boolean_9C0948F4("Use SoftParticleFactor?", Float) = 1
        Vector1_2C5A3101("Emission_Power", Float) = 1

        [HideInInspector]_BUILTIN_QueueOffset("Float", Float) = 0
        [HideInInspector]_BUILTIN_QueueControl("Float", Float) = -1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Pass
        {
            Name "FORWARD_UNLIT_OPTIMIZED"

            Cull Off
            Lighting Off
            ZWrite Off
            ZTest LEqual

            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ColorMask RGB

            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            sampler2D Main_tex;
            float4 Main_tex_ST;

            float Vector1_930B327D; // Highlight_Min
            float Vector1_270105AC; // Highlight_Max
            float Boolean_9C0948F4; // Use SoftParticleFactor?
            float Vector1_2C5A3101; // Emission_Power

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 projPos : TEXCOORD3;
                UNITY_FOG_COORDS(4)
            };

            inline float RemapFloat(float value, float2 inMinMax, float2 outMinMax)
            {
                float denom = max(abs(inMinMax.y - inMinMax.x), 0.0001);
                return outMinMax.x + (value - inMinMax.x) * (outMinMax.y - outMinMax.x) / denom;
            }

            v2f vert(appdata v)
            {
                v2f o;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv0 = v.uv0;
                o.uv1 = v.uv1;
                o.uv2 = v.uv2;

                o.projPos = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.projPos.z);

                UNITY_TRANSFER_FOG(o, o.pos);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv0 = i.uv0.xy;

                float4 texA = tex2D(Main_tex, uv0);

                float uOffset = texA.b * i.uv1.b;
                float2 flowUV = float2(uv0.x + uOffset, uv0.y);

                float4 texB = tex2D(Main_tex, flowUV);

                float life = i.uv1.r;
                float dissolvePower = i.uv1.g;

                float oneMinusLife = 1.0 - life;
                float dissolveInput = texB.g + oneMinusLife;

                float inMax = 1.0 + life * 0.1;
                float outMin = -(life * dissolvePower);

                float dissolve = RemapFloat(
                    dissolveInput,
                    float2(0.0, inMax),
                    float2(outMin, 1.0)
                );

                dissolve = saturate(dissolve);

                float baseMask = texB.r * dissolve;

                float3 baseColor = i.color.rgb * baseMask;

                float highlightInput = texB.g + oneMinusLife;

                float highlightMask = RemapFloat(
                    highlightInput,
                    float2(0.0, 1.0),
                    float2(Vector1_930B327D, Vector1_270105AC)
                );

                highlightMask = saturate(highlightMask);

                float highlightAlpha = i.color.a * i.uv2.a * highlightMask;

                float3 highlightColor = Vector1_2C5A3101 * i.uv2.rgb;

                float3 finalColor = lerp(baseColor, highlightColor, highlightAlpha);

                float alpha = texB.a * dissolve * i.color.a;

                if (Boolean_9C0948F4 > 0.5)
                {
                    float sceneZ = LinearEyeDepth(
                        SAMPLE_DEPTH_TEXTURE_PROJ(
                            _CameraDepthTexture,
                            UNITY_PROJ_COORD(i.projPos)
                        )
                    );

                    float particleZ = i.projPos.z;
                    float softFactor = saturate(i.uv1.a * (sceneZ - particleZ));

                    alpha *= softFactor;
                }

                fixed4 col = fixed4(finalColor, alpha);

                UNITY_APPLY_FOG(i.fogCoord, col);

                return col;
            }
            ENDCG
        }
    }

    FallBack Off
}
