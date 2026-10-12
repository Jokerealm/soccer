using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    /// <summary>局部镜头下仅在足球出画时出现；整个提示穿透鼠标，不干扰瞄准。</summary>
    internal sealed class BallLocator
    {
        private readonly VisualElement root,container;
        private readonly DirectionMarker marker;
        private readonly Label distance;
        public bool Visible => container.style.display.value==DisplayStyle.Flex;
        public BallLocator(VisualElement ui)
        {
            root=ui;
            container=new VisualElement {name="offscreen-ball",pickingMode=PickingMode.Ignore};
            container.style.position=Position.Absolute;
            container.style.width=116; container.style.height=46;
            container.style.flexDirection=FlexDirection.Row; container.style.alignItems=Align.Center;
            container.style.backgroundColor=new Color(.045f,.09f,.105f,.86f);
            container.style.borderTopLeftRadius=container.style.borderTopRightRadius=23;
            container.style.borderBottomLeftRadius=container.style.borderBottomRightRadius=23;
            container.style.borderTopWidth=container.style.borderBottomWidth=1;
            container.style.borderLeftWidth=container.style.borderRightWidth=1;
            var trim=new Color(.49f,.85f,.74f,.65f);
            container.style.borderTopColor=container.style.borderBottomColor=trim;
            container.style.borderLeftColor=container.style.borderRightColor=trim;
            marker=new DirectionMarker(); container.Add(marker);
            distance=new Label {name="offscreen-ball-distance",pickingMode=PickingMode.Ignore};
            distance.style.fontSize=13; distance.style.color=new Color(.91f,.95f,.86f);
            container.Add(distance); root.Q("match-page").Add(container); Hide();
        }
        public void Hide() => container.style.display=DisplayStyle.None;
        public void Refresh(Camera camera,MatchState state,Rect minimapBounds=default)
        {
            Vector3 target=camera.WorldToViewportPoint(ArenaProjection.ToWorld(state.Ball.Position,state.Ball.Height+.18f));
            if(target.z>0 && target.x>=0 && target.x<=1 && target.y>=0 && target.y<=1) { Hide(); return; }
            Rect pixels=camera.pixelRect;
            Vector2 topLeft=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(pixels.xMin,Screen.height-pixels.yMax))-root.worldBound.position;
            Vector2 bottomRight=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(pixels.xMax,Screen.height-pixels.yMin))-root.worldBound.position;
            Vector2 point=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(pixels.xMin+target.x*pixels.width,
                Screen.height-(pixels.yMin+target.y*pixels.height)))-root.worldBound.position;
            Vector2 min=new Vector2(topLeft.x+74,Mathf.Max(topLeft.y+40,104));
            Vector2 max=bottomRight-new Vector2(74,42);
            Vector2 center=(min+max)*.5f,half=(max-min)*.5f,delta=point-center;
            float fraction=Mathf.Min(half.x/Mathf.Max(.001f,Mathf.Abs(delta.x)),half.y/Mathf.Max(.001f,Mathf.Abs(delta.y)));
            Vector2 edge=center+delta*fraction;
            // 左下角地图拥有固定位置；球的边缘提示沿边缘向上避让。
            if(minimapBounds.width>0 && new Rect(edge-new Vector2(58,23),new Vector2(116,46)).Overlaps(minimapBounds))
                edge.y=Mathf.Max(min.y,minimapBounds.yMin-33);
            container.style.left=edge.x-58; container.style.top=edge.y-23;
            container.style.display=DisplayStyle.Flex;
            marker.Direction=delta.normalized; marker.MarkDirtyRepaint();
            distance.text=$"足球\n{Vec2.Distance(state.Players[0].Position,state.Ball.Position):0.#} m";
        }
        private sealed class DirectionMarker : VisualElement
        {
            public Vector2 Direction;
            public DirectionMarker()
            {
                pickingMode=PickingMode.Ignore; style.width=46; style.height=46;
                generateVisualContent+=Draw;
            }
            private void Draw(MeshGenerationContext context)
            {
                Vector2 c=new Vector2(23,23);
                Polygon(context,c,12,24,new Color(.92f,.94f,.85f));
                Polygon(context,c,4.7f,5,new Color(.08f,.14f,.17f));
                Vector2 side=new Vector2(-Direction.y,Direction.x);
                var mesh=context.Allocate(3,3); Color mint=new Color(.55f,.94f,.79f);
                foreach(var p in new[]{c+Direction*21,c+Direction*14+side*4,c+Direction*14-side*4})
                    mesh.SetNextVertex(new Vertex {position=new Vector3(p.x,p.y,Vertex.nearZ),tint=mint});
                mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
            }
            private static void Polygon(MeshGenerationContext context,Vector2 center,float radius,int sides,Color color)
            {
                var mesh=context.Allocate(sides+1,sides*3);
                mesh.SetNextVertex(new Vertex {position=new Vector3(center.x,center.y,Vertex.nearZ),tint=color});
                for(int i=0;i<sides;i++)
                {
                    float a=i*Mathf.PI*2/sides-Mathf.PI*.5f; Vector2 p=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;
                    mesh.SetNextVertex(new Vertex {position=new Vector3(p.x,p.y,Vertex.nearZ),tint=color});
                }
                for(int i=0;i<sides;i++) {mesh.SetNextIndex(0);mesh.SetNextIndex((ushort)(i+1));mesh.SetNextIndex((ushort)((i+1)%sides+1));}
            }
        }
    }
}
