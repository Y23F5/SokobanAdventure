/**
 * @file RoomMist.shader
 * @brief 云层从房间边界向外按世界尺度羽化，并以入口为圆心揭雾。颜色与天空主题同步。
 */
Shader "Sokoban/RoomMist" {
Properties {
    _Reveal("Reveal",Range(0,1))=0
    _Entry("Entry",Vector)=(0.5,0.5,0,0)
    _MistSize("World size and margin",Vector)=(12,12,2.5,0)
    _Tint("Sky tint",Color)=(.8,.76,.63,1)
}
SubShader { Tags { "Queue"="Transparent+20" "RenderType"="Transparent" } Pass {
Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
struct Out { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
Out vert(appdata_base v) { Out o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.texcoord.xy; return o; }
float _Reveal;
float4 _Entry, _MistSize, _Tint;
float hash(float2 c) { return frac(sin(dot(c,float2(127.1,311.7)))*43758.55); }
float noise(float2 p) { float2 i=floor(p), f=frac(p); f=f*f*(3-2*f); return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+float2(1,1)),f.x),f.y); }
fixed4 frag(Out i):SV_Target {
    float2 p = i.uv;
    float2 world = p * _MistSize.xy;
    float clouds = noise(world * .42) + .5 * noise(world * .91);
    float2 edge = min(p, 1 - p) * _MistSize.xy;
    float feather = smoothstep(0, _MistSize.z, max(0, min(edge.x, edge.y) * lerp(.75, 1.45, saturate(clouds)) - .5));
    float hole = smoothstep(_Reveal * 1.8 - .13, _Reveal * 1.8 + .04, distance(_Entry.xy, p));
    float3 color = lerp(_Tint.rgb * .83, lerp(_Tint.rgb, float3(.95,.96,.91), .7), saturate(clouds * .75));
    return fixed4(color, feather * hole);
}
ENDCG
} } }
