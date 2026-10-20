// Simple transparent tint for window glass and water.
Shader "Ashfall/Glass"
{
    Properties { _Color ("Tint", Color) = (0.6,0.75,0.85,0.35) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Lambert alpha:fade
        fixed4 _Color;
        struct Input { float4 color : COLOR; };
        void surf (Input IN, inout SurfaceOutput o) { o.Albedo = _Color.rgb * IN.color.rgb; o.Alpha = _Color.a * IN.color.a; }
        ENDCG
    }
    Fallback "Transparent/Diffuse"
}
