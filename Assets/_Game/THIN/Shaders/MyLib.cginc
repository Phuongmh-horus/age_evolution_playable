#ifndef MY_LIB_INCLUDED
#define MY_LIB_INCLUDED

#include "UnityCG.cginc"


// This shader library was created by NXT.

inline float3 hsv_to_rgb(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
}

inline float spr(const float v)
{
    return v * v;
}

inline half3 PerPixelNormal(sampler2D bumpMap, half4 coords, half3 vertexNormal, half bumpStrength)
{
    half3 bump = (UnpackNormal(tex2D(bumpMap, coords.xy)) + UnpackNormal(tex2D(bumpMap, coords.zw))) * 0.5;
    const half3 world_normal = vertexNormal + bump.xxy * bumpStrength * half3(1, 0, 1);
    return normalize(world_normal);
}

#define MIN_REFLECTIVITY 0.04

/**
 * \brief 
 * \param metallic 
 * \return diffuse light's strength
 */
inline float diffuse_light_strength(const float metallic)
{
    const float range = 1.0 - MIN_REFLECTIVITY;
    return range - metallic * range;
}

/**
 * \brief 
 * \param color surface's color
 * \param metallic 
 * \return specular light's color. The higher metallic is, the more output is effected by color
 */
inline float3 specular_light_color(fixed4 color, float metallic)
{
    return lerp(MIN_REFLECTIVITY, color, metallic);
}

/**
 * \brief 
 * \param view_dir direction from surface to camera
 * \param normal surface's normal
 * \param light_dir direction from surface to light
 * \param roughness surface's roughness
 * \return strength of specular light. This value should be multiply by lambert value
 */
inline float specular_light_strength(float3 view_dir, float3 normal, float3 light_dir, float roughness)
{
    float3 h = normalize(light_dir + view_dir);
    float nh2 = spr(saturate(dot(normal, h)));
    float lh2 = spr(saturate(dot(light_dir, h)));
    float r2 = spr(roughness);
    float d2 = spr(nh2 * (r2 - 1.0) + 1.00001);
    float normalization = roughness * 4.0 + 2.0;
    return r2 / (d2 * max(0.1, lh2) * normalization);
}


/**
 * \brief 
 * \param local_pos vertex position in object'space 
 * \param normal normal vector of triangle
 * \return tangent vector at local position of triangle that pointing away from origin 
 */
inline float3 rejection(float3 local_pos, float3 normal)
{
    float3 rejection = local_pos - dot(local_pos, normal) / dot(normal, normal) * normal;
    return rejection;
}

// TODO: Write note for 2 function below, delete all other fresnel functions

inline float fresnel(const float3 normal, const float3 view_dir, const float bias, const float contrast,
                     const float power)
{
    return bias + contrast * pow(1.0 - saturate(dot(normalize(normal), normalize(view_dir))), power);
}

inline float inverse_fresnel(const float3 normal, const float3 view_dir, const float bias, const float contrast,
                             const float power)
{
    return bias + contrast * pow(saturate(dot(normalize(normal), normalize(view_dir))), power);
}

inline float smoother_step(float i, float min, float max)
{
    i = clamp((i - min) / (max - min), 0.0, 1.0);
    return i * i * i * (6 * i * i - 15 * i + 10);
}

/**
 * \brief 
 * \param camera_normal_depth_texture _CameraDepthNormalsTexture 
 * \param screen_uv sample value 
 * \return depth value of _CameraDepthNormalsTexture in screen_uv position (in screen space)
 */
inline float screen_depth(const sampler2D camera_normal_depth_texture, const float2 screen_uv)
{
    return DecodeFloatRG(tex2D(camera_normal_depth_texture, screen_uv).zw);
}

/**
 * \brief 
 * \param clip_pos_vertex vertex's position in camera's clip space
 * \return uv value of vertex in screen
 */
inline float2 screen_uv(float4 clip_pos_vertex)
{
    float2 o = (clip_pos_vertex.xy / clip_pos_vertex.w + 1) / 2;
    o.y = 1 - o.y;
    return o;
}

/**
 * \brief 
 * \param vertex 
 * \return distance from vertex to camera multiply 1/FarPlane
 */
inline float depth01(float4 vertex)
{
    return -mul(UNITY_MATRIX_MV, vertex).z * _ProjectionParams.w;
}

/**
 * \brief 
 * \param local_pos 
 * \return distance from position to camera multiply 1/FarPlane
 */
inline float depth01(float3 local_pos)
{
    return -UnityObjectToViewPos(local_pos).z * _ProjectionParams.w;
}

inline float remap(float IN, float in_min, float in_max, float out_min, float out_max)
{
    return out_min + (IN - in_min) * (out_max - out_min) / (in_max - in_min);
}

