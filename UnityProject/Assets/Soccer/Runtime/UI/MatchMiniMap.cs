using System.Collections.Generic;
using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    /// <summary>只读比赛状态的全场地图；所有子元素穿透鼠标，角色头像按实际坐标实时定位。</summary>
    public sealed class MatchMiniMap
    {
        private readonly VisualElement root, frame;
        private readonly PitchCanvas pitch;
        private readonly Dictionary<int, PortraitMarker> markers = new Dictionary<int, PortraitMarker>();
        private readonly Dictionary<CharacterId, Texture2D> portraits = new Dictionary<CharacterId, Texture2D>();
        public bool Visible => frame.style.display.value == DisplayStyle.Flex;
        public int ActivePortraitCount { get; private set; }
        public Rect BoundsInRoot => new Rect(frame.worldBound.position-root.worldBound.position, frame.worldBound.size);
        public Rect CameraBounds => pitch.CameraBounds;
        public Vector2 BallPosition => pitch.BallPosition;
        public Vector2 WorldToMap(Vec2 position) => pitch.Project(position);

        public MatchMiniMap(VisualElement ui)
        {
            root = ui;
            frame = new VisualElement { name="minimap", pickingMode=PickingMode.Ignore };
            frame.AddToClassList("minimap");
            var heading = new VisualElement { pickingMode=PickingMode.Ignore };
            heading.AddToClassList("minimap-heading");
            var title = new Label("全场") { pickingMode=PickingMode.Ignore };
            title.AddToClassList("minimap-title"); heading.Add(title);
            var allies = new Label("● 我方") { pickingMode=PickingMode.Ignore };
            allies.AddToClassList("minimap-allies"); heading.Add(allies);
            var enemies = new Label("● 敌方") { pickingMode=PickingMode.Ignore };
            enemies.AddToClassList("minimap-enemies"); heading.Add(enemies);
            frame.Add(heading);
            pitch = new PitchCanvas(); frame.Add(pitch);
            root.Q("match-page").Add(frame);
            portraits[CharacterId.Marine] = Resources.Load<Texture2D>("FrontEnd/portrait-marine-v2");
            portraits[CharacterId.Thor] = Resources.Load<Texture2D>("FrontEnd/portrait-thor-v2");
            Hide();
        }

        public void Hide()
        {
            frame.style.display=DisplayStyle.None;
            ActivePortraitCount=0;
        }

        public void Refresh(Camera camera, MatchState state, MatchRules rules, int localPlayerId=0)
        {
            if (root.panel==null || state.Players.Count==0 || !(root.resolvedStyle.height>0)) { Hide(); return; }
            // 两种 HUD 保持相同大小；按窗口高度缩放，作为紧凑半透明覆盖层。
            float height = Mathf.Clamp(root.resolvedStyle.height*.17f-14, 108, 148);
            float canvasHeight = height-28;
            float scale = (canvasHeight-22)/(rules.FieldHeight*ArenaProjection.GroundYScale);
            float canvasWidth = (rules.FieldWidth+rules.GoalDepth*2)*scale+22;
            frame.style.width=canvasWidth+16; frame.style.height=height;
            pitch.style.width=canvasWidth; pitch.style.height=canvasHeight;
            pitch.Configure(camera, state, rules, new Vector2(canvasWidth,canvasHeight), scale);
            frame.style.display=DisplayStyle.Flex;
            foreach (var marker in markers.Values) marker.Seen=false;
            Team localTeam=state.Players[localPlayerId].Team;
            ActivePortraitCount=state.Players.Count;
            PortraitMarker local=null;
            int allySlot=0, enemySlot=0;
            foreach (var player in state.Players)
            {
                if (!markers.TryGetValue(player.Id,out var marker))
                {
                    marker=new PortraitMarker(player.Id); markers.Add(player.Id,marker); pitch.Add(marker);
                }
                portraits.TryGetValue(player.Stats.Id,out Texture2D portrait);
                bool isLocal=player.Id==state.Players[localPlayerId].Id;
                bool ally=player.Team==localTeam;
                marker.Refresh(player,portrait,pitch.Project(player.Position),ally,
                    isLocal,state.Ball.OwnerId==player.Id,ally ? ++allySlot : ++enemySlot);
                if (isLocal) local=marker;
            }
            foreach (var marker in markers.Values) if (!marker.Seen) marker.style.display=DisplayStyle.None;
            // 交叠时本地玩家的头像保持可见；不挪动真实位置。
            if (local!=null && pitch.ElementAt(pitch.childCount-1)!=local) local.BringToFront();
            pitch.MarkDirtyRepaint();
        }

        private sealed class PortraitMarker : VisualElement
        {
            public bool Seen;
            private readonly VisualElement portrait, ball;
            private readonly Label badge, down;
            public PortraitMarker(int id)
            {
                name="minimap-player-"+id; pickingMode=PickingMode.Ignore;
                AddToClassList("minimap-player");
                portrait=new VisualElement { name="minimap-portrait-"+id, pickingMode=PickingMode.Ignore };
                portrait.AddToClassList("minimap-portrait"); Add(portrait);
                badge=new Label { pickingMode=PickingMode.Ignore }; badge.AddToClassList("minimap-badge"); Add(badge);
                down=new Label("×") { name="minimap-down-"+id, pickingMode=PickingMode.Ignore }; down.AddToClassList("minimap-down"); Add(down);
                ball=new VisualElement { name="minimap-possession-"+id, pickingMode=PickingMode.Ignore }; ball.AddToClassList("minimap-possession"); Add(ball);
            }
            public void Refresh(PlayerState player, Texture2D image, Vector2 position, bool ally, bool local, bool holding, int slot)
            {
                float size=local ? 24 : 20;
                Seen=true;
                style.display=DisplayStyle.Flex;
                style.width=style.height=size;
                style.left=position.x-size*.5f; style.top=position.y-size*.5f;
                EnableInClassList("minimap-ally",ally); EnableInClassList("minimap-enemy",!ally);
                EnableInClassList("minimap-local",local); EnableInClassList("minimap-knocked-out",player.IsDead);
                if (portrait.style.backgroundImage.value.texture!=image) portrait.style.backgroundImage=new StyleBackground(image);
                badge.text=local ? "你" : slot.ToString();
                down.style.display=player.IsDead ? DisplayStyle.Flex : DisplayStyle.None;
                ball.style.display=holding && !player.IsDead ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private sealed class PitchCanvas : VisualElement
        {
            private Vector2 center;
            private float scale, yScale;
            private MatchRules rules;
            private bool freeBall, openingShield;
            public Rect CameraBounds { get; private set; }
            public Vector2 BallPosition { get; private set; }
            public PitchCanvas()
            {
                name="minimap-pitch"; pickingMode=PickingMode.Ignore;
                style.position=Position.Absolute; style.left=8; style.top=23;
                generateVisualContent+=Draw;
            }
            public Vector2 Project(Vec2 ground) => center+new Vector2(ground.X*scale,-ground.Y*yScale);
            public void Configure(Camera camera, MatchState state, MatchRules definition, Vector2 size, float pixelsPerUnit)
            {
                rules=definition; center=size*.5f; scale=pixelsPerUnit; yScale=scale*ArenaProjection.GroundYScale;
                BallPosition=Project(state.Ball.Position); freeBall=state.Ball.OwnerId<0; openingShield=state.Phase==MatchPhase.Countdown;
                // 用真正的全窗口镜头射线求地面可见范围。
                var plane=new Plane(Vector3.up,Vector3.zero);
                Vector2 min=new Vector2(float.MaxValue,float.MaxValue), max=new Vector2(float.MinValue,float.MinValue);
                for (int i=0;i<4;i++)
                {
                    var ray=camera.ViewportPointToRay(new Vector3(i%2,i/2,0));
                    if (!plane.Raycast(ray,out float distance)) continue;
                    Vector2 point=Project(ArenaProjection.ToGround(ray.GetPoint(distance)));
                    min=Vector2.Min(min,point); max=Vector2.Max(max,point);
                }
                var whole=new Rect(Project(new Vec2(-rules.FieldWidth*.5f-rules.GoalDepth,rules.FieldHeight*.5f)),
                    new Vector2((rules.FieldWidth+rules.GoalDepth*2)*scale,rules.FieldHeight*yScale));
                CameraBounds=Rect.MinMaxRect(Mathf.Clamp(min.x,whole.xMin,whole.xMax),Mathf.Clamp(min.y,whole.yMin,whole.yMax),
                    Mathf.Clamp(max.x,whole.xMin,whole.xMax),Mathf.Clamp(max.y,whole.yMin,whole.yMax));
            }
            private void Draw(MeshGenerationContext context)
            {
                if (rules==null) return;
                float hw=rules.FieldWidth*.5f, hh=rules.FieldHeight*.5f, goal=rules.GoalWidth*.5f;
                Vector2 tl=Project(new Vec2(-hw,hh)), br=Project(new Vec2(hw,-hh));
                Rect court=Rect.MinMaxRect(tl.x,tl.y,br.x,br.y);
                Fill(context,court,new Color(.10f,.22f,.20f,.70f));
                var ink=new Color(.67f,.83f,.74f,.33f);
                Outline(context,court,1,ink);
                Segment(context,Project(new Vec2(0,hh)),Project(new Vec2(0,-hh)),.8f,ink);
                Ellipse(context,center,rules.CenterCircleRadius*scale,rules.CenterCircleRadius*yScale,.8f,ink);
                Disc(context,center,1,ink);
                if (openingShield) Ellipse(context,center,rules.CenterCircleRadius*scale,rules.CenterCircleRadius*yScale,1.4f,new Color(.5f,.9f,.8f,.65f));
                for (int side=-1;side<=1;side+=2)
                {
                    Color team=side<0 ? new Color(.34f,.87f,.76f,.85f) : new Color(1,.64f,.36f,.85f);
                    Vector2 a=Project(new Vec2(side*hw,goal)), b=Project(new Vec2(side*(hw+rules.GoalDepth),-goal));
                    Rect net=Rect.MinMaxRect(Mathf.Min(a.x,b.x),a.y,Mathf.Max(a.x,b.x),b.y);
                    Fill(context,net,new Color(team.r,team.g,team.b,.15f)); Outline(context,net,1.2f,team);
                    Vector2 pa=Project(new Vec2(side*hw,goal+4)), pb=Project(new Vec2(side*(hw-5),-goal-4));
                    Outline(context,Rect.MinMaxRect(Mathf.Min(pa.x,pb.x),pa.y,Mathf.Max(pa.x,pb.x),pb.y),.7f,ink);
                }
                Fill(context,CameraBounds,new Color(.78f,.93f,.88f,.055f));
                Outline(context,CameraBounds,1,new Color(.72f,.94f,.87f,.68f));
                if (freeBall)
                {
                    Disc(context,BallPosition,5,new Color(1,.78f,.36f,.16f));
                    Disc(context,BallPosition,2.9f,new Color(1,.95f,.76f));
                    Disc(context,BallPosition,1.1f,new Color(.07f,.13f,.16f));
                }
            }
            private static void Fill(MeshGenerationContext context,Rect rect,Color color)
            {
                if (rect.width<=0 || rect.height<=0) return;
                var mesh=context.Allocate(4,6);
                Put(mesh,new Vector2(rect.xMin,rect.yMin),color); Put(mesh,new Vector2(rect.xMax,rect.yMin),color);
                Put(mesh,new Vector2(rect.xMax,rect.yMax),color); Put(mesh,new Vector2(rect.xMin,rect.yMax),color);
                mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
                mesh.SetNextIndex(2); mesh.SetNextIndex(3); mesh.SetNextIndex(0);
            }
            private static void Outline(MeshGenerationContext context,Rect r,float width,Color color)
            {
                Fill(context,new Rect(r.xMin,r.yMin,r.width,width),color);
                Fill(context,new Rect(r.xMin,r.yMax-width,r.width,width),color);
                Fill(context,new Rect(r.xMin,r.yMin,width,r.height),color);
                Fill(context,new Rect(r.xMax-width,r.yMin,width,r.height),color);
            }
            private static void Segment(MeshGenerationContext context,Vector2 a,Vector2 b,float width,Color color)
            {
                Vector2 delta=(b-a).normalized, side=new Vector2(-delta.y,delta.x)*width*.5f;
                var mesh=context.Allocate(4,6);
                Put(mesh,a-side,color); Put(mesh,b-side,color); Put(mesh,b+side,color); Put(mesh,a+side,color);
                mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
                mesh.SetNextIndex(2); mesh.SetNextIndex(3); mesh.SetNextIndex(0);
            }
            private static void Disc(MeshGenerationContext context,Vector2 c,float radius,Color color)
            {
                const int steps=16;
                var mesh=context.Allocate(steps+1,steps*3); Put(mesh,c,color);
                for (int i=0;i<steps;i++) {float a=i*Mathf.PI*2/steps; Put(mesh,c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,color);}
                for (int i=0;i<steps;i++) {mesh.SetNextIndex(0);mesh.SetNextIndex((ushort)(i+1));mesh.SetNextIndex((ushort)((i+1)%steps+1));}
            }
            private static void Ellipse(MeshGenerationContext context,Vector2 c,float rx,float ry,float width,Color color)
            {
                const int steps=40;
                var mesh=context.Allocate(steps*2,steps*6);
                for (int i=0;i<steps;i++)
                {
                    float a=i*Mathf.PI*2/steps; Vector2 p=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                    Put(mesh,c+Vector2.Scale(p,new Vector2(rx,ry)),color);
                    Put(mesh,c+Vector2.Scale(p,new Vector2(rx-width,ry-width)),color);
                }
                for (int i=0;i<steps;i++)
                {
                    ushort a=(ushort)(i*2),b=(ushort)((i+1)%steps*2);
                    mesh.SetNextIndex(a);mesh.SetNextIndex(b);mesh.SetNextIndex((ushort)(a+1));
                    mesh.SetNextIndex((ushort)(a+1));mesh.SetNextIndex(b);mesh.SetNextIndex((ushort)(b+1));
                }
            }
            private static void Put(MeshWriteData mesh,Vector2 p,Color color) =>
                mesh.SetNextVertex(new Vertex {position=new Vector3(p.x,p.y,Vertex.nearZ),tint=color});
        }
    }
}
