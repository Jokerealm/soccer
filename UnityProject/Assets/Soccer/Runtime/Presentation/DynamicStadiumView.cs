using System.Collections.Generic;
using Soccer.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soccer.Runtime
{
    /// <summary>立体场景和场边动画只参与展示；无 Collider，不改变二维比赛规则。</summary>
    public sealed class DynamicStadiumView : MonoBehaviour
    {
        public MeshRenderer Turf { get; private set; }
        public float PresentationTime { get; private set; }
        public int CrowdCount => crowd.Count;
        public int FlagCount => flags.Count;
        public int DroneCount => drones.Count;
        public int ConfettiCount => confetti == null ? 0 : confetti.particleCount;
        public float GoalReactionRemaining { get; private set; }
        public float NetDeflection { get; private set; }
        public GoalCelebrationView Celebration { get; private set; }
        public Transform FirstDrone => drones[0].Body;
        public int FootprintCount { get; private set; }
        private readonly Vector4[] footprints=new Vector4[16];
        private readonly Dictionary<int,Vec2> lastFootPosition=new Dictionary<int,Vec2>();
        private int nextFoot;
        private readonly List<Material> materials=new List<Material>();
        private readonly List<Mesh> meshes=new List<Mesh>();
        private readonly Dictionary<Vector3,Mesh> boxes=new Dictionary<Vector3,Mesh>();
        private readonly List<Spectator> crowd=new List<Spectator>();
        private readonly List<MeshRenderer> flags=new List<MeshRenderer>();
        private readonly List<Drone> drones=new List<Drone>();
        private readonly List<Net> nets=new List<Net>();
        private Material turf,grass,cyanFlag,amberFlag,cyanLED,amberLED,dark,ivory,gold,cyan,amber;
        private ParticleSystem confetti;
        private Light[] fills;
        private int reactingGoal;
        private float fieldHalfWidth,goalHalfWidth;
        private sealed class Spectator { public Transform Root,LeftArm,RightArm; public Vector3 Position; public float Phase; }
        private sealed class Drone { public Transform Body,LeftRotor,RightRotor; public Vector3 Position; public float Phase; }
        private sealed class Net { public LineRenderer Line; public Vector3[] Rest,Animated; public int Side; }

        public void Initialize(MatchRules rules,Light[] sidelineFills)
        {
            fills=sidelineFills;
            dark=Surface("Stadium dark alloy",new Color(.07f,.14f,.17f),.40f);
            ivory=Surface("Stadium warm ivory",new Color(.77f,.81f,.73f),.25f);
            gold=Surface("Stadium champagne trim",new Color(.86f,.62f,.29f),.55f);
            cyan=Surface("Stadium cyan",new Color(.11f,.57f,.51f),.28f);
            amber=Surface("Stadium amber",new Color(.88f,.43f,.16f),.28f);
            turf=Custom("SceneSurface/LivingTurf");
            turf.mainTexture=Resources.Load<Texture2D>("SceneSurface/turf-detail-v4");
            grass=Custom("SceneSurface/ArenaWind"); grass.SetColor("_Color",new Color(.20f,.36f,.23f)); grass.SetFloat("_WindStrength",.022f);
            cyanFlag=Custom("SceneSurface/ArenaWind"); cyanFlag.SetColor("_Color",new Color(.11f,.66f,.57f));
            amberFlag=Custom("SceneSurface/ArenaWind"); amberFlag.SetColor("_Color",new Color(.94f,.49f,.17f));
            cyanLED=Custom("SceneSurface/StadiumLED"); cyanLED.SetColor("_TeamColor",new Color(.24f,.88f,.73f));
            amberLED=Custom("SceneSurface/StadiumLED"); amberLED.SetColor("_TeamColor",new Color(1,.67f,.26f));
            float w=rules.FieldWidth/2,h=rules.FieldHeight/2;
            fieldHalfWidth=w; goalHalfWidth=rules.GoalWidth*.5f;
            turf.SetVector("_FieldSize",new Vector4(rules.FieldWidth,rules.FieldHeight,rules.CenterCircleRadius,0));
            Turf=MeshObject("3D grass playing surface",transform,RoundedGround(w+.30f,h+.30f,.45f,-.008f),turf);
            GrassBlades(w,h);
            for(int side=-1;side<=1;side+=2)
            {
                for(int bay=0;bay<4;bay++)
                {
                    float x=new[]{-.75f,-.34375f,.34375f,.75f}[bay]*w;
                    Stand(x,side,h);
                    Flag(new Vector3(x-2.35f,.05f,side*(h+2.25f)),side<0 ? amberFlag : cyanFlag);
                }
                for(int board=0;board<8;board++)
                {
                    float boardWidth=rules.FieldWidth/8-.45f;
                    Vector3 p=new Vector3(w*(-.875f+board*.25f),.22f,side*(h+.57f));
                    Box("LED board housing",transform,p,new Vector3(boardWidth,.28f,.18f),dark);
                    Box("LED gold lower trim",transform,p+Vector3.down*.16f,new Vector3(boardWidth+.10f,.055f,.24f),gold);
                    var screen=Primitive("Animated LED arrows",PrimitiveType.Quad,transform,p+new Vector3(0,.10f,-.11f),
                        new Vector3(boardWidth-.19f,.25f,1),board%2==0 ? cyanLED : amberLED);
                    screen.localRotation=Quaternion.Euler(55,0,0);
                }
                DroneRig(new Vector3(side*(w+2),1.3f,side*4.2f),side<0 ? cyan : amber,side*1.7f);
                // 球门底座覆盖原图中的平面门区，接上真实立柱与球网。
                Box("Raised goal apron",transform,new Vector3(side*(w+rules.GoalDepth*.5f),-.028f,0),
                    new Vector3(rules.GoalDepth+.1f,.06f,rules.GoalWidth+.5f),cyan);
                Box("Goal threshold inlay",transform,new Vector3(side*(w+.10f),.009f,0),
                    new Vector3(.13f,.018f,rules.GoalWidth+.14f),side<0 ? cyan : amber);
            }
            CreateConfetti();
            var celebration=new GameObject("Goal flags and ribbons pool");celebration.transform.SetParent(transform,false);
            Celebration=celebration.AddComponent<GoalCelebrationView>();Celebration.Initialize(rules);
        }

        private void Stand(float x,int side,float h)
        {
            var root=Child("Tiered stand "+side+" "+x,transform);
            for(int row=0;row<3;row++)
            {
                float z=side*(h+1.00f+row*.48f),y=.05f+row*.22f;
                Box("Beveled terrace",root,new Vector3(x,y-.08f,z),new Vector3(5.5f,.16f,.53f),dark);
                Box("Terrace golden lip",root,new Vector3(x,y+.012f,z-side*.25f),new Vector3(5.52f,.055f,.035f),gold);
                for(int seat=0;seat<6;seat++)
                {
                    float sx=x-2.2f+seat*.88f;
                    Box("Rounded stadium seat",root,new Vector3(sx,y+.065f,z),new Vector3(.39f,.08f,.33f),side>0 ? cyan : amber);
                    Box("Seat back",root,new Vector3(sx,y+.18f,z+side*.16f),new Vector3(.39f,.22f,.075f),side>0 ? cyan : amber);
                    if((row==0 && seat%2==0) || (row==2 && seat%2==1)) SpectatorRig(root,new Vector3(sx,y+.10f,z),side,crowd.Count*.73f);
                }
            }
        }
        private void SpectatorRig(Transform parent,Vector3 position,int side,float phase)
        {
            var root=Child("Waving stadium spectator",parent); root.localPosition=position;
            root.localRotation=Quaternion.Euler(0,side>0 ? 180 : 0,0);
            var shirt=crowd.Count%3==0 ? gold : side>0 ? cyan : amber;
            Primitive("Round mascot body",PrimitiveType.Capsule,root,new Vector3(0,.16f,0),new Vector3(.22f,.17f,.18f),shirt);
            Primitive("Mascot head",PrimitiveType.Sphere,root,new Vector3(0,.40f,-.015f),Vector3.one*.24f,ivory);
            Box("Visor",root,new Vector3(0,.41f,.10f),new Vector3(.15f,.055f,.025f),dark);
            var left=Child("Left waving arm",root); left.localPosition=new Vector3(-.15f,.29f,0);
            var right=Child("Right waving arm",root); right.localPosition=new Vector3(.15f,.29f,0);
            Primitive("Left glove arm",PrimitiveType.Capsule,left,new Vector3(0,.075f,0),new Vector3(.075f,.095f,.075f),shirt);
            Primitive("Right glove arm",PrimitiveType.Capsule,right,new Vector3(0,.075f,0),new Vector3(.075f,.095f,.075f),shirt);
            crowd.Add(new Spectator {Root=root,LeftArm=left,RightArm=right,Position=position,Phase=phase});
        }
        private void Flag(Vector3 position,Material cloth)
        {
            var root=Child("Wind driven team flag",transform); root.localPosition=position;
            Primitive("Flag pole",PrimitiveType.Cylinder,root,new Vector3(0,.69f,0),new Vector3(.045f,.69f,.045f),gold);
            Primitive("Pole cap",PrimitiveType.Sphere,root,new Vector3(0,1.40f,0),Vector3.one*.11f,gold);
            var renderer=MeshObject("Deforming cloth",root,ClothMesh(1.12f,.57f),cloth);
            renderer.transform.localPosition=new Vector3(.04f,.80f,0); flags.Add(renderer);
        }
        private void DroneRig(Vector3 position,Material trim,float phase)
        {
            var body=Child("Hovering stadium camera drone",transform); body.localPosition=position;
            Primitive("Drone shell",PrimitiveType.Sphere,body,Vector3.zero,new Vector3(.50f,.24f,.36f),ivory);
            Box("Drone camera visor",body,new Vector3(0,-.04f,-.17f),new Vector3(.24f,.11f,.055f),dark);
            Primitive("Camera lens",PrimitiveType.Sphere,body,new Vector3(0,-.04f,-.22f),Vector3.one*.075f,trim);
            Box("Rotor crossbeam",body,new Vector3(0,.08f,0),new Vector3(.92f,.045f,.07f),gold);
            var left=Child("Left spinning rotor",body); left.localPosition=new Vector3(-.43f,.13f,0);
            var right=Child("Right spinning rotor",body); right.localPosition=new Vector3(.43f,.13f,0);
            Box("Rotor blade",left,Vector3.zero,new Vector3(.40f,.035f,.08f),trim);
            Box("Rotor blade",right,Vector3.zero,new Vector3(.40f,.035f,.08f),trim);
            drones.Add(new Drone {Body=body,LeftRotor=left,RightRotor=right,Position=position,Phase=phase});
        }

        public void CreateGoalNet(int side,float w,float g,float depth,float height)
        {
            var a=new Vector3(side*w,0,-g); var b=new Vector3(side*w,0,g);
            var c=a+Vector3.right*side*depth; var d=b+Vector3.right*side*depth;
            for(int i=0;i<=16;i++) NetLine(side,Vector3.Lerp(a,b,i/16f)+Vector3.up*height,Vector3.Lerp(c,d,i/16f)+Vector3.up*.42f);
            for(int i=0;i<=7;i++) NetLine(side,Vector3.Lerp(a,c,i/7f)+Vector3.up*Mathf.Lerp(height,.42f,i/7f),
                Vector3.Lerp(b,d,i/7f)+Vector3.up*Mathf.Lerp(height,.42f,i/7f));
        }
        private void NetLine(int side,Vector3 a,Vector3 b)
        {
            var line=ViewLines.Create(transform,"Reactive goal net",.018f,new Color(.84f,.94f,.87f,.57f),-60);
            line.positionCount=9;
            var rest=new Vector3[9]; for(int i=0;i<9;i++) rest[i]=Vector3.Lerp(a,b,i/8f);
            line.SetPositions(rest); nets.Add(new Net {Line=line,Side=side,Rest=rest,Animated=new Vector3[9]});
        }

        public void Render(MatchState state,bool animate)
        {
            Celebration?.Render(state,animate);
            if(animate)
            {
                float dt=Mathf.Min(Time.unscaledDeltaTime,.1f);
                PresentationTime+=dt; GoalReactionRemaining=Mathf.Max(0,GoalReactionRemaining-dt);
                UpdateFootprints(state,dt);
            }
            float t=PresentationTime,cheer=GoalReactionRemaining/2.2f;
            turf.SetFloat("_ArenaTime",t); grass.SetFloat("_ArenaTime",t);
            turf.SetVectorArray("_Footprints",footprints);
            cyanFlag.SetFloat("_ArenaTime",t); amberFlag.SetFloat("_ArenaTime",t);
            cyanLED.SetFloat("_ArenaTime",t); amberLED.SetFloat("_ArenaTime",t);
            cyanLED.SetFloat("_Celebration",reactingGoal>0 ? cheer : 0);
            amberLED.SetFloat("_Celebration",reactingGoal<0 ? cheer : 0);
            cyanFlag.SetFloat("_WindStrength",.14f+cheer*.06f); amberFlag.SetFloat("_WindStrength",.14f+cheer*.06f);
            foreach(var person in crowd)
            {
                float wave=Mathf.Sin(t*2.1f+person.Phase);
                person.LeftArm.localRotation=Quaternion.Euler(0,0,12+wave*28-cheer*30);
                person.RightArm.localRotation=Quaternion.Euler(0,0,-12+Mathf.Sin(t*2.1f+person.Phase+.8f)*28+cheer*30);
                person.Root.localPosition=person.Position+Vector3.up*(Mathf.Max(0,wave)*.045f+cheer*Mathf.Abs(Mathf.Sin(t*8+person.Phase))*.10f);
            }
            foreach(var drone in drones)
            {
                drone.Body.localPosition=drone.Position+new Vector3(Mathf.Sin(t*.65f+drone.Phase)*.65f,Mathf.Sin(t*1.8f+drone.Phase)*.13f,Mathf.Cos(t*.65f+drone.Phase)*.12f);
                drone.Body.localRotation=Quaternion.Euler(Mathf.Sin(t+drone.Phase)*4,Mathf.Sin(t*.5f+drone.Phase)*12,Mathf.Cos(t+drone.Phase)*4);
                drone.LeftRotor.localRotation=Quaternion.Euler(0,t*900,0); drone.RightRotor.localRotation=Quaternion.Euler(0,-t*900,0);
            }
            NetDeflection=0;
            foreach(var net in nets)
            {
                float amplitude=net.Side==reactingGoal ? Mathf.Sin((2.2f-GoalReactionRemaining)*22)*cheer*.28f : 0;
                for(int i=0;i<9;i++)
                {
                    float bend=Mathf.Sin(i/8f*Mathf.PI)*amplitude;
                    net.Animated[i]=net.Rest[i]+new Vector3(net.Side*bend,0,0);
                    NetDeflection=Mathf.Max(NetDeflection,Mathf.Abs(bend));
                }
                net.Line.SetPositions(net.Animated);
            }
            for(int i=0;i<fills.Length;i++)
            {
                fills[i].intensity=.60f+Mathf.Sin(t*.8f+i*2)*.035f+cheer*.08f;
                fills[i].transform.localPosition=new Vector3((i==0 ? -1 : 1)*(fieldHalfWidth+2),8,Mathf.Sin(t*.4f+i*2)*2);
            }
            if(confetti!=null)
            { if(animate && !confetti.isPlaying) confetti.Play(); else if(!animate && !confetti.isPaused) confetti.Pause(); }
        }
        public void Goal(Team team)
        {
            reactingGoal=team==Team.Cyan ? 1 : -1; GoalReactionRemaining=2.2f;
            Celebration?.Goal(team);
            var emit=new ParticleSystem.EmitParams();
            for(int i=0;i<64;i++)
            {
                float a=i*2.39996f;
                emit.position=new Vector3(reactingGoal*(fieldHalfWidth+.8f),.8f,Mathf.Sin(a)*(goalHalfWidth-.05f));
                emit.velocity=new Vector3(-reactingGoal*(.3f+Mathf.Abs(Mathf.Cos(a))),2.3f+Mathf.Abs(Mathf.Sin(a))*1.5f,Mathf.Sin(a)*1.3f);
                emit.startColor=i%3==0 ? new Color(1,.80f,.36f) : team==Team.Cyan ? new Color(.4f,1,.84f) : new Color(1,.59f,.30f);
                emit.startLifetime=1.1f+(i%7)*.09f; emit.startSize=.07f+(i%4)*.025f;
                confetti.Emit(emit,1);
            }
        }
        private void UpdateFootprints(MatchState state,float dt)
        {
            for(int i=0;i<footprints.Length;i++)
                footprints[i].z=Mathf.Max(0,footprints[i].z-dt/5);
            foreach(var p in state.Players)
            {
                if(!lastFootPosition.TryGetValue(p.Id,out var previous)) {lastFootPosition[p.Id]=p.Position;continue;}
                float step=Vec2.Distance(previous,p.Position);
                if(p.IsDead || step>2) {lastFootPosition[p.Id]=p.Position;continue;}
                if(step<.45f) continue;
                var f=(p.Position-previous).Normalized;
                var foot=p.Position+new Vec2(-f.Y,f.X)*(nextFoot%2==0 ? .14f : -.14f);
                footprints[nextFoot]=new Vector4(foot.X,foot.Y,1,-Mathf.Atan2(f.X,f.Y));
                nextFoot=(nextFoot+1)%footprints.Length; lastFootPosition[p.Id]=p.Position;
            }
            FootprintCount=0;
            foreach(var foot in footprints) if(foot.z>0) FootprintCount++;
        }
        public void ResetMatch()
        {
            GoalReactionRemaining=0; NetDeflection=0; reactingGoal=0; confetti?.Clear();
            Celebration?.Clear();
            System.Array.Clear(footprints,0,footprints.Length); nextFoot=FootprintCount=0; lastFootPosition.Clear();
            turf?.SetVectorArray("_Footprints",footprints);
        }
        private void CreateConfetti()
        {
            var go=new GameObject("Stadium goal celebration pool"); go.transform.SetParent(transform,false);
            confetti=go.AddComponent<ParticleSystem>();
            var main=confetti.main; main.playOnAwake=false; main.loop=false; main.maxParticles=96;
            main.simulationSpace=ParticleSystemSimulationSpace.World; main.gravityModifier=.35f;
            var emission=confetti.emission; emission.enabled=false;
            var shape=confetti.shape; shape.enabled=false;
            var color=confetti.colorOverLifetime; color.enabled=true;
            var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)}); color.color=gradient;
            var renderer=go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=Resources.Load<Material>("SceneSurface/Combat-Sparks");
            renderer.sortingOrder=100; renderer.shadowCastingMode=ShadowCastingMode.Off;
            confetti.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void GrassBlades(float w,float h)
        {
            var vertices=new List<Vector3>(); var uv=new List<Vector2>(); var triangles=new List<int>();
            for(int i=0;i<900;i++)
            {
                float x=(Mathf.Repeat(i*.618034f,1)*2-1)*(w-.12f),z=(Mathf.Repeat(i*.754877f,1)*2-1)*(h-.12f);
                if(Mathf.Abs(x)<w*.94f && Mathf.Abs(z)<h*.91f) continue;
                int n=vertices.Count; float height=.04f+(i%5)*.006f;
                vertices.Add(new Vector3(x-.018f,-.004f,z)); vertices.Add(new Vector3(x+.018f,-.004f,z)); vertices.Add(new Vector3(x+.010f,height,z));
                uv.Add(Vector2.zero); uv.Add(Vector2.zero); uv.Add(new Vector2(1,.3f));
                triangles.Add(n); triangles.Add(n+2); triangles.Add(n+1);
            }
            var mesh=new Mesh {name="900 individual 3D grass blades"}; mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); meshes.Add(mesh);
            var normals=new Vector3[vertices.Count]; for(int i=0;i<normals.Length;i++) normals[i]=Vector3.up; mesh.normals=normals;
            var r=MeshObject("Wind driven grass geometry",transform,mesh,grass); r.shadowCastingMode=ShadowCastingMode.Off;
        }
        private Mesh RoundedGround(float w,float h,float radius,float y)
        {
            var vertices=new List<Vector3> {new Vector3(0,y,0)}; var uv=new List<Vector2> {new Vector2(.5f,.5f)};
            for(int corner=0;corner<4;corner++)
            {
                float angle=corner*90;
                Vector2 center=corner==0 ? new Vector2(w-radius,h-radius) : corner==1 ? new Vector2(-w+radius,h-radius) :
                    corner==2 ? new Vector2(-w+radius,-h+radius) : new Vector2(w-radius,-h+radius);
                for(int i=0;i<=8;i++)
                {
                    float a=(angle+i*90f/8)*Mathf.Deg2Rad;
                    Vector2 p=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;
                    vertices.Add(new Vector3(p.x,y,p.y)); uv.Add(new Vector2((p.x/w+1)*.5f,(p.y/h+1)*.5f));
                }
            }
            var indices=new List<int>(); for(int i=1;i<vertices.Count;i++) {indices.Add(0); indices.Add(i==vertices.Count-1 ? 1 : i+1); indices.Add(i);}
            var mesh=new Mesh {name="Rounded live turf mesh"}; mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(indices,0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); meshes.Add(mesh); return mesh;
        }
        private Mesh ClothMesh(float width,float height)
        {
            var v=new List<Vector3>(); var uv=new List<Vector2>(); var indices=new List<int>();
            const int cols=12,rows=4;
            for(int y=0;y<=rows;y++) for(int x=0;x<=cols;x++) {v.Add(new Vector3(x*width/cols,y*height/rows,0)); uv.Add(new Vector2(x/(float)cols,y/(float)rows));}
            for(int y=0;y<rows;y++) for(int x=0;x<cols;x++)
            {int a=y*(cols+1)+x,b=a+1,c=a+cols+1,d=c+1; indices.AddRange(new[]{a,c,b,b,c,d});}
            var mesh=new Mesh {name="Pinned team flag cloth"}; mesh.SetVertices(v); mesh.SetUVs(0,uv); mesh.SetTriangles(indices,0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); meshes.Add(mesh); return mesh;
        }
        private Material Custom(string resource)
        {
            var shader=Resources.Load<Shader>(resource); var material=new Material(shader) {name=resource}; materials.Add(material); return material;
        }
        private Material Surface(string name,Color color,float metallic)
        {
            var material=new Material(Shader.Find("Standard")) {name=name,color=color};
            material.SetFloat("_Metallic",metallic); material.SetFloat("_Glossiness",.38f); materials.Add(material); return material;
        }
        private static Transform Child(string name,Transform parent)
        { var go=new GameObject(name); go.transform.SetParent(parent,false); return go.transform; }
        private MeshRenderer MeshObject(string name,Transform parent,Mesh mesh,Material material)
        {
            var go=Child(name,parent).gameObject; go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.On; renderer.receiveShadows=true; return renderer;
        }
        private Transform Box(string name,Transform parent,Vector3 position,Vector3 size,Material material)
        {
            if(!boxes.TryGetValue(size,out var mesh))
            {mesh=PlayerModelView.BeveledBox(size,Mathf.Min(.04f,Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.20f)); boxes.Add(size,mesh); meshes.Add(mesh);}
            var renderer=MeshObject(name,parent,mesh,material); renderer.transform.localPosition=position; return renderer.transform;
        }
        private static Transform Primitive(string name,PrimitiveType type,Transform parent,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(type); go.name=name; go.transform.SetParent(parent,false);
            go.transform.localPosition=position; go.transform.localScale=scale;
            var collider=go.GetComponent<Collider>(); if(collider!=null) {collider.enabled=false; Destroy(collider);}
            var renderer=go.GetComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.On; renderer.receiveShadows=true; return go.transform;
        }
        private void OnDestroy()
        {foreach(var material in materials) Destroy(material); foreach(var mesh in meshes) Destroy(mesh);}
    }
}
