// Artillery red zone: a ground quad around the camera focus; the circle and its edge ring are computed per pixel
// from the zone centre and radius, so any zone size draws exactly without a mesh rebuild.
Shader "Ashfall/Zone"
{
    Properties
    {
        _Color ("Fill", Color) = (1,0.12,0.06,0.2)
        _Center ("Center (x, z)", Vector) = (0,0,0,0)
        _Radius ("Radius", Float) = 50
        _Edge ("Edge width", Float) = 1.6
        _Pulse ("Pulse", Range(0,1)) = 0
        _Hatch ("Hatch", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            fixed4 _Color; float4 _Center; float _Radius, _Edge, _Pulse, _Hatch;
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; UNITY_FOG_COORDS(1) };
            v2f vert (float4 vertex : POSITION)
            {
                v2f o; o.pos = UnityObjectToClipPos(vertex); o.world = mul(unity_ObjectToWorld, vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos); return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float d = distance(i.world.xz, _Center.xy);
                float inside = 1 - smoothstep(_Radius - 0.3, _Radius + 0.3, d);
                float ring = 1 - saturate(abs(d - _Radius) / max(0.1, _Edge));
                float stripes = step(0.5, frac((i.world.x + i.world.z) * 0.125 + _Time.y * 0.3)) * _Hatch;
                float a = inside * (_Color.a * (0.55 + 0.45 * _Pulse) + stripes * 0.1) + ring * (0.55 + 0.35 * _Pulse);
                fixed4 c = fixed4(_Color.rgb * (1 + ring * 0.5), saturate(a));
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
