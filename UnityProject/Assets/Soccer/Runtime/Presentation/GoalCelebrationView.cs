using System.Collections.Generic;
using Soccer.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soccer.Runtime
{
    /// <summary>两门复用一个动态网格：会翻折的纸片、卷曲飘带和展开的彩旗。仅参与表现。</summary>
    public sealed class GoalCelebrationView : MonoBehaviour
    {
        public const int PaperBudget=96;
        public const int RibbonBudget=12;
        public int ActivePaperCount { get; private set; }
        public int ActiveRibbonCount { get; private set; }
        public int ActivePennantCount { get; private set; }
        public int GoalSide { get; private set; }
        public float Age { get; private set; }=3;
        public bool Visible => paperRenderer!=null && paperRenderer.enabled;
        private Mesh mesh;
        private MeshRenderer paperRenderer;
        private Material material;
        private LineRenderer cord,shockwave;
        private float halfWidth,halfGoal;
        private Team scoringTeam;
        private readonly List<Vector3> vertices=new List<Vector3>(2400);
        private readonly List<int> triangles=new List<int>(3600);
        private readonly List<Color> colors=new List<Color>(2400);
        private static readonly Color[] Palette={new Color(.28f,.91f,.80f),new Color(1,.74f,.28f),
            new Color(1,.40f,.55f),new Color(.60f,.50f,1),new Color(.40f,.75f,1),new Color(1,.94f,.76f)};

        public void Initialize(MatchRules rules)
        {
            halfWidth=rules.FieldWidth*.5f;halfGoal=rules.GoalWidth*.5f;
            mesh=new Mesh {name="Pooled celebration paper and cloth"};mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            paperRenderer=gameObject.AddComponent<MeshRenderer>();
            material=new Material(Resources.Load<Shader>("SceneSurface/CelebrationPaper"));paperRenderer.sharedMaterial=material;
            paperRenderer.shadowCastingMode=ShadowCastingMode.Off;paperRenderer.receiveShadows=true;
            cord=ViewLines.Create(transform,"Goal pennant cord",.025f,new Color(1,.91f,.66f),90);cord.positionCount=33;
            shockwave=ViewLines.Create(transform,"Goal line gold pulse",.08f,Color.white,43);shockwave.positionCount=65;shockwave.widthCurve=AnimationCurve.Linear(0,1,1,1);
            Clear();
        }
        public void Goal(Team team)
        {scoringTeam=team;GoalSide=team==Team.Cyan ? 1 : -1;Age=0;Build();}
        public void Render(MatchState state,bool animate)
        {
            if(!Visible) return;
            if(state.Phase!=MatchPhase.GoalCelebration && state.Phase!=MatchPhase.Finished) {Clear();return;}
            if(animate) Age+=Mathf.Min(Time.unscaledDeltaTime,.1f);
            if(Age>=2.2f) {Clear();return;}
            if(animate) Build();
        }
        private Vector3 Garland(float t)
        {
            float bow=Mathf.Sin(t*Mathf.PI);
            return new Vector3(GoalSide*(halfWidth+.38f-bow*1.1f),2.85f-bow*.18f,(t*2-1)*(halfGoal+.50f));
        }
        private void Build()
        {
            vertices.Clear();triangles.Clear();colors.Clear();
            float fade=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((Age-1.72f)/.48f));
            ActivePaperCount=PaperBudget;ActiveRibbonCount=RibbonBudget;ActivePennantCount=11;
            for(int i=0;i<PaperBudget;i++)
            {
                float seed=i*2.39996f,a=Age;
                int end=i%2==0 ? 1 : -1;
                var origin=new Vector3(GoalSide*(halfWidth-.30f),.90f,end*(halfGoal+.35f));
                var velocity=new Vector3(-GoalSide*(.35f+Frac(i*.618f)*2.1f),3.7f+Frac(i*.414f)*2.1f,
                    -end*(.25f+Frac(i*.732f)*1.4f)+Mathf.Sin(seed)*.6f);
                var position=origin+velocity*a+new Vector3(Mathf.Sin(a*5+seed)*.12f,-1.9f*a*a,Mathf.Cos(a*4+seed)*.14f);
                position.y=Mathf.Max(.04f,position.y);
                var rotation=Quaternion.Euler(seed*41+a*(170+i%5*37),seed*57+a*145,seed*30+a*110);
                float size=.12f+Frac(i*.317f)*.09f;
                var x=rotation*Vector3.right*size;var y=rotation*Vector3.up*size*.68f;
                var fold=rotation*Vector3.forward*(Mathf.Sin(seed+a*9)*size*.32f);
                var color=PaperColor(i,fade);
                Quad(position-x-y,position-y+fold,position+y+fold,position-x+y,color);
                Quad(position-y+fold,position+x-y,position+x+y,position+y+fold,color);
            }
            for(int i=0;i<RibbonBudget;i++)
            {
                int end=i%2==0 ? 1 : -1;
                var color=PaperColor(i+2,fade);
                for(int n=0;n<18;n++)
                {
                    float u=n/18f,v=(n+1)/18f;
                    var p=RibbonPoint(i,end,u);var q=RibbonPoint(i,end,v);
                    var width=new Vector3(.035f*GoalSide,Mathf.Sin(u*12+i)*.022f,.025f);
                    Quad(p-width,p+width,q+width,q-width,color);
                }
            }
            float open=1-Mathf.Pow(1-Mathf.Clamp01(Age/.24f),3);
            for(int i=0;i<11;i++)
            {
                float t=(i+.5f)/11;
                var a=Garland(t-.032f);var b=Garland(t+.032f);
                var tip=Garland(t)+new Vector3(-GoalSide*(.33f+Mathf.Sin(Age*7+i)*.07f),-.58f*open,Mathf.Sin(Age*8+i*.9f)*.09f);
                var center=(a+b+tip)/3+new Vector3(-GoalSide*.045f,0,0);
                var color=PaperColor(i,fade);
                Triangle(a,b,center,color);Triangle(b,tip,center,color);Triangle(tip,a,center,color);
            }
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            paperRenderer.enabled=true;cord.enabled=true;
            cord.startColor=cord.endColor=new Color(1,.91f,.66f,fade);
            for(int i=0;i<33;i++) cord.SetPosition(i,Garland(i/32f));
            shockwave.enabled=Age<.65f;
            if(shockwave.enabled)
            {
                float t=Age/.65f;shockwave.widthMultiplier=.09f*(1-t*.7f);
                shockwave.startColor=shockwave.endColor=new Color(1,.80f,.35f,(1-t)*(1-t)*.75f);
                for(int i=0;i<65;i++) {float a=i/64f*Mathf.PI*2;shockwave.SetPosition(i,new Vector3(GoalSide*halfWidth+Mathf.Cos(a)*t*2.4f,.042f,Mathf.Sin(a)*t*2.4f));}
            }
        }
        private Vector3 RibbonPoint(int i,int end,float u)
        {
            float t=Mathf.Max(0,Age-u*.24f),seed=i*1.73f;
            return new Vector3(GoalSide*(halfWidth-.30f-t*(.6f+i%3*.45f))+Mathf.Sin(u*16+seed+Age*3)*.14f,
                Mathf.Max(.05f,.92f+(4.6f+i%4*.28f)*t-1.9f*t*t-u*.95f)+Mathf.Cos(u*16+seed)*.07f,
                end*(halfGoal+.35f)-end*t*(.25f+i%4*.17f)+Mathf.Cos(u*16+seed+Age*3)*.15f);
        }
        private Color PaperColor(int i,float alpha)
        {
            var c=Palette[i%Palette.Length];
            if(i%3==0) c=scoringTeam==Team.Cyan ? Palette[0] : Palette[1];
            c.a=alpha;return c;
        }
        private static float Frac(float value) => value-Mathf.Floor(value);
        private void Triangle(Vector3 a,Vector3 b,Vector3 c,Color color)
        {int start=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);for(int i=0;i<3;i++) {triangles.Add(start+i);colors.Add(color);}}
        private void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
        {int start=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);for(int i=0;i<4;i++) colors.Add(color);triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);triangles.Add(start);triangles.Add(start+2);triangles.Add(start+3);}
        public void Clear()
        {Age=3;GoalSide=0;ActivePaperCount=ActiveRibbonCount=ActivePennantCount=0;if(paperRenderer!=null) paperRenderer.enabled=false;if(cord!=null) cord.enabled=false;if(shockwave!=null) shockwave.enabled=false;}
        private void OnDestroy() {if(mesh!=null) Destroy(mesh);if(material!=null) Destroy(material);}
    }
}
