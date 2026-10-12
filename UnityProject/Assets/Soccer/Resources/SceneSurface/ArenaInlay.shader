Shader "Soccer/Arena Inlay"
{
    Properties
    {
        _Color ("Surface tint", Color) = (.4,.4,.4,1)
        _Metallic ("Metal content", Range(0,1)) = .2
        _Emission ("Recessed light", Color) = (0,0,0,0)
        _ArenaTime ("Presentation time", Float) = 0
        _Flow ("Subtle light flow", Float) = 0
    }
    SubShader
    {
        Tags {"RenderType"="Opaque"}
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        struct Input { float3 worldPos; };
        fixed4 _Color,_Emission;
        float _Metallic,_ArenaTime,_Flow;
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            float2 p=IN.worldPos.xz;
            float brushed=sin(p.x*320+sin(p.y*73))*sin(p.y*180)*.028;
            o.Albedo=_Color.rgb*(.98+brushed);
            o.Normal=normalize(float3(brushed*.08,brushed*.04,1));
            o.Metallic=_Metallic;o.Smoothness=.23;
            float pulse=lerp(1,.94+.06*sin(p.x*.6+p.y*.8-_ArenaTime*1.2),_Flow);
            o.Emission=_Emission.rgb*pulse;o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Standard"
}
