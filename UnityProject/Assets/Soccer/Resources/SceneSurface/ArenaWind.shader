Shader "Soccer/Arena Wind"
{
    Properties
    {
        _Color ("Color", Color) = (0.15,0.75,0.65,1)
        _MainTex ("UV reference", 2D) = "white" {}
        _ArenaTime ("Presentation time", Float) = 0
        _WindStrength ("Wind strength", Float) = 0.14
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert
        #pragma target 3.0
        struct Input { float2 uv_MainTex; };
        fixed4 _Color;
        float _ArenaTime, _WindStrength;
        void vert(inout appdata_full v)
        {
            v.vertex.z+=sin(v.vertex.x*4+v.vertex.y*1.4-_ArenaTime*3)*_WindStrength*v.texcoord.x;
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float band=step(.78,IN.uv_MainTex.y);
            o.Albedo=lerp(_Color.rgb,fixed3(.96,.90,.69),band);
            o.Metallic=.03; o.Smoothness=.28; o.Emission=o.Albedo*.04; o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Standard"
}
