Shader "Cesium/VertexColoredBuilding"
{
    Properties
    {
        _Color ("Base Color", Color) = (1,1,1,1)
        _MainTex ("Base Texture", 2D) = "white" {}
        _UseVertexColor ("Use Vertex Color", Range(0,1)) = 1.0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        float _UseVertexColor;
        half _Glossiness;
        half _Metallic;

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Sample texture and multiply with base color
            fixed4 texColor = tex2D(_MainTex, IN.uv_MainTex);
            fixed4 baseColor = _Color * texColor;
            
            // Blend between texture/base color and vertex color
            fixed4 finalColor = lerp(baseColor, IN.color, _UseVertexColor);
            
            o.Albedo = finalColor.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = finalColor.a;
        }
        ENDCG
    }
    
    FallBack "Standard"
}
