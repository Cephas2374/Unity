// Forces alpha=1.0 on every pixel — used by ForceOpaqueAlpha.cs for MRC on HoloLens 2.
//
// HoloLens 2 MRC composites holograms over the real-world camera feed using the alpha
// channel. Cesium terrain and other shaders may write alpha=0/alpha<1 for opaque geometry,
// making content invisible in MRC recordings.
//
// KEY DESIGN: Uses ColorMask A — writes ONLY to the alpha channel.
//   • RGB values from scene rendering are untouched (no source texture read needed)
//   • No temporary RenderTexture needed (avoids stereo texture array issues)
//   • Works perfectly with single-pass instanced stereo rendering (HoloLens 2)
//   • Called via CommandBuffer.Blit with a dummy source texture
//
// Stereo-aware: supports single-pass instanced rendering used by HoloLens 2.

Shader "Hidden/ForceAlphaOnly"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {} // Required by Blit API, not sampled
    }
    SubShader
    {
        Tags { "RenderType"="Overlay" }

        Pass
        {
            ZTest Always
            Cull Off
            ZWrite Off
            ColorMask A // CRITICAL: writes ONLY alpha channel, preserves RGB

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return fixed4(0, 0, 0, 1); // Only alpha=1 is written (ColorMask A)
            }
            ENDCG
        }
    }
    Fallback Off
}
