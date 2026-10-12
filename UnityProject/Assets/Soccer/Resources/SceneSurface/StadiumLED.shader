Shader "Soccer/Stadium LED"
{
    Properties
    {
        _TeamColor ("Team color", Color) = (0.18,0.84,0.69,1)
        _ArenaTime ("Presentation time", Float) = 0
        _Celebration ("Goal celebration", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
            fixed4 _TeamColor;
            float _ArenaTime, _Celebration;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                float arrow=step(.64,frac(i.uv.x*7-_ArenaTime*.9+abs(i.uv.y-.5)*1.65));
                float grid=1-smoothstep(.38,.5,length(frac(i.uv*float2(128,12))-.5));
                float edge=smoothstep(0,.15,i.uv.y)*smoothstep(0,.15,1-i.uv.y);
                fixed3 team=lerp(_TeamColor.rgb,fixed3(1,.78,.30),_Celebration);
                fixed3 color=lerp(fixed3(.025,.075,.085),team,arrow*grid*edge*.85);
                color+=team*.09*(.5+.5*sin(i.uv.x*20-_ArenaTime*2));
                return fixed4(color,1);
            }
            ENDCG
        }
    }
    FallBack "Unlit/Color"
}
