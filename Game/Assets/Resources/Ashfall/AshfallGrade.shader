// Full-screen colour grade: saturation, contrast, shadow/highlight tint, vignette and a damage flash at the edges.
Shader "Hidden/Ashfall/Grade"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Lift, _Gain, _Flash;
            float _Saturation, _Contrast, _Vignette;
            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv);
                float3 c = src.rgb;
                float l = dot(c, float3(0.299, 0.587, 0.114));
                c = lerp(float3(l, l, l), c, _Saturation);
                c = (c - 0.5) * _Contrast + 0.5;
                c = c * _Gain.rgb + _Lift.rgb * (1 - saturate(l * 2));
                float2 d = i.uv - 0.5; float r = saturate(dot(d, d) * 2.4);
                c *= 1 - _Vignette * r;
                c = lerp(c, _Flash.rgb, _Flash.a * saturate(0.25 + r * 1.2));
                return fixed4(saturate(c), src.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
