using Soccer.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soccer.Runtime
{
    /// <summary>只画防护罩，碰撞和禁接球由 Core 负责。</summary>
    public sealed class OpeningShieldView : MonoBehaviour
    {
        private MeshRenderer dome;
        private Material material;
        private LineRenderer ring;
        private float time,fade;
        private readonly System.Collections.Generic.List<LineRenderer> pads = new System.Collections.Generic.List<LineRenderer>();
        public bool Visible => dome!=null && dome.enabled;
        public float PresentationTime => time;
        public void Initialize(Transform parent)
        {
            transform.SetParent(parent,false);
            var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name="Transparent football protection dome"; sphere.transform.SetParent(transform,false);
            Destroy(sphere.GetComponent<Collider>());
            dome=sphere.GetComponent<MeshRenderer>();
            material=new Material(Resources.Load<Shader>("SceneSurface/OpeningShield")); dome.sharedMaterial=material;
            dome.shadowCastingMode=ShadowCastingMode.Off; dome.receiveShadows=false;
            ring=ViewLines.Create(transform,"Shield ground boundary",.035f,new Color(.48f,.95f,.78f,.85f),710);
            ring.positionCount=97; dome.enabled=ring.enabled=false;
        }
        public void Render(MatchState state,MatchRules rules,bool animate)
        {
            bool protectedBall=state.IsOpeningWarmup;
            while(pads.Count<state.Players.Count)
            {
                var pad=ViewLines.Create(transform,"Opening hexagon "+pads.Count,.055f,Color.white,711);
                pad.positionCount=7;pads.Add(pad);
            }
            for(int i=0;i<pads.Count;i++)
            {
                var pad=pads[i];pad.enabled=protectedBall && i<state.Players.Count;
                if(!pad.enabled)continue;
                var player=state.Players[i];var color=player.Team==Team.Cyan ? new Color(.32f,.96f,.82f,.85f) : new Color(1,.64f,.28f,.85f);
                pad.startColor=pad.endColor=color;
                for(int corner=0;corner<=6;corner++)pad.SetPosition(corner,ArenaProjection.ToWorld(OpeningArea.Vertex(player.OpeningPosition,rules.OpeningHexRadius,corner),.045f));
            }
            if(animate) {time+=Mathf.Min(Time.unscaledDeltaTime,.1f); fade=Mathf.MoveTowards(fade,protectedBall ? 1 : 0,Time.unscaledDeltaTime*7);}
            if(protectedBall) fade=1;
            // 正式开球即关闭可见罩体，避免视觉阻挡晚于规则解除。
            dome.enabled=ring.enabled=protectedBall;
            if(!protectedBall) return;
            float radius=rules.OpeningShieldRadius;
            dome.transform.position=ArenaProjection.ToWorld(state.Ball.Position,.012f);
            dome.transform.localScale=Vector3.one*radius*2;
            material.SetFloat("_ShieldTime",time); material.SetFloat("_Opacity",fade);
            for(int i=0;i<97;i++)
            {
                float a=i*Mathf.PI*2/96;
                ring.SetPosition(i,ArenaProjection.ToWorld(state.Ball.Position+new Vec2(Mathf.Cos(a),Mathf.Sin(a))*radius,.027f));
            }
        }
        private void OnDestroy() {if(material!=null) Destroy(material);}
    }
}
