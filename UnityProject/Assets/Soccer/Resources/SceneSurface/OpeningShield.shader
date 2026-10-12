Shader "Soccer/Opening Shield"
{
    Properties
    {
        _Color ("Shield tint", Color) = (0.35,0.95,0.80,1)
        _ShieldTime ("Presentation time", Float) = 0
        _Opacity ("Fade", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float3 normal:TEXCOORD0; float3 world:TEXCOORD1; float2 uv:TEXCOORD2; };
            fixed4 _Color;
            float _ShieldTime,_Opacity;
            v2f vert(appdata v)
            {
                v2f o; o.vertex=UnityObjectToClipPos(v.vertex);
                o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                o.normal=UnityObjectToWorldNormal(v.normal); o.uv=v.uv; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float rim=pow(1-saturate(dot(normalize(i.normal),normalize(_WorldSpaceCameraPos-i.world))),2.7);
                float2 grid=abs(frac(i.uv*float2(32,16))-.5);
                float gridLine=smoothstep(.45,.49,max(grid.x,grid.y));
                float scan=pow(saturate(1-abs(frac(i.world.y*.7-_ShieldTime*.35)-.5)*8),3);
                fixed3 color=lerp(_Color.rgb,fixed3(.90,1,.89),rim*.65+scan*.15);
                return fixed4(color,(.035+rim*.43+gridLine*.07+scan*.10)*_Opacity);
            }
            ENDCG
        }
    }
    FallBack "Unlit/Transparent"
}
