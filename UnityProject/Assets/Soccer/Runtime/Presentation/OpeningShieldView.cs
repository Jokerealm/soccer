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
