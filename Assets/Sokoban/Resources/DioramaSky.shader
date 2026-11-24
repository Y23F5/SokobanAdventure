/**
 * @file DioramaSky.shader
 * @brief 以观察射线的仰角混合当前房间的环境色与上下两端的明暗色。
 */
Shader "Sokoban/DioramaSky" {
Properties { _Tint("Room color",Color)=(.8,.76,.63,1) }
SubShader { Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" } Cull Off ZWrite Off Pass {
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
struct V { float4 pos:SV_POSITION; float3 dir:TEXCOORD0; };
V vert(appdata_base v) { V o; o.pos=UnityObjectToClipPos(v.vertex); o.dir=v.vertex.xyz; return o; }
float4 _Tint;
fixed4 frag(V i):SV_Target { float a=saturate(normalize(i.dir).y*.85+.98); return fixed4(lerp(_Tint.rgb*.78,lerp(_Tint.rgb,float3(1,.97,.89),.4),a),1); }
ENDCG
} } }
