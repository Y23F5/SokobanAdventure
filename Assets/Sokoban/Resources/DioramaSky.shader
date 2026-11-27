/**
 * @file DioramaSky.shader
 * @brief 以观察射线混合环境色、天空亮部和覆盖整个背景下方的远处薄雾。
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
fixed4 frag(V i):SV_Target {
    float3 direction = normalize(i.dir);
    float lift = smoothstep(-1, .4, direction.y);
    float haze = 1 - smoothstep(-.65, .12, direction.y);
    float3 sky = lerp(_Tint.rgb * .90, lerp(_Tint.rgb, float3(1,.97,.89), .4), lift);
    float3 mist = lerp(_Tint.rgb, float3(.95,.96,.91), .33);
    return fixed4(lerp(sky, mist, haze * .72), 1);
}
ENDCG
} } }
