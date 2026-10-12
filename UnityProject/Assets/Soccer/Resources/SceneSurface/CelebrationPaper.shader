Shader "Soccer/Celebration Paper"
{
    Properties { _Color ("Tint",Color)=(1,1,1,1) }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent"}
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard alpha:fade vertex:vert
        #pragma target 3.0
        struct Input { float4 paperColor; };
        fixed4 _Color;
        void vert(inout appdata_full v,out Input o) { UNITY_INITIALIZE_OUTPUT(Input,o);o.paperColor=v.color; }
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            fixed4 c=IN.paperColor*_Color;
            o.Albedo=c.rgb;o.Emission=c.rgb*.22;o.Metallic=.05;o.Smoothness=.15;o.Alpha=c.a;
        }
        ENDCG
    }
    FallBack "Transparent/VertexLit"
}
