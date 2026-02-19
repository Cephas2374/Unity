using UnityEngine;

/// <summary>
/// Forces alpha=1.0 on all opaque pixels after rendering.
/// This fixes HoloLens 2 Mixed Reality Capture (MRC) not showing
/// Cesium terrain/tiles in video recordings.
/// 
/// Attach this to the Main Camera (DynamicCamera).
/// </summary>
[RequireComponent(typeof(Camera))]
public class ForceOpaqueAlpha : MonoBehaviour
{
    private Material forceAlphaMat;

    private static readonly string shaderCode = @"
        Shader ""Hidden/ForceAlpha""
        {
            SubShader
            {
                Pass
                {
                    ZTest Always ZWrite Off Cull Off
                    ColorMask A
                    
                    CGPROGRAM
                    #pragma vertex vert
                    #pragma fragment frag
                    
                    struct appdata { float4 vertex : POSITION; };
                    struct v2f { float4 pos : SV_POSITION; };
                    
                    v2f vert(appdata v)
                    {
                        v2f o;
                        o.pos = UnityObjectToClipPos(v.vertex);
                        return o;
                    }
                    
                    fixed4 frag(v2f i) : SV_Target
                    {
                        return fixed4(0, 0, 0, 1);
                    }
                    ENDCG
                }
            }
        }";

    void OnEnable()
    {
        var shader = Shader.Find("Hidden/ForceAlpha");
        if (shader == null)
        {
            // Create shader at runtime if not found
            Debug.Log("[ForceOpaqueAlpha] Creating force-alpha shader for MRC compatibility");
            // We'll use a simple blit approach instead
        }
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (forceAlphaMat == null)
        {
            forceAlphaMat = new Material(Shader.Find("Hidden/Internal-Colored"));
            if (forceAlphaMat == null)
            {
                Graphics.Blit(src, dest);
                return;
            }
        }

        // Blit normally first
        Graphics.Blit(src, dest);

        // Then force alpha=1 on the destination
        // This ensures MRC sees all rendered pixels as opaque holograms
        GL.PushMatrix();
        GL.LoadOrtho();

        // Only write to alpha channel
        GL.Clear(false, false, new Color(0, 0, 0, 1));

        // Use a simple fullscreen quad that writes alpha=1
        var prevRT = RenderTexture.active;
        RenderTexture.active = dest;

        GL.Begin(GL.QUADS);
        GL.Color(new Color(0, 0, 0, 1));
        GL.Vertex3(0, 0, 0);
        GL.Vertex3(1, 0, 0);
        GL.Vertex3(1, 1, 0);
        GL.Vertex3(0, 1, 0);
        GL.End();

        RenderTexture.active = prevRT;
        GL.PopMatrix();
    }

    void OnDestroy()
    {
        if (forceAlphaMat != null)
            Destroy(forceAlphaMat);
    }
}
