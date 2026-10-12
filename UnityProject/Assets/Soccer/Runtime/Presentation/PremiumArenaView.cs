using Soccer.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soccer.Runtime
{
    /// <summary>球场图片是地面材质；灯光、球门、球体及投影由实时三维场景生成。</summary>
    public sealed class PremiumArenaView : MonoBehaviour
    {
        public bool Ready { get; private set; }
        public Light KeyLight { get; private set; }
        public MeshRenderer Floor { get; private set; }
        public DynamicStadiumView Dynamics { get; private set; }
        public ArenaDetailView Details { get; private set; }
        private readonly Light[] sidelineFills=new Light[2];
        private void Start()
        {
            var material = Resources.Load<Material>("SceneSurface/Floor");
            if (material == null) return;
            var old = GameObject.Find("Arena (art only; Core owns geometry)");
            if (old != null) foreach (var renderer in old.GetComponentsInChildren<SpriteRenderer>()) renderer.enabled = false;
            var root = new GameObject("Premium Arena v3 (presentation only)").transform;
            root.SetParent(transform, false);
            var rules = FindObjectOfType<AppController>().RulesAsset.Rules;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "Lit Artwork Ground"; floor.transform.SetParent(root, false);
            floor.transform.localPosition = new Vector3(0,-0.015f,0);
            floor.transform.localRotation = Quaternion.Euler(90,0,0);
            floor.transform.localScale = ArtworkSize(rules);
            Destroy(floor.GetComponent<Collider>());
            Floor = floor.GetComponent<MeshRenderer>(); Floor.sharedMaterial = material;
            Floor.shadowCastingMode = ShadowCastingMode.Off; Floor.receiveShadows = true;
            Lighting(root,rules);
            var dynamicRoot=new GameObject("Living 3D Stadium"); dynamicRoot.transform.SetParent(root,false);
            Dynamics=dynamicRoot.AddComponent<DynamicStadiumView>(); Dynamics.Initialize(rules,sidelineFills);
            var detailsRoot=new GameObject("Crafted arena architecture"); detailsRoot.transform.SetParent(root,false);
            Details=detailsRoot.AddComponent<ArenaDetailView>(); Details.Initialize(rules);
            float w = rules.FieldWidth / 2, h = rules.FieldHeight / 2, g = rules.GoalWidth / 2;
            Color chalk = new Color(0.94f,0.96f,0.87f,0.88f);
            Path(root,"Upper Field Boundary",chalk,0.065f,-80,new Vec2(-w,-g),new Vec2(-w,-h),new Vec2(w,-h),new Vec2(w,-g));
            Path(root,"Lower Field Boundary",chalk,0.065f,-80,new Vec2(-w,g),new Vec2(-w,h),new Vec2(w,h),new Vec2(w,g));
            Path(root,"Halfway",chalk,0.05f,-80,new Vec2(0,-h),new Vec2(0,h));
            var ring = ViewLines.Create(root,"Center Circle",0.06f,chalk,-80); ring.positionCount = 97;
            for (int i = 0; i < 97; i++)
            { float a = i*Mathf.PI*2/96; ring.SetPosition(i,ArenaProjection.ToWorld(new Vec2(Mathf.Cos(a),Mathf.Sin(a))*rules.CenterCircleRadius,0.012f)); }
            for(int sx=-1;sx<=1;sx+=2) for(int sy=-1;sy<=1;sy+=2)
            {
                var corner=ViewLines.Create(root,"Corner quarter circle",.045f,chalk,-80); corner.positionCount=25;
                for(int i=0;i<25;i++)
                {
                    float a=i*Mathf.PI*.5f/24;
                    corner.SetPosition(i,ArenaProjection.ToWorld(new Vec2(sx*(w-Mathf.Cos(a)*.7f),sy*(h-Mathf.Sin(a)*.7f)),.012f));
                }
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Path(root,"Goal Area "+side,new Color(chalk.r,chalk.g,chalk.b,0.65f),0.05f,-80,
                    new Vec2(w*side,-4.5f),new Vec2((w-3)*side,-4.5f),new Vec2((w-3)*side,4.5f),new Vec2(w*side,4.5f));
                Goal(root,side,w,g,rules.GoalDepth,2); // 高度仅用于三维美术。
                Dynamics.CreateGoalNet(side,w,g,rules.GoalDepth,2);
            }
            Ready = true;
        }
        public static Vector3 ArtworkSize(MatchRules rules) => new Vector3(rules.FieldWidth*1.25f,
            rules.FieldHeight*1.25f/ArenaProjection.GroundYScale,1);
        private void Lighting(Transform root,MatchRules rules)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.38f,0.48f,0.57f);
            RenderSettings.ambientEquatorColor = new Color(0.29f,0.37f,0.43f);
            RenderSettings.ambientGroundColor = new Color(0.16f,0.22f,0.26f);
            var key = new GameObject("Warm Stadium Key"); key.transform.SetParent(root,false);
            key.transform.rotation = Quaternion.LookRotation(new Vector3(6,-12,7));
            KeyLight = key.AddComponent<Light>(); KeyLight.type = LightType.Directional;
            KeyLight.color = new Color(1,0.97f,0.90f); KeyLight.intensity = 0.92f;
            KeyLight.shadows = LightShadows.Soft; KeyLight.shadowStrength = 0.65f;
            KeyLight.shadowBias = 0.025f; KeyLight.shadowNormalBias = 0.025f;
            KeyLight.shadowResolution = LightShadowResolution.VeryHigh;
            float x=rules.FieldWidth*.5f+2;
            sidelineFills[0]=Fill(root,"Cyan Sideline Fill",new Vector3(-x,8,1),new Color(0.77f,0.89f,0.87f));
            sidelineFills[1]=Fill(root,"Amber Sideline Fill",new Vector3(x,8,-1),new Color(1,0.88f,0.69f));
        }
        private static Light Fill(Transform root, string label, Vector3 position, Color color)
        {
            var go = new GameObject(label); go.transform.SetParent(root,false); go.transform.localPosition = position;
            var light = go.AddComponent<Light>(); light.type = LightType.Point; light.color = color;
            light.intensity = .60f; light.range = 19; light.renderMode = LightRenderMode.ForcePixel;
            return light;
        }
        private static void Path(Transform root, string name, Color color, float width, int order, params Vec2[] points)
        {
            var line = ViewLines.Create(root,name,width,color,order); line.positionCount = points.Length;
            for (int i=0;i<points.Length;i++) line.SetPosition(i,ArenaProjection.ToWorld(points[i],0.012f));
            line.numCornerVertices = 4; line.numCapVertices = 4;
        }
        private static void Goal(Transform root, int side, float w, float g, float depth, float height)
        {
            var material = Resources.Load<Material>("SceneSurface/"+(side<0?"Goal-Cyan":"Goal-Amber"));
            var a = ArenaProjection.ToWorld(new Vec2(w*side,-g)); var b = ArenaProjection.ToWorld(new Vec2(w*side,g));
            var c = a+Vector3.right*side*depth; var d = b+Vector3.right*side*depth;
            Bar(root,"Goal Near Post",a,a+Vector3.up*height,0.085f,material);
            Bar(root,"Goal Far Post",b,b+Vector3.up*height,0.085f,material);
            Bar(root,"Goal Crossbar",a+Vector3.up*height,b+Vector3.up*height,0.085f,material);
            Bar(root,"Goal Rear Brace",c+Vector3.up*0.42f,d+Vector3.up*0.42f,0.04f,material);
            Bar(root,"Goal Near Support",a+Vector3.up*height,c+Vector3.up*0.42f,0.04f,material);
            Bar(root,"Goal Far Support",b+Vector3.up*height,d+Vector3.up*0.42f,0.04f,material);
        }
        private static void Bar(Transform root, string label, Vector3 a, Vector3 b, float radius, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = label; go.transform.SetParent(root,false);
            go.transform.position = (a+b)/2; go.transform.rotation = Quaternion.FromToRotation(Vector3.up,b-a);
            go.transform.localScale = new Vector3(radius*2,(b-a).magnitude/2,radius*2);
            Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
        }
    }
}
