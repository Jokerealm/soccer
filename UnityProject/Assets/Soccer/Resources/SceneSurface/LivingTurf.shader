Shader "Soccer/Living Turf"
{
    Properties
    {
        _LightColor ("Soft sage turf", Color) = (0.255,0.365,0.285,1)
        _DarkColor ("Olive moss turf", Color) = (0.205,0.305,0.245,1)
        _ArenaTime ("Presentation time", Float) = 0
        _MainTex ("HD cultivated grass", 2D) = "white" {}
        _FieldSize ("Playable dimensions and center radius", Vector) = (56,34,4.2,0)
        _TileSize ("World meters per texture tile", Float) = 4.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf StandardSpecular fullforwardshadows
        #pragma target 3.0
        struct Input { float3 worldPos; };
        fixed4 _LightColor, _DarkColor;
        float _ArenaTime,_TileSize;
        float4 _FieldSize;
        float4 _Footprints[16];
        sampler2D _MainTex;
        float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
        float noise(float2 p)
        {
            float2 i=floor(p),f=frac(p); f=f*f*(3-2*f);
            return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
        }
        float luminance(float3 c) { return dot(c,float3(.2126,.7152,.0722)); }
        fixed3 grassTile(float2 uv)
        {
            // 四偏移混合隐藏生成素材边缘，也消除明显的重复块。
            float2 blend=.5-.5*cos(uv*6.2831853);
            fixed3 a=tex2D(_MainTex,uv).rgb,b=tex2D(_MainTex,uv+float2(.5,0)).rgb;
            fixed3 c=tex2D(_MainTex,uv+float2(0,.5)).rgb,d=tex2D(_MainTex,uv+.5).rgb;
            return lerp(lerp(d,c,blend.x),lerp(b,a,blend.x),blend.y);
        }
        void surf(Input IN, inout SurfaceOutputStandardSpecular o)
        {
            float2 p=IN.worldPos.xz;
            float2 uv=p/_TileSize;
            float detail=luminance(grassTile(uv));
            float macro=noise(p*.19)*.60+noise(p*.67)*.25+noise(p*1.7)*.15;
            float fibers=saturate((detail-.12)*2.4);
            fixed3 grass=lerp(_DarkColor.rgb,_LightColor.rgb,macro)*(.82+fibers*.34);
            // 踩踏与门前磨损只有柔和色差，没有大片反光。
            float2 goal=float2((abs(p.x)-_FieldSize.x*.5+1.8)/4,p.y/3.2);
            float wear=exp(-dot(goal,goal)*1.5)*smoothstep(.30,.78,noise(p*1.8))*.27;
            grass=lerp(grass,fixed3(.31,.30,.235),wear);
            float center=1-smoothstep(_FieldSize.z-.03,_FieldSize.z+.03,length(p));
            grass=lerp(grass,grass*fixed3(1.15,1.035,1.08),center);
            float compression=0;
            [unroll] for(int i=0;i<16;i++)
            {
                float2 q=p-_Footprints[i].xy;
                float sn=sin(_Footprints[i].w),cs=cos(_Footprints[i].w);
                float2 stamp=float2(q.x*cs+q.y*sn,-q.x*sn+q.y*cs)/float2(.085,.17);
                compression+=exp(-dot(stamp,stamp)*2)*_Footprints[i].z;
            }
            grass=lerp(grass,grass*.74,saturate(compression)*.55);
            float e=.008;
            float dx=luminance(grassTile(uv+float2(e,0)))-detail;
            float dy=luminance(grassTile(uv+float2(0,e)))-detail;
            float wind=sin(p.x*.48+p.y*.63-_ArenaTime*1.1)*.008;
            o.Albedo=grass;
            o.Normal=normalize(float3(-dx*.36+wind,-dy*.36,1));
            o.Specular=.015;
            o.Smoothness=.07;
            o.Emission=grass*.025;
            o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Standard"
}