/**
 * \brief 
 * \param IN input
 * \param in_min 
 * \param in_max 
 * \param out_min 
 * \param out_max 
 * \return remap value of IN, the value is clamped between out_min and out_max before being returned
 */
inline float remap_clamp(float IN, float in_min, float in_max, float out_min, float out_max)
{
    const float x = saturate((IN - in_min) / (in_max - in_min));
    return out_min + x * (out_max - out_min);
}

// /**
//  * \brief 
//  * \param vertex position in local space
//  * \return position in camera’s clip space, this billboard function is effected by object's rotation
//  */
// inline float4 billboard(float4 vertex)
// {
//     float3 world_pos = mul((float3x3)unity_ObjectToWorld, vertex.xyz);
//     const float4 world_coord = float4(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23, 1);
//     // = view_origin - world_origin
//     const float4 view_pos = mul(UNITY_MATRIX_V, world_coord) + float4(world_pos, 0);
//     float4 o = mul(UNITY_MATRIX_P, view_pos);
//     return o;
// }

/**
 * \brief x value of object's lossy scale
 */
#define SCALE_X length(float3(unity_ObjectToWorld[0].x, unity_ObjectToWorld[1].x, unity_ObjectToWorld[2].x))

/**
 * \brief y value of object's lossy scale
 */
#define SCALE_Y length(float3(unity_ObjectToWorld[0].y, unity_ObjectToWorld[1].y, unity_ObjectToWorld[2].y))


inline float2 dir_to_rectilinear(float3 view_dir)
{
    float x = atan2(view_dir.z, view_dir.x) / UNITY_TWO_PI + 0.5;
    float y = view_dir.y * 0.5 + 0.5;
    return float2(x, y);
}

/**
 * \brief 
 * \param vertex local position
 * \param scale_x 
 * \param scale_y 
* \return position in camera’s clip space, this billboard function is not effected by object's rotation
 */
inline float4 billboard(float4 vertex, float scale_x, float scale_y)
{
    float4 o = mul(
        UNITY_MATRIX_P,
        mul(UNITY_MATRIX_MV, float4(0.0, 0.0, 0.0, 1.0)) + float4(vertex.x, vertex.y, 0.0, 0.0) * float4(
            scale_x, scale_y, 1.0, 1.0));
    return o;
}

/**
 * \brief 
 * \param IN input
 * \param min minimum out put
 * \param max maximum out put
 * \return wrap mode repeat
 */
inline float wrap(float IN, float min, float max)
{
    const float range = max - min;
    const float remainder = (IN - min) % range;
    if (remainder < 0)
        return max + remainder;
    return min + remainder;
}

inline float2 uv_sheet(float2 uv, float id, float2 sheet)
{
    uv /= sheet;
    const float y = floor(id / sheet.x);
    const float x = id - y * sheet.x;
    uv += float2(x * 1 / sheet.x, 1 - y * 1 / sheet.y);
    return uv;
}

inline float2 uv_rotate(float2 uv, float angle)
{
    const float sin_angle = sin(angle);
    const float cos_angle = cos(angle);

    float2 o;
    o.x = cos_angle * uv.x - sin_angle * uv.y;
    o.y = sin_angle * uv.x + cos_angle * uv.y;

    return o;
}

inline float rand(float3 co)
{
    return frac(sin(dot(co.xyz, float3(12.9898, 78.233, 45.5432))) * 43758.5453);
}

float2 hash(float2 n) { return frac(sin(n) * 1399763.5453123); }

float2 grad(float2 n, float2 p)
{
    float2 r = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return -1.0 + 2.0 * frac(sin(r + n) * 43758.5453123);
}

float noise(float2 x)
{
    float2 n = float2(dot(x, float2(127.1, 311.7)), dot(x, float2(269.5, 183.3)));
    float2 f = frac(x * 0.1031);
    f = f * f * (3.0 - 2.0 * f);
    float2 vs = float2(0.0, 1.0);
    float2 g = float2(0.0, 0.0);
    g.x = dot(grad(n, vs), f);
    vs = float2(1.0, 0.0);
    g.y = dot(grad(n, vs), f);
    return dot(g, float2(0.25, 0.25));
}

inline float unity_noise_randomValue(float2 uv)
{
    return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
}

inline float unity_noise_interpolate(float a, float b, float t)
{
    return (1.0 - t) * a + (t * b);
}

