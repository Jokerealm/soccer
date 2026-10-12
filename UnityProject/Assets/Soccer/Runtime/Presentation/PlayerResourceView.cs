using Soccer.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soccer.Runtime
{
    /// <summary>角色头顶的一体化双资源条；弹药、蓄力和护盾不增加常驻资源行。</summary>
    public sealed class PlayerResourceView : MonoBehaviour
    {
        private static Material material;
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private readonly Vector3[] vertices=new Vector3[320];
        private readonly Color[] colors=new Color[320];
        private readonly int[] indices=new int[900];
        private int vertexCount,indexCount;
        private Color teamTint;
        private float healthTrail=1, previousHealth=1, damageHold;
        public const int ResourceRows=2;
        public float HealthFraction { get; private set; }
        public float EnergyFraction { get; private set; }
        public bool Visible => meshRenderer!=null && meshRenderer.enabled;

        public void Initialize(Team team,bool local)
        {
            teamTint=local ? new Color(.95f,.80f,.46f,.78f) : team==Team.Cyan
                ? new Color(.43f,.75f,.72f,.70f) : new Color(.88f,.61f,.40f,.70f);
            transform.localRotation=Quaternion.Euler(55,0,0);
            if(material==null) material=new Material(Shader.Find("Sprites/Default")) { name="Quiet Player Resource UI" };
            mesh=new Mesh {name="Rounded Two Resource Bars"}; mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            meshRenderer=gameObject.AddComponent<MeshRenderer>(); meshRenderer.sharedMaterial=material;
            meshRenderer.sortingOrder=790; meshRenderer.shadowCastingMode=ShadowCastingMode.Off; meshRenderer.receiveShadows=false;
        }

        public void Render(PlayerState state,float top,bool animate)
        {
            meshRenderer.enabled=!state.IsDead;
            HealthFraction=Mathf.Clamp01(state.Health/state.Stats.MaxHealth);
            EnergyFraction=Mathf.Clamp01(state.Energy/state.Stats.MaxEnergy);
            if(state.IsDead) { healthTrail=previousHealth=HealthFraction; damageHold=0; return; }
            transform.localPosition=ArenaProjection.VisualOffset(0,top);
            if(HealthFraction<previousHealth-.002f) { healthTrail=Mathf.Max(healthTrail,previousHealth); damageHold=.28f; }
            if(HealthFraction>=healthTrail) healthTrail=HealthFraction;
            if(animate)
            {
                damageHold=Mathf.Max(0,damageHold-Time.deltaTime);
                if(damageHold<=0) healthTrail=Mathf.Lerp(healthTrail,HealthFraction,1-Mathf.Exp(-9*Time.deltaTime));
            }
            previousHealth=HealthFraction;
            vertexCount=indexCount=0;
            // Soft dark casing, relation tab, then two slim tracks. Both teams use the same resource colours.
            Rounded(-.57f,-.125f,1.14f,.235f,.058f,new Color(.035f,.075f,.075f,.63f));
            Rounded(-.545f,-.094f,.036f,.171f,.016f,teamTint);
            Rounded(-.478f,.010f,.995f,.062f,.027f,new Color(.025f,.065f,.064f,.76f));
            Rounded(-.478f,-.074f,.995f,.043f,.019f,new Color(.025f,.065f,.078f,.76f));
            if(healthTrail>HealthFraction+.005f) Rounded(-.478f,.010f,.995f*healthTrail,.062f,.025f,new Color(.76f,.64f,.39f,.42f));
            Color hp=HealthFraction<.25f ? new Color(.97f,.43f,.35f,.91f) : new Color(.46f,.79f,.61f,.91f);
            if(HealthFraction>0) Rounded(-.478f,.010f,.995f*HealthFraction,.062f,.025f,hp);
            if(EnergyFraction>0) Rounded(-.478f,-.074f,.995f*EnergyFraction,.043f,.018f,new Color(.39f,.65f,.82f,.86f));
            mesh.Clear(false); mesh.SetVertices(vertices,0,vertexCount); mesh.SetColors(colors,0,vertexCount);
            mesh.SetTriangles(indices,0,indexCount,0); mesh.RecalculateBounds();
        }

        private void Rounded(float x,float y,float width,float height,float radius,Color color)
        {
            radius=Mathf.Min(radius,Mathf.Min(width,height)*.5f);
            int start=vertexCount;
            Vertex(new Vector3(x+width*.5f,y+height*.5f,0),color);
            const int cornerSteps=5;
            for(int corner=0;corner<4;corner++)
            {
                float cx=corner==0 || corner==3 ? x+width-radius : x+radius;
                float cy=corner<2 ? y+height-radius : y+radius;
                for(int step=0;step<=cornerSteps;step++)
                {
                    float angle=(corner*90+step*90f/cornerSteps)*Mathf.Deg2Rad;
                    Color tint=color; tint*=Mathf.Lerp(.88f,1,(cy+Mathf.Sin(angle)*radius-y)/height); tint.a=color.a;
                    Vertex(new Vector3(cx+Mathf.Cos(angle)*radius,cy+Mathf.Sin(angle)*radius,0),tint);
                }
            }
            int perimeter=4*(cornerSteps+1);
            for(int i=0;i<perimeter;i++)
            { indices[indexCount++]=start; indices[indexCount++]=start+1+i; indices[indexCount++]=start+1+(i+1)%perimeter; }
        }
        private void Vertex(Vector3 p,Color color) {vertices[vertexCount]=p; colors[vertexCount++]=color;}
        private void OnDestroy() {if(mesh!=null) Destroy(mesh);}
    }
}
