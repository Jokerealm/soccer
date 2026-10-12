using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    /// <summary>透明开球仪式：时间完全读取比赛状态，暂停时冻结装饰动画。</summary>
    internal sealed class CountdownPresentation
    {
        private readonly VisualElement container;
        private readonly VisualElement root;
        private readonly Label number, caption;
        private readonly CountdownDial dial;
        private MatchPhase previousPhase;
        private bool observed, visible, kickoff, effects=true;
        private int digit;
        private float age=1;
        public int BeatCount { get; private set; }
        public float Fraction => dial.Fraction;
        public bool Visible => visible;

        public CountdownPresentation(VisualElement root)
        {
            this.root=root;
            container=root.Q("countdown-overlay");
            number=root.Q<Label>("countdown-number"); caption=root.Q<Label>("countdown-caption");
            dial=new CountdownDial(); container.Insert(0,dial);
        }
        public void PositionOverBall(Camera camera,MatchState state)
        {
            if(!visible || root.panel==null) return;
            Vector3 screen=camera.WorldToScreenPoint(ArenaProjection.ToWorld(state.Ball.Position));
            Vector2 point=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(screen.x,Screen.height-screen.y))-root.worldBound.position;
            container.style.alignSelf=Align.Auto;
            container.style.left=Mathf.Clamp(point.x-112,24,Mathf.Max(24,root.worldBound.width-248));
        }
        public void Reset()
        { observed=visible=kickoff=false; digit=0; age=1; container.AddToClassList("hidden"); }
        public void SetEffects(bool value) { effects=value; dial.Effects=value; }
        public void Observe(MatchState state)
        {
            if(state.Phase==MatchPhase.Countdown)
            {
                int next=Mathf.Max(1,Mathf.CeilToInt(state.PhaseRemaining));
                if(!visible || kickoff || next!=digit)
                {
                    visible=true; kickoff=false; digit=next; age=0; BeatCount++;
                    container.RemoveFromClassList("hidden");
                    number.text=next.ToString(); caption.text=state.IsOpeningWarmup ? "移动热身中" : state.Half==2 ? "下半场 · 准备开球" : "准备开球";
                    container.RemoveFromClassList("kickoff");
                }
                dial.Fraction=Mathf.Clamp01(state.PhaseRemaining-(digit-1));
            }
            else if(observed && previousPhase==MatchPhase.Countdown && state.Phase==MatchPhase.Playing)
            {
                kickoff=visible=true; age=0; BeatCount++;
                container.RemoveFromClassList("hidden"); container.AddToClassList("kickoff");
                number.text="开球！"; caption.text="一起开路，一起进球"; dial.Fraction=1;
            }
            else if(state.Phase!=MatchPhase.Playing) Reset();
            observed=true; previousPhase=state.Phase;
        }
        public void Tick(float dt,bool paused)
        {
            if(!visible) return;
            if(!paused) age+=dt;
            float pulse=effects ? Mathf.Pow(Mathf.Clamp01(1-age/.28f),2) : 0;
            float scale=1+pulse*.22f;
            number.style.scale=new Scale(new Vector3(scale,scale,1));
            container.style.opacity=kickoff ? Mathf.Clamp01((.64f-age)/.24f) : Mathf.Clamp01(age/.055f);
            dial.Age=age; dial.Kickoff=kickoff; dial.MarkDirtyRepaint();
            if(kickoff && age>=.64f) {visible=false; container.AddToClassList("hidden");}
        }
    }

    internal sealed class CountdownDial : VisualElement
    {
        public float Fraction=1,Age;
        public bool Kickoff,Effects=true;
        private readonly Color mint=new Color(.56f,.91f,.79f,.9f),gold=new Color(1,.81f,.43f,.85f);
        public CountdownDial()
        {
            name="countdown-dial"; pickingMode=PickingMode.Ignore;
            style.position=Position.Absolute; style.left=style.right=0; style.top=0; style.height=216;
            generateVisualContent+=Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            if(contentRect.width<=0) return;
            Vector2 center=new Vector2(contentRect.width*.5f,104);
            var track=gold; track.a=.20f;
            Arc(context,center,79,1.2f,0,Mathf.PI*2,track,96);
            Arc(context,center,79,3,-Mathf.PI/2,Mathf.PI*2*(Kickoff ? 1 : Fraction),mint,96);
            for(int i=0;i<36;i++)
            {
                float a=i*Mathf.PI*2/36;
                var c=i%3==0 ? gold : track;
                Arc(context,center,91,i%3==0 ? 4 : 2,a,.012f,c,1);
            }
            if(!Effects) return;
            float orbit=Age*.55f;
            for(int i=0;i<3;i++) Arc(context,center,99,1.4f,orbit+i*Mathf.PI*2/3,.32f,gold,12);
            float progress=Mathf.Clamp01(Age/.6f);
            var echo=mint; echo.a=(1-progress)*.36f;
            Arc(context,center,81+progress*26,1.8f,0,Mathf.PI*2,echo,96);
            if(progress>=1) return;
            for(int i=0;i<14;i++)
            {
                float a=i*2.39996f, radius=74+progress*(Kickoff ? 69 : 34);
                Vector2 p=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;
                float size=(1-progress)*(i%3==0 ? 3.2f : 2);
                Color c=i%2==0 ? mint : gold; c.a=(1-progress)*.85f;
                var mesh=context.Allocate(4,6);
                mesh.SetNextVertex(new Vertex {position=new Vector3(p.x,p.y-size*1.6f,Vertex.nearZ),tint=c});
                mesh.SetNextVertex(new Vertex {position=new Vector3(p.x+size,p.y,Vertex.nearZ),tint=c});
                mesh.SetNextVertex(new Vertex {position=new Vector3(p.x,p.y+size*1.6f,Vertex.nearZ),tint=c});
                mesh.SetNextVertex(new Vertex {position=new Vector3(p.x-size,p.y,Vertex.nearZ),tint=c});
                mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
                mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);
            }
        }
        private static void Arc(MeshGenerationContext context,Vector2 center,float radius,float width,float start,float sweep,Color color,int segments)
        {
            if(sweep<=0 || color.a<=0) return;
            int count=Mathf.Max(1,Mathf.CeilToInt(segments*sweep/(Mathf.PI*2)));
            var mesh=context.Allocate((count+1)*2,count*6);
            for(int i=0;i<=count;i++)
            {
                float a=start+sweep*i/count; Vector2 direction=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                Vector2 inner=center+direction*(radius-width*.5f),outer=center+direction*(radius+width*.5f);
                mesh.SetNextVertex(new Vertex {position=new Vector3(inner.x,inner.y,Vertex.nearZ),tint=color});
                mesh.SetNextVertex(new Vertex {position=new Vector3(outer.x,outer.y,Vertex.nearZ),tint=color});
            }
            for(int i=0;i<count;i++)
            {
                ushort k=(ushort)(i*2);
                mesh.SetNextIndex(k); mesh.SetNextIndex((ushort)(k+1)); mesh.SetNextIndex((ushort)(k+3));
                mesh.SetNextIndex(k); mesh.SetNextIndex((ushort)(k+3)); mesh.SetNextIndex((ushort)(k+2));
            }
        }
    }
}
