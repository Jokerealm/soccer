using System.Collections.Generic;
using Soccer.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soccer.Runtime
{
    /// <summary>中场纹章、嵌入饰条与立体场边结构。所有结构只参与美术，不改变可跑动区域。</summary>
    public sealed class ArenaDetailView : MonoBehaviour
    {
        public float CenterRadius { get; private set; }
        public int InlayCount { get; private set; }
        private readonly List<Material> materials=new List<Material>();
        private readonly List<Mesh> meshes=new List<Mesh>();
        private Material graphite,ceramic,champagne,mint,amber,cyanPaint,amberPaint;
        private readonly Dictionary<Vector3,Mesh> boxes=new Dictionary<Vector3,Mesh>();
        public void Initialize(MatchRules rules)
        {
            CenterRadius=rules.CenterCircleRadius;
            graphite=Surface("Graphite stadium composite",new Color(.095f,.15f,.16f),.22f);
            ceramic=Surface("Warm stone stadium tiles",new Color(.40f,.44f,.41f),.04f);
            champagne=Surface("Brushed champagne inlay",new Color(.75f,.63f,.40f),.38f);
            mint=Surface("Cyan embedded light",new Color(.27f,.67f,.58f),.08f,new Color(.10f,.29f,.21f));
            amber=Surface("Amber embedded light",new Color(.76f,.51f,.23f),.08f,new Color(.29f,.14f,.04f));
            cyanPaint=Surface("Muted cyan field enamel",new Color(.15f,.35f,.33f),.02f);
            amberPaint=Surface("Muted amber field enamel",new Color(.42f,.34f,.22f),.02f);
            CenterInlay(); Perimeter(rules); FieldStencils(rules);
        }
        private void CenterInlay()
        {
            float r=CenterRadius;
            Arc("Recessed center surround",Vector2.zero,r-.27f,r-.05f,0,Mathf.PI*2,graphite);
            Arc("Inner champagne hairline",Vector2.zero,r-.28f,r-.245f,0,Mathf.PI*2,champagne);
            Arc("Outer champagne hairline",Vector2.zero,r-.07f,r-.04f,0,Mathf.PI*2,champagne);
            for(int i=0;i<32;i++)
            {
                float a=i*Mathf.PI*2/32;
                Arc("Segmented center energy inlay",Vector2.zero,r-.19f,r-.125f,a+.027f,.11f,
                    Mathf.Cos(a)<0 ? mint : amber);
            }
            // 独立地面纹章，实际足球只有这个标记的很小一部分大小。
            Arc("Center emblem medallion",Vector2.zero,0,r*.29f,0,Mathf.PI*2,graphite);
            Arc("Medallion trim",Vector2.zero,r*.285f,r*.295f,0,Mathf.PI*2,champagne);
            float s=r/4.2f;
            Polygon("Lightning crest",champagne,new Vector2(-.06f,1.02f)*s,new Vector2(-.67f,-.12f)*s,
                new Vector2(-.04f,-.12f)*s,new Vector2(-.19f,-1.05f)*s,new Vector2(.67f,.15f)*s,new Vector2(.08f,.15f)*s);
            for(int side=-1;side<=1;side+=2)
            {
                for(int layer=0;layer<3;layer++)
                {
                    float z=(-.95f+layer*.78f)*s;
                    Polygon("Team wing engraving",side<0 ? cyanPaint : amberPaint,
                        new Vector2(side*1.18f*s,z),new Vector2(side*2.85f*s,z+.15f*s),
                        new Vector2(side*3.13f*s,z+.43f*s),new Vector2(side*1.42f*s,z+.28f*s));
                }
                Arc("Crest team arc",Vector2.zero,r*.61f,r*.625f,side<0 ? Mathf.PI*.64f : -Mathf.PI*.36f,Mathf.PI*.72f,
                    side<0 ? cyanPaint : amberPaint);
            }
        }
        private void Perimeter(MatchRules rules)
        {
            float w=rules.FieldWidth*.5f,h=rules.FieldHeight*.5f,g=rules.GoalWidth*.5f;
            for(int side=-1;side<=1;side+=2)
            {
                var team=side<0 ? mint : amber;
                Box("Sideline stone promenade",new Vector3(0,-.035f,side*(h+1.2f)),new Vector3(w*2+2,.07f,1.25f),ceramic);
                Box("Raised beveled sideline curb",new Vector3(0,.018f,side*(h+.42f)),new Vector3(w*2,.13f,.40f),graphite);
                Box("Sideline champagne lip",new Vector3(0,.092f,side*(h+.30f)),new Vector3(w*2,.025f,.033f),champagne);
                Box("Continuous recessed sideline light",new Vector3(0,.092f,side*(h+.40f)),new Vector3(w*2,.023f,.046f),team);
                for(float x=-w+.9f;x<w;x+=1.8f)
                    Box("Promenade stone joint",new Vector3(x,.003f,side*(h+1.2f)),new Vector3(.025f,.014f,1.26f),graphite);
                for(int half=-1;half<=1;half+=2)
                {
                    float length=h-g-.40f,z=half*(g+.40f+length*.5f);
                    Box("Endline stone apron",new Vector3(side*(w+1.2f),-.035f,z),new Vector3(1.3f,.07f,length),ceramic);
                    Box("Raised endline curb",new Vector3(side*(w+.42f),.018f,z),new Vector3(.40f,.13f,length),graphite);
                    Box("Endline light strip",new Vector3(side*(w+.40f),.092f,z),new Vector3(.046f,.023f,length),team);
                    Box("Corner light housing",new Vector3(side*(w-.28f),.08f,half*(h+.42f)),new Vector3(.66f,.15f,.62f),graphite);
                    Box("Corner frosted cap",new Vector3(side*(w-.28f),.165f,half*(h+.42f)),new Vector3(.42f,.05f,.34f),team);
                }
            }
        }
        private void FieldStencils(MatchRules rules)
        {
            float w=rules.FieldWidth*.5f,h=rules.FieldHeight*.5f;
            for(int side=-1;side<=1;side+=2)
            {
                var paint=side<0 ? cyanPaint : amberPaint;
                for(int lane=-1;lane<=1;lane+=2)
                {
                    for(int i=0;i<3;i++)
                    {
                        float x=side*(rules.CenterCircleRadius+4+i*.62f),z=lane*h*.53f;
                        Polygon("Painted team direction chevron",paint,new Vector2(x-side*.34f,z-.48f),
                            new Vector2(x+side*.15f,z),new Vector2(x-side*.34f,z+.48f),
                            new Vector2(x-side*.22f,z+.48f),new Vector2(x+side*.28f,z),new Vector2(x-side*.22f,z-.48f));
                    }
                    for(int segment=0;segment<4;segment++)
                    {
                        float x=side*(w*.59f+segment*.75f),z=lane*h*.53f;
                        Polygon("Broken tactical field stroke",paint,new Vector2(x,z-.018f),new Vector2(x+side*.42f,z-.018f),
                            new Vector2(x+side*.42f,z+.018f),new Vector2(x,z+.018f));
                    }
                    Arc("Corner team sweep",new Vector2(side*(w-4),lane*(h-4)),2.55f,2.58f,
                        side<0 ? -.9f : 2.24f,1.8f,paint);
                }
            }
        }
        private Material Surface(string name,Color color,float metal,Color emission=default)
        {
            var mat=new Material(Resources.Load<Shader>("SceneSurface/ArenaInlay")) {name=name,color=color,enableInstancing=true};
            mat.SetFloat("_Metallic",metal); mat.SetColor("_Emission",emission);
            mat.SetFloat("_Flow",emission.maxColorComponent>0 ? .65f : 0); materials.Add(mat); return mat;
        }
        public void Render(float presentationTime)
        { mint.SetFloat("_ArenaTime",presentationTime); amber.SetFloat("_ArenaTime",presentationTime); }
        private void Box(string name,Vector3 position,Vector3 scale,Material material)
        {
            if(!boxes.TryGetValue(scale,out var box))
            {
                box=PlayerModelView.BeveledBox(scale,Mathf.Min(.045f,Mathf.Min(scale.x,Mathf.Min(scale.y,scale.z))*.20f));
                boxes.Add(scale,box); meshes.Add(box);
            }
            var mesh=Object(name,box,material); mesh.transform.localPosition=position;
        }
        private MeshRenderer Object(string name,Mesh mesh,Material material)
        {
            var go=new GameObject(name); go.transform.SetParent(transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.On; renderer.receiveShadows=true; InlayCount++; return renderer;
        }
        private void Arc(string name,Vector2 center,float inner,float outer,float start,float sweep,Material material)
        {
            int segments=Mathf.Max(3,Mathf.CeilToInt(sweep*24));
            var v=new Vector3[(segments+1)*2]; var uv=new Vector2[v.Length]; var triangles=new int[segments*6];
            float height=material==graphite ? .014f : material==champagne ? .024f : material==mint || material==amber ? .030f : .018f;
            for(int i=0;i<=segments;i++)
            {
                float a=start+sweep*i/segments; Vector2 d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                Vector2 p=center+d*inner,q=center+d*outer;
                v[i*2]=new Vector3(p.x,height,p.y);v[i*2+1]=new Vector3(q.x,height,q.y);
                uv[i*2]=p*.2f;uv[i*2+1]=q*.2f;
                if(i==segments) continue; int k=i*2,t=i*6;
                triangles[t]=k;triangles[t+1]=k+3;triangles[t+2]=k+1;
                triangles[t+3]=k;triangles[t+4]=k+2;triangles[t+5]=k+3;
            }
            var mesh=new Mesh {name=name};mesh.vertices=v;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateTangents();
            meshes.Add(mesh);Object(name,mesh,material);
        }
        private void Polygon(string name,Material material,params Vector2[] outline)
        {
            var v=new Vector3[outline.Length];var uv=new Vector2[outline.Length];var indices=Triangulate(outline);
            for(int i=0;i<v.Length;i++) v[i]=new Vector3(outline[i].x,.021f,outline[i].y);
            for(int i=0;i<v.Length;i++) uv[i]=outline[i]*.2f;
            var mesh=new Mesh {name=name};mesh.vertices=v;mesh.uv=uv;mesh.triangles=indices;mesh.RecalculateNormals();mesh.RecalculateTangents();
            meshes.Add(mesh);Object(name,mesh,material);
        }
        private static int[] Triangulate(Vector2[] p)
        {
            float area=0; for(int i=0;i<p.Length;i++) area+=Cross(p[i],p[(i+1)%p.Length]);
            var remaining=new List<int>();
            for(int i=0;i<p.Length;i++) remaining.Add(area>=0 ? i : p.Length-1-i);
            var triangles=new List<int>(); int guard=0;
            while(remaining.Count>2 && guard++<p.Length*p.Length)
            {
                bool found=false;
                for(int i=0;i<remaining.Count;i++)
                {
                    int a=remaining[(i+remaining.Count-1)%remaining.Count],b=remaining[i],c=remaining[(i+1)%remaining.Count];
                    if(Cross(p[b]-p[a],p[c]-p[b])<=.000001f) continue;
                    bool contains=false;
                    foreach(int j in remaining)
                    {
                        if(j==a || j==b || j==c) continue;
                        if(Cross(p[b]-p[a],p[j]-p[a])>=0 && Cross(p[c]-p[b],p[j]-p[b])>=0 && Cross(p[a]-p[c],p[j]-p[c])>=0)
                        {contains=true;break;}
                    }
                    if(contains) continue;
                    triangles.Add(a);triangles.Add(c);triangles.Add(b);remaining.RemoveAt(i);found=true;break;
                }
                if(!found) break;
            }
            return triangles.ToArray();
        }
        private static float Cross(Vector2 a,Vector2 b) => a.x*b.y-a.y*b.x;
        private void OnDestroy()
        {foreach(var material in materials) Destroy(material);foreach(var mesh in meshes) Destroy(mesh);}
    }
}
