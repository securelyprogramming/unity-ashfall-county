// Vertex-coloured Lambert used for streamed county meshes (ground, walls, roofs, procedural props).
Shader "Ashfall/VertexLit"
{
    Properties
    {
        _MainTex ("Albedo (optional atlas)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Emission ("Self illumination", Range(0,2)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows addshadow
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        half _Emission;
        struct Input { float2 uv_MainTex; float4 color : COLOR; };
        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color * IN.color;
            o.Albedo = c.rgb;
            o.Emission = c.rgb * _Emission;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
