// Procedural skybox with realistic volumetric cumulus clouds.
//
// Inspired by UE5's VolumetricCloudComponent. UE5 uses full 3D ray-marching
// through a density volume — too heavy for HoloLens 2 (Adreno 630). This shader
// instead uses multi-layer 2D noise with:
//   • Gradient noise (not hash value noise) for smooth, organic shapes
//   • Worley (cellular) distance for the puffy cumulus bubble look
//   • Multi-scale: large macro shapes modulated by fine detail
//   • Subtle domain warping for natural cloud grouping
//   • Self-shadowing via offset density sampling (fake light extinction)
//   • Slow wind animation
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
        _SkyTopColor    ("Sky Zenith",  Color) = (0.24, 0.44, 0.82, 1)
        _SkyMidColor    ("Sky Mid",     Color) = (0.45, 0.62, 0.90, 1)
        _SkyHorizonColor("Horizon",     Color) = (0.70, 0.82, 0.96, 1)
        _GroundColor    ("Ground",      Color) = (0.37, 0.35, 0.34, 1)
        _HazeColor      ("Haze Color",  Color) = (0.75, 0.83, 0.95, 1)
        _HazeStrength   ("Haze",        Range(0, 1)) = 0.35
        _Exposure       ("Exposure",    Range(0.5, 4)) = 1.2

        [Header(Sun)]
        _SunColor    ("Sun Color",    Color) = (1, 0.96, 0.88, 1)
        _SunSize     ("Disk Size",    Range(0.001, 0.1)) = 0.025
        _SunGlow     ("Glow",         Range(0, 5)) = 1.5

        [Header(Volumetric Clouds)]
        _CloudColor    ("Lit Color",       Color) = (1, 1, 1, 1)
        _CloudShadow   ("Shadow Color",    Color) = (0.58, 0.62, 0.72, 1)
        _CloudAmbient  ("Ambient Color",   Color) = (0.72, 0.76, 0.85, 1)
        _CloudCoverage ("Coverage",        Range(0, 1)) = 0.42
        _CloudSoftness ("Edge Softness",   Range(0.02, 0.6)) = 0.20
        _CloudSpeed    ("Wind Speed",      Range(0, 0.1)) = 0.008
        _CloudScale    ("Scale",           Range(2, 40)) = 8
        _CloudAltitude ("Altitude",        Range(0.02, 0.5)) = 0.12
        _CloudOpacity  ("Max Opacity",     Range(0, 1)) = 0.92
        _CloudDetail   ("Detail Amount",   Range(0, 1)) = 0.45
        _CloudBrightTop("Top Brightness",  Range(0.5, 2)) = 1.3
        _ShadowOffset  ("Shadow Depth",    Range(0.01, 0.3)) = 0.08
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
            half4 _SkyTopColor, _SkyMidColor, _SkyHorizonColor, _GroundColor, _HazeColor;
            half  _HazeStrength, _Exposure;
            half4 _SunColor;
            half  _SunSize, _SunGlow;
            half4 _CloudColor, _CloudShadow, _CloudAmbient;
            half  _CloudCoverage, _CloudSoftness, _CloudSpeed;
            half  _CloudScale, _CloudAltitude, _CloudOpacity;
            half  _CloudDetail, _CloudBrightTop, _ShadowOffset;

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
            //  Noise functions — gradient noise + Worley for cumulus shapes
            // ================================================================

            // Smooth 2D hash returning float2 gradient direction
            float2 hash22(float2 p)
            {
                float3 q = float3(
                    dot(p, float2(127.1, 311.7)),
                    dot(p, float2(269.5, 183.3)),
                    dot(p, float2(419.2, 371.9)));
                return frac(sin(q.xy) * 43758.5453) * 2.0 - 1.0;
            }

            // Scalar hash
            float hash21(float2 p)
            {
                p = frac(p * float2(443.8975, 397.2973));
                p += dot(p.xy, p.yx + 19.19);
                return frac(p.x * p.y);
            }

            // Gradient (Perlin-like) noise — much smoother than value noise
            float gnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                // Quintic interpolation for C2 continuity (no grid artifacts)
                float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

                float a = dot(hash22(i + float2(0, 0)), f - float2(0, 0));
                float b = dot(hash22(i + float2(1, 0)), f - float2(1, 0));
                float c = dot(hash22(i + float2(0, 1)), f - float2(0, 1));
                float d = dot(hash22(i + float2(1, 1)), f - float2(1, 1));

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y) * 0.5 + 0.5;
            }

            // Worley (cellular) noise — returns distance to nearest cell center
            // Creates the puffy, bubble-like cumulus cloud shapes
            float worley(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float minDist = 1.0;
                // Check 3x3 neighborhood
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 neighbor = float2(x, y);
                        float2 cellCenter = hash22(i + neighbor) * 0.5 + 0.5;
                        float2 diff = neighbor + cellCenter - f;
                        float dist = dot(diff, diff);
                        minDist = min(minDist, dist);
                    }
                }
                return sqrt(minDist);
            }

            // FBM with gradient noise — 5 octaves for large-scale shape
            float fbmShape(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                float2x2 rot = float2x2(0.8, -0.6, 0.6, 0.8); // Rotate each octave
                for (int i = 0; i < 5; i++)
                {
                    v += a * gnoise(p);
                    p = mul(rot, p) * 2.02;
                    a *= 0.50;
                }
                return v;
            }

            // FBM detail — 3 octaves, higher frequency, for edge detail
            float fbmDetail(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                float2x2 rot = float2x2(0.7, -0.71, 0.71, 0.7);
                for (int i = 0; i < 3; i++)
                {
                    v += a * gnoise(p);
                    p = mul(rot, p) * 2.1;
                    a *= 0.45;
                }
                return v;
            }

            // ================================================================
            //  Cloud density function — the core of the volumetric look
            // ================================================================
            float cloudDensity(float2 uv, float t)
            {
                // === Layer 1: Large-scale shape (macro cumulus formations) ===
                float2 p = uv + float2(t * 0.7, t * 0.3);
                float shape = fbmShape(p);

                // === Layer 2: Worley for puffy bubble sub-structure ===
                // Inverted worley: 1-worley gives round bubble shapes
                float bubbles = 1.0 - worley(uv * 2.5 + float2(t * 0.4, t * 0.15));
                bubbles = bubbles * bubbles; // Sharpen the bubbles

                // === Layer 3: Fine detail for fluffy edges ===
                float detail = fbmDetail(uv * 4.0 + float2(t * 1.2, -t * 0.5));

                // Combine: shape defines where clouds are, bubbles add puffiness,
                // detail adds fluffy edges and internal texture
                float combined = shape * 0.65 + bubbles * 0.25 + detail * _CloudDetail * 0.35;

                // === Subtle domain warp for natural grouping ===
                // Offset UV by noise to break up regularity
                float2 warp = float2(
                    gnoise(uv * 0.5 + float2(t * 0.2, 100.0)),
                    gnoise(uv * 0.5 + float2(200.0, t * 0.15)));
                float warped = fbmShape(p + warp * 0.8);
                combined = lerp(combined, warped * 0.65 + bubbles * 0.25, 0.3);

                // === Coverage threshold with soft edges ===
                float threshold = 1.0 - _CloudCoverage;
                float dens = saturate((combined - threshold) / _CloudSoftness);

                // Soften edges further for fluffy look
                dens = smoothstep(0.0, 1.0, dens);

                return dens;
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
                // 3-stop gradient: horizon → mid → zenith for richer blue
                half3 sky;
                if (y >= 0.0)
                {
                    float t1 = saturate(pow(y, 0.35));        // horizon → mid
                    float t2 = saturate(pow(y, 0.8));         // mid → zenith
                    sky = lerp(_SkyHorizonColor.rgb, _SkyMidColor.rgb, t1);
                    sky = lerp(sky, _SkyTopColor.rgb, t2);
                }
                else
                {
                    sky = lerp(_SkyHorizonColor.rgb, _GroundColor.rgb, saturate(pow(-y, 0.45)));
                }

                // Atmospheric haze near horizon
                float hazeAmount = pow(saturate(1.0 - abs(y)), 3.0) * _HazeStrength;
                sky = lerp(sky, _HazeColor.rgb, hazeAmount);

                // ---- Sun ----
                float3 sunDir = _WorldSpaceLightPos0.xyz;
                float  sd     = dot(d, sunDir);

                // Sharp disk with soft edge
                float disk = smoothstep(1.0 - _SunSize * 0.008, 1.0 - _SunSize * 0.001, sd);
                // Soft glow halo
                float glow = pow(saturate(sd), 12.0) * _SunGlow * 0.06;
                // Wider subtle glow
                float wideGlow = pow(saturate(sd * 0.5 + 0.5), 4.0) * 0.03;
                // Sunset / sunrise band near horizon
                float sunset = pow(saturate(1.0 - abs(y)) * saturate(sd * 0.5 + 0.5), 2.5);

                sky += _SunColor.rgb * (glow + wideGlow + sunset * 0.2);
                sky += _SunColor.rgb * disk * 3.0;

                // ---- Volumetric cumulus clouds ----
                if (y > 0.003)
                {
                    // Project view ray onto flat cloud plane
                    float2 uv = d.xz / (y + _CloudAltitude) * _CloudScale;
                    float  t  = _Time.y * _CloudSpeed;

                    // Main cloud density
                    float dens = cloudDensity(uv, t);

                    if (dens > 0.001)
                    {
                        // === Self-shadow: sample density offset toward sun ===
                        // This creates darker cloud bases and brighter tops
                        float2 shadowUV = uv + sunDir.xz * _ShadowOffset * _CloudScale;
                        float shadowDens = cloudDensity(shadowUV, t);
                        float shadow = exp(-shadowDens * 2.5); // Beer's law extinction

                        // === Cloud lighting ===
                        // Direct illumination: brighter where facing sun, darker in shadow
                        float directLight = shadow * saturate(sunDir.y * 0.4 + 0.8);

                        // Ambient: softer lighting from sky
                        float ambient = 0.35;

                        // Silver lining / edge scattering: thin cloud edges glow
                        float edgeGlow = pow(1.0 - dens, 3.0) * 0.4 * saturate(sd * 0.5 + 0.5);

                        // Top brightness: upper parts of clouds are brighter
                        // (approximated by higher density = deeper into cloud = thicker top)
                        float topBright = lerp(1.0, _CloudBrightTop, dens * 0.5);

                        float totalLight = (directLight + ambient + edgeGlow) * topBright;
                        totalLight = saturate(totalLight);

                        // Color: blend between shadow color and lit color based on lighting
                        half3 cc = lerp(_CloudShadow.rgb, _CloudColor.rgb, totalLight);
                        // Mix in ambient color for mid-tones
                        cc = lerp(cc, _CloudAmbient.rgb, (1.0 - totalLight) * 0.3);
                        // Sunset warm tint on clouds
                        cc = lerp(cc, cc * _SunColor.rgb * 1.1, sunset * 0.4);

                        // === Opacity ===
                        // Density × master opacity, S-curve for natural buildup
                        float alpha = smoothstep(0.0, 0.6, dens) * _CloudOpacity;
                        // Fade near horizon to avoid hard cutoff
                        alpha *= smoothstep(0.003, 0.15, y);
                        // Slight fade at very high angles (sky dome edge)
                        alpha *= smoothstep(0.98, 0.85, y);

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
