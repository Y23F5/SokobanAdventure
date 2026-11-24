/**
 * @file RoomMist.shader
 * @brief 按房间平面坐标生成软边云层；以进入格为圆心扩大透明区。
 */
Shader "Sokoban/RoomMist" {
Properties { _Reveal("Reveal",Range(0,1))=0 _Entry("Entry",Vector)=(0.5,0.5,0,0) }
SubShader { Tags { "Queue"="Transparent+20" "RenderType"="Transparent" } Pass {
Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
struct Out { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
Out vert(appdata_base v) { Out o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.texcoord.xy; return o; }
float _Reveal; float4 _Entry;
float noise(float2 p) { float2 i=floor(p), f=frac(p); f=f*f*(3-2*f); float4 h=frac(sin(dot(i,float2(127.1,311.7))+float4(0,127.1,311.7,438.8))*43758.55); return lerp(lerp(h.x,h.y,f.x),lerp(h.z,h.w,f.x),f.y); }
fixed4 frag(Out i):SV_Target { float2 p=i.uv; float n=noise(p*6)+.5*noise(p*13); float2 edge=min(p,1-p); float a=smoothstep(0,.095,min(edge.x,edge.y)+.04*(n-.8)); float hole=smoothstep(_Reveal*1.8-.13,_Reveal*1.8+.04,distance(_Entry.xy,p)); return fixed4(lerp(float3(.51,.67,.7),float3(.92,.94,.85),saturate(n*.8+.12*p.y)),a*hole); }
ENDCG
} } }
