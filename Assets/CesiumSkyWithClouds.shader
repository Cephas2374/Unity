// Procedural skybox with atmospheric scattering and volumetric-style clouds.
//
// Inspired by UE5's VolumetricCloudComponent, adapted for Unity's built-in
// render pipeline and optimised for HoloLens 2 (Qualcomm Adreno 630).
//
// UE5 uses full 3D ray-marching through a density volume — far too heavy for
// mobile AR.  This shader instead projects domain-warped fractal noise onto a
// flat cloud plane, giving a convincing volumetric look at a fraction of the cost
// (3 × 3-octave FBM = ~36 ALU-heavy samples per pixel, well within budget for a
// single skybox pass on Adreno 630).
//
// Features:
//   • Rayleigh-like gradient atmosphere (zenith → horizon → ground)
//   • Sun disk + glow aligned to the scene's directional light
//   • Sunset/sunrise tinting near the horizon
//   • Animated cloud layer with domain-warped FBM noise
//   • Cloud lighting: sun-facing highlights, self-shadow, edge scattering
//   • Stereo-aware for single-pass instanced rendering (HoloLens 2)
//
// Usage:
//   1. Create a Material using this shader
//   2. Assign it to Lighting > Environment > Skybox Material
//   3. Tweak properties in the Material Inspector
//   (Auto-setup: CesiumSkySetup.cs does steps 1–2 automatically)

Shader "Skybox/CesiumSkyWithClouds"
{
    Properties
    {
        [Header(Atmosphere)]
        _SkyTopColor    ("Sky Zenith",  Color) = (0.18, 0.34, 0.76, 1)
        _SkyHorizonColor("Horizon",     Color) = (0.60, 0.75, 0.95, 1)
        _GroundColor    ("Ground",      Color) = (0.37, 0.35, 0.34, 1)
        _Exposure       ("Exposure",    Range(0.5, 4)) = 1.3

        [Header(Sun)]
        _SunColor    ("Sun Color",    Color) = (1, 0.95, 0.84, 1)
        _SunSize     ("Disk Size",    Range(0.001, 0.1)) = 0.03
        _SunGlow     ("Glow",         Range(0, 5)) = 2.0

        [Header(Volumetric Clouds)]
        _CloudColor    ("Lit Color",     Color) = (1, 1, 1, 1)
        _CloudShadow   ("Shadow Color",  Color) = (0.55, 0.58, 0.68, 1)
        _CloudCoverage ("Coverage",      Range(0, 1)) = 0.50
        _CloudSoftness ("Edge Softness", Range(0.01, 0.5)) = 0.12
        _CloudSpeed    ("Wind Speed",    Range(0, 0.3)) = 0.02
        _CloudScale    ("Scale",         Range(5, 80)) = 25
        _CloudAltitude ("Altitude",      Range(0.02, 0.5)) = 0.15
        _CloudOpacity  ("Opacity",       Range(0, 1)) = 0.90
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            // ---- Properties ----
            half4 _SkyTopColor, _SkyHorizonColor, _GroundColor;
            half  _Exposure;
            half4 _SunColor;
            half  _SunSize, _SunGlow;
            half4 _CloudColor, _CloudShadow;
            half  _CloudCoverage, _CloudSoftness, _CloudSpeed;
            half  _CloudScale, _CloudAltitude, _CloudOpacity;

            // ---- Structs ----
            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            // ================================================================
            //  Noise — hash-based value noise, compiles well on Adreno 630
            // ================================================================
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);  // smoothstep
                return lerp(
                    lerp(hash21(i),                hash21(i + float2(1, 0)), f.x),
                    lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x),
                    f.y);
            }

            // 3-octave FBM — sweet spot for HoloLens 2 perf vs. quality
            float fbm3(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    v += a * vnoise(p);
                    p  = p * 2.03 + float2(100.0, 100.0);
                    a *= 0.5;
                }
                return v;
            }

            // ================================================================
            //  Fragment
            // ================================================================
            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.dir);
                float  y = d.y;

                // ---- Atmosphere gradient ----
                half3 sky;
                if (y >= 0.0)
                    sky = lerp(_SkyHorizonColor.rgb, _SkyTopColor.rgb, saturate(pow(y, 0.45)));
                else
                    sky = lerp(_SkyHorizonColor.rgb, _GroundColor.rgb, saturate(pow(-y, 0.45)));

                // ---- Sun ----
                float3 sunDir = _WorldSpaceLightPos0.xyz;
                float  sd     = dot(d, sunDir);

                // Sharp disk
                float disk = smoothstep(1.0 - _SunSize * 0.01, 1.0, sd);
                // Soft glow
                float glow = pow(saturate(sd), 8.0) * _SunGlow * 0.08;
                // Sunset / sunrise band near horizon
                float sunset = pow(saturate(1.0 - abs(y)) * saturate(sd * 0.5 + 0.5), 2.0);

                sky += _SunColor.rgb * (glow + sunset * 0.25);
                sky += _SunColor.rgb * disk * 4.0;

                // ---- Volumetric-style clouds (above horizon only) ----
                if (y > 0.005)
                {
                    // Project view ray onto flat cloud plane at _CloudAltitude
                    float2 uv = d.xz / (y + _CloudAltitude) * _CloudScale;
                    float  t  = _Time.y * _CloudSpeed;

                    // Domain-warped FBM: 2 warp passes + 1 shape = 3 × fbm3
                    //   Gives swirling, cumulus-like shapes without 3D ray marching.
                    float2 q = float2(
                        fbm3(uv + float2(t,            0.0)),
                        fbm3(uv + float2(5.2, 1.3 + t * 0.4)));

                    float n = fbm3(uv + 3.0 * q);

                    // Coverage threshold + softness
                    float dens = saturate((n - (1.0 - _CloudCoverage)) / _CloudSoftness);

                    if (dens > 0.001)
                    {
                        // Cloud lighting — brighter faces toward sun
                        float lit = saturate(sunDir.y * 0.5 + 0.7);
                        // Edge scattering — thin edges glow
                        lit = lerp(lit, 1.0, pow(1.0 - dens, 2.0) * 0.3);

                        half3 cc = lerp(_CloudShadow.rgb, _CloudColor.rgb, lit);
                        // Sunset warm tint on clouds
                        cc = lerp(cc, cc * _SunColor.rgb, sunset * 0.4);

                        // Opacity: density × master opacity, fade near horizon to avoid cutoff
                        float alpha = saturate(dens * _CloudOpacity * 2.0)
                                    * smoothstep(0.005, 0.12, y);

                        sky = lerp(sky, cc, alpha);
                    }
                }

                return half4(sky * _Exposure, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
