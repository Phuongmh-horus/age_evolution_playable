Shader "Custom/FlagWaving"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        
        [Header(Wave Settings)]
        _WaveSpeed ("Wave Speed", Range(0, 10)) = 2.5
        _WaveAmplitude ("Wave Amplitude", Range(0, 1)) = 0.18
        _WaveFrequency ("Wave Frequency", Range(0, 20)) = 6.0
        _WaveDirection ("Wave Direction", Vector) = (1, 0.3, 0, 0)
        
        [Header(Secondary Wave)]
        _SecondaryWaveSpeed ("Secondary Wave Speed", Range(0, 10)) = 4.2
        _SecondaryWaveAmplitude ("Secondary Wave Amplitude", Range(0, 0.5)) = 0.1
        _SecondaryWaveFrequency ("Secondary Wave Frequency", Range(0, 20)) = 9.5
        
        [Header(Wind Settings)]
        _WindStrength ("Wind Strength", Range(0, 2)) = 0.6
        _WindDirection ("Wind Direction", Vector) = (1, 0.1, 0.4, 0)
        _WindTurbulence ("Wind Turbulence", Range(0, 1)) = 0.4
        
        [Header(Droop Settings)]
        _DroopStrength ("Droop Strength", Range(0, 2)) = 0.5
        _DroopCurve ("Droop Curve", Range(1, 4)) = 2.0
        
        [Header(Edge Flutter)]
        _EdgeFlutterStrength ("Edge Flutter Strength", Range(0, 2)) = 1.2
        _EdgeFlutterSpeed ("Edge Flutter Speed", Range(0, 10)) = 5.5
        
        [Header(Wave Shadow)]
        _WaveShadowStrength ("Wave Shadow Strength", Range(0, 1)) = 0.4
        _WaveShadowSoftness ("Wave Shadow Softness", Range(0.1, 2)) = 0.5
        _WaveShadowWidth ("Wave Shadow Width", Range(0.1, 5)) = 1.0
        
        [Header(Rendering)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 0
        _Brightness ("Brightness", Range(0.5, 2)) = 1.0
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        Cull [_Cull]
        
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            
            // Wave properties
            float _WaveSpeed;
            float _WaveAmplitude;
            float _WaveFrequency;
            float4 _WaveDirection;
            
            float _SecondaryWaveSpeed;
            float _SecondaryWaveAmplitude;
            float _SecondaryWaveFrequency;
            
            // Wind properties
            float _WindStrength;
            float4 _WindDirection;
            float _WindTurbulence;
            
            // Droop properties
            float _DroopStrength;
            float _DroopCurve;
            
            // Edge flutter
            float _EdgeFlutterStrength;
            float _EdgeFlutterSpeed;
            
            // Wave shadow
            float _WaveShadowStrength;
            float _WaveShadowSoftness;
            float _WaveShadowWidth;
            
            // Rendering
            float _Brightness;
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float waveDepth : TEXCOORD2; // Độ sâu của sóng (âm = lòng sóng, dương = đỉnh sóng)
            };
            
            // Noise function for turbulence
            float noise(float3 p)
            {
                return frac(sin(dot(p, float3(12.9898, 78.233, 45.5432))) * 43758.5453);
            }
            
            v2f vert(appdata v)
            {
                v2f o;
                
                // Lưu UV gốc
                float2 originalUV = v.uv;
                
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                float3 localPos = v.vertex.xyz;
                
                // Use UV.x to determine distance from pole (assuming flag is attached on left side)
                float distanceFromPole = v.uv.x;
                float heightFactor = v.uv.y;
                
                // Time-based animation
                float time = _Time.y;
                
                // Primary wave - horizontal wave motion
                float wavePhase = dot(localPos.xyz, _WaveDirection.xyz) * _WaveFrequency + time * _WaveSpeed;
                float wave = sin(wavePhase) * _WaveAmplitude;
                
                // Secondary wave for more complex motion
                float secondaryPhase = dot(localPos.xyz, float3(_WaveDirection.y, _WaveDirection.x, _WaveDirection.z)) 
                                      * _SecondaryWaveFrequency + time * _SecondaryWaveSpeed;
                float secondaryWave = sin(secondaryPhase) * _SecondaryWaveAmplitude;
                
                // Edge flutter - more intense at the free edge
                float edgeFlutter = sin(time * _EdgeFlutterSpeed + heightFactor * 10.0) 
                                  * cos(time * _EdgeFlutterSpeed * 1.3 + heightFactor * 8.0);
                edgeFlutter *= _EdgeFlutterStrength * 0.1;
                
                // Wind turbulence using noise
                float turbulence = (noise(worldPos.xyz * 2.0 + time * 0.5) - 0.5) * _WindTurbulence;
                
                // Distance factor with curve
                float distanceFactor = pow(distanceFromPole, _DroopCurve);
                
                // Calculate displacement
                float3 displacement = float3(0, 0, 0);
                
                // Main wave displacement (perpendicular to flag surface)
                displacement.z = (wave + secondaryWave) * distanceFactor;
                
                // Wind displacement (in wind direction)
                displacement += _WindDirection.xyz * _WindStrength * distanceFactor * 0.1;
                
                // DROOP EFFECT - Gravity pulling down the flag
                // Càng xa cột cờ thì càng rũ xuống nhiều
                float droopAmount = _DroopStrength * distanceFactor;
                displacement.y -= droopAmount;
                
                // Edge flutter (more pronounced at free edge)
                displacement.y += edgeFlutter * distanceFactor;
                displacement.x += edgeFlutter * 0.5 * distanceFactor;
                
                // Add turbulence
                displacement += turbulence * distanceFactor * 0.1;
                
                // Apply displacement
                v.vertex.xyz += displacement;
                
                // Recalculate normals for proper lighting
                float waveDerivative = cos(wavePhase) * _WaveFrequency * _WaveAmplitude * distanceFactor;
                float3 normalOffset = float3(0, 0, waveDerivative);
                v.normal = normalize(v.normal + normalOffset * 0.5);
                
                // Tính toán độ sâu của sóng để tạo shadow trong lòng sóng
                // Giá trị âm = lòng sóng (trough), giá trị dương = đỉnh sóng (crest)
                o.waveDepth = (wave + secondaryWave * 0.5) * distanceFactor;
                
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = originalUV; // Sử dụng UV gốc
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                
                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                // Sample texture với UV gốc
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                
                // Ánh sáng đều từ mọi phía - không phụ thuộc vào hướng normal
                // Chỉ thêm một chút ambient occlusion nhẹ dựa trên normal để có chiều sâu
                float ambientOcclusion = saturate(dot(i.worldNormal, float3(0, 1, 0)) * 0.15 + 0.85);
                
                // Tạo shadow trong lòng sóng
                // waveDepth < 0 = lòng sóng → có shadow
                // waveDepth > 0 = đỉnh sóng → không có shadow
                float waveShadow = -i.waveDepth / _WaveShadowWidth;
                
                // Làm mềm shadow với smoothstep
                waveShadow = smoothstep(0, _WaveShadowSoftness, waveShadow);
                
                // Giới hạn shadow từ 0 đến 1
                waveShadow = saturate(waveShadow);
                
                // Áp dụng shadow strength
                float shadowFactor = 1.0 - (waveShadow * _WaveShadowStrength);
                
                // Kết hợp tất cả các yếu tố lighting
                col.rgb *= _Brightness * ambientOcclusion * shadowFactor;
                
                return col;
            }
            ENDCG
        }
    }
    
    FallBack "Diffuse"
}