inline float unity_valueNoise(float2 uv)
{
    float2 i = floor(uv);
    float2 f = frac(uv);
    f = f * f * (3.0 - 2.0 * f);

    uv = abs(frac(uv) - 0.5);
    float2 c0 = i + float2(0.0, 0.0);
    float2 c1 = i + float2(1.0, 0.0);
    float2 c2 = i + float2(0.0, 1.0);
    float2 c3 = i + float2(1.0, 1.0);
    float r0 = unity_noise_randomValue(c0);
    float r1 = unity_noise_randomValue(c1);
    float r2 = unity_noise_randomValue(c2);
    float r3 = unity_noise_randomValue(c3);

    float bottomOfGrid = unity_noise_interpolate(r0, r1, f.x);
    float topOfGrid = unity_noise_interpolate(r2, r3, f.x);
    float t = unity_noise_interpolate(bottomOfGrid, topOfGrid, f.y);
    return t;
}

inline float simple_noise(float2 UV, float Scale)
{
    float t = 0.0;

    float freq = pow(2.0, float(0));
    float amp = pow(0.5, float(3 - 0));
    t += unity_valueNoise(float2(UV.x * Scale / freq, UV.y * Scale / freq)) * amp;

    freq = pow(2.0, float(1));
    amp = pow(0.5, float(3 - 1));
    t += unity_valueNoise(float2(UV.x * Scale / freq, UV.y * Scale / freq)) * amp;

    freq = pow(2.0, float(2));
    amp = pow(0.5, float(3 - 2));
    t += unity_valueNoise(float2(UV.x * Scale / freq, UV.y * Scale / freq)) * amp;

    return t;
}

inline float simple_noise(float2 uv, float2 scale)
{
    float t = 0.0;

    float freq = pow(2.0, float(0));
    float amp = pow(0.5, float(3 - 0));
    t += unity_valueNoise(uv * scale / freq) * amp;

    freq = pow(2.0, float(1));
    amp = pow(0.5, float(3 - 1));
    t += unity_valueNoise(uv * scale / freq) * amp;

    freq = pow(2.0, float(2));
    amp = pow(0.5, float(3 - 2));
    t += unity_valueNoise(uv * scale / freq) * amp;

    return t;
}

// Gradient noise

inline float2 unity_gradientNoise_dir(float2 p)
{
    p = p % 289;
    float x = (34 * p.x + 1) * p.x % 289 + p.y;
    x = (34 * x + 1) * x % 289;
    x = frac(x / 41) * 2 - 1;
    return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
}

inline float unity_gradientNoise(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);
    float d00 = dot(unity_gradientNoise_dir(ip), fp);
    float d01 = dot(unity_gradientNoise_dir(ip + float2(0, 1)), fp - float2(0, 1));
    float d10 = dot(unity_gradientNoise_dir(ip + float2(1, 0)), fp - float2(1, 0));
    float d11 = dot(unity_gradientNoise_dir(ip + float2(1, 1)), fp - float2(1, 1));
    fp = fp * fp * fp * (fp * (fp * 6 - 15) + 10);
    return lerp(lerp(d00, d01, fp.y), lerp(d10, d11, fp.y), fp.x);
}

inline float gradient_noise(float2 UV, float Scale)
{
    return unity_gradientNoise(UV * Scale) + 0.5;
}

// Voronoi noise

inline float2 unity_voronoi_noise_randomVector(float2 UV, float offset)
{
    float2x2 m = float2x2(15.27, 47.63, 99.41, 89.98);
    UV = frac(sin(mul(UV, m)) * 46839.32);
    return float2(sin(UV.y * +offset) * 0.5 + 0.5, cos(UV.x * offset) * 0.5 + 0.5);
}

inline float voronoi_value(float2 UV, float AngleOffset, float CellDensity)
{
    float value = 0;
    float2 g = floor(UV * CellDensity);
    float2 f = frac(UV * CellDensity);
    float t = 8.0;
    float3 res = float3(8.0, 0.0, 0.0);

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 lattice = float2(x, y);
            float2 offset = unity_voronoi_noise_randomVector(lattice + g, AngleOffset);
            float d = distance(lattice + offset, f);
            if (d < res.x)
            {
                res = float3(d, offset.x, offset.y);
                value = res.x;
            }
        }
    }

    return value;
}

inline void voronoi(float2 UV, float AngleOffset, float CellDensity, out float Out, out float Cells)
{
    float2 g = floor(UV * CellDensity);
    float2 f = frac(UV * CellDensity);
    float t = 8.0;
    float3 res = float3(8.0, 0.0, 0.0);

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 lattice = float2(x, y);
            float2 offset = unity_voronoi_noise_randomVector(lattice + g, AngleOffset);
            float d = distance(lattice + offset, f);
            if (d < res.x)
            {
                res = float3(d, offset.x, offset.y);
                Out = res.x;
                Cells = res.y;
            }
        }
    }
}

#endif // MY_LIB_INCLUDED
