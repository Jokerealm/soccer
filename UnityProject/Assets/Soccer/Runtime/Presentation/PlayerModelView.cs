using System.Collections.Generic;
using Soccer.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soccer.Runtime
{
    /// <summary>可替换的临时立体造型；模型尺寸和动画不参与平面玩法判定。</summary>
    public sealed class PlayerModelView : MonoBehaviour
    {
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
        private readonly List<Color> colors = new List<Color>();
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private Transform rig, leftLeg, rightLeg, gunRig;
        private GameObject jets;
        private bool thor;
        public int PartCount => renderers.Count;
        public bool IsThor => thor;
        public bool HasRealtimeShadows => renderers.TrueForAll(r => r.shadowCastingMode == ShadowCastingMode.On && r.receiveShadows && r.sharedMaterial.shader.name == "Standard");

        public void Initialize(CharacterStats stats, Team team)
        {
            thor = stats.Id == CharacterId.Thor;
            rig = Child("Animated Body", transform);
            Color teamColor = team == Team.Cyan ? new Color(.22f,.74f,.72f) : new Color(.93f,.51f,.20f);
            var armor = Material("Team Armor", teamColor, .38f, .48f);
            var pale = Material("Ivory Armor", new Color(.77f,.82f,.77f), .35f, .45f);
            var dark = Material("Joints and Weapon", new Color(.055f,.10f,.14f), .5f, .4f);
            var metal = Material("Gun Metal", new Color(.30f,.39f,.43f), .7f, .55f);
            var glow = Material("Visor and Reactor", new Color(.72f,.98f,.91f), .2f, .65f);
            glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor", teamColor * .8f);
            leftLeg = Child("Left Leg", rig); rightLeg = Child("Right Leg", rig);
            gunRig = Child("Weapon Recoil", rig);
            if (thor)
            {
                MakeLeg(leftLeg, -.32f, .62f, true, armor, dark, pale);
                MakeLeg(rightLeg, .32f, .62f, true, armor, dark, pale);
                Box("Core Chassis", rig, new Vector3(0,1.02f,0), new Vector3(.95f,.70f,.66f), pale);
                Box("Chest Plate", rig, new Vector3(0,1.07f,.34f), new Vector3(.76f,.41f,.13f), armor);
                Box("Reactor", rig, new Vector3(0,1.10f,.42f), new Vector3(.24f,.17f,.06f), glow);
                Box("Waist", rig, new Vector3(0,.62f,0), new Vector3(.62f,.18f,.45f), dark);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Box("Shoulder Armor", rig, new Vector3(sign*.61f,1.20f,0), new Vector3(.38f,.48f,.55f), armor);
                    Box("Heavy Arm", rig, new Vector3(sign*.66f,.82f,.11f), new Vector3(.29f,.40f,.37f), metal);
                    Cylinder("Twin Cannon", gunRig, new Vector3(sign*.48f,1.55f,.33f), new Vector3(.24f,.51f,.24f), metal, Quaternion.Euler(90,0,0));
                    Cylinder("Cannon Muzzle", gunRig, new Vector3(sign*.48f,1.55f,.83f), new Vector3(.31f,.08f,.31f), dark, Quaternion.Euler(90,0,0));
                    Cylinder("Muzzle Inset", gunRig, new Vector3(sign*.48f,1.55f,.91f), new Vector3(.17f,.01f,.17f), glow, Quaternion.Euler(90,0,0));
                }
                Box("Cockpit", rig, new Vector3(0,1.58f,-.04f), new Vector3(.51f,.36f,.46f), pale);
                Box("Cockpit Visor", rig, new Vector3(0,1.59f,.20f), new Vector3(.39f,.13f,.055f), glow);
                Box("Back Engine", rig, new Vector3(0,1.05f,-.43f), new Vector3(.66f,.57f,.23f), dark);
            }
            else
            {
                MakeLeg(leftLeg, -.17f, .68f, false, armor, dark, pale);
                MakeLeg(rightLeg, .17f, .68f, false, armor, dark, pale);
                Box("Torso", rig, new Vector3(0,1.04f,0), new Vector3(.59f,.61f,.40f), pale);
                Box("Chest Armor", rig, new Vector3(0,1.13f,.20f), new Vector3(.48f,.30f,.10f), armor);
                Box("Belt", rig, new Vector3(0,.76f,0), new Vector3(.55f,.12f,.43f), dark);
                Sphere("Helmet", rig, new Vector3(0,1.54f,0), new Vector3(.50f,.47f,.50f), pale);
                Box("Helmet Stripe", rig, new Vector3(0,1.76f,0), new Vector3(.13f,.07f,.34f), armor);
                Box("Visor", rig, new Vector3(0,1.56f,.23f), new Vector3(.42f,.14f,.08f), glow);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Sphere("Shoulder", rig, new Vector3(sign*.36f,1.25f,0), new Vector3(.28f,.30f,.31f), armor);
                    Box("Arm", rig, new Vector3(sign*.36f,1.01f,.17f), new Vector3(.19f,.38f,.23f), pale);
                    Sphere("Glove", rig, new Vector3(sign*.33f,.88f,.34f), new Vector3(.21f,.19f,.22f), dark);
                }
                Box("Pulse Rifle", gunRig, new Vector3(.23f,.98f,.58f), new Vector3(.22f,.23f,.62f), metal);
                Box("Rifle Energy Cell", gunRig, new Vector3(.23f,1.10f,.56f), new Vector3(.11f,.08f,.30f), glow);
                Cylinder("Rifle Muzzle", gunRig, new Vector3(.23f,.98f,.93f), new Vector3(.19f,.09f,.19f), dark, Quaternion.Euler(90,0,0));
                Box("Backpack", rig, new Vector3(0,1.08f,-.27f), new Vector3(.39f,.47f,.23f), armor);
            }
            jets = new GameObject("Speed Boost Jets"); jets.transform.SetParent(rig, false);
            for (int sign = -1; sign <= 1; sign += 2)
                Sphere("Jet Glow", jets.transform, new Vector3(sign*(thor ? .22f : .13f),.90f,thor ? -.65f : -.43f), new Vector3(.15f,.15f,.35f), glow);
            jets.SetActive(false);
        }

        private void MakeLeg(Transform leg, float x, float y, bool heavy, Material armor, Material dark, Material pale)
        {
            leg.localPosition = new Vector3(x,y,0);
            Box("Leg Joint", leg, new Vector3(0,-.15f,0), new Vector3(heavy ? .29f : .20f,.31f,.25f), dark);
            Box("Shin Armor", leg, new Vector3(0,-.37f,.04f), new Vector3(heavy ? .33f : .23f,.32f,.31f), armor);
            Box("Boot", leg, new Vector3(0,-y+.13f,.14f), new Vector3(heavy ? .45f : .28f,.24f,heavy ? .59f : .43f), pale);
        }

        public void Render(PlayerState state, Vec2 facing, float time, bool animate, float impact, Vec2 impactDirection,
            float recoil, Vec2 recoilDirection, float flash, Color flashColor)
        {
            var target = Quaternion.Euler(0,Mathf.Atan2(facing.X,facing.Y)*Mathf.Rad2Deg,0);
            transform.localRotation = animate ? Quaternion.Slerp(transform.localRotation,target,1-Mathf.Exp(-22*Time.deltaTime)) : target;
            bool walking = !state.IsDead && state.Velocity.Length > .1f;
            float step = walking ? Mathf.Sin(time*(thor ? 11 : 15)) : 0;
            leftLeg.localRotation = Quaternion.Euler(step*14,0,0);
            rightLeg.localRotation = Quaternion.Euler(-step*14,0,0);
            // 反馈仅改变网格姿态，角色位置和碰撞仍由平面模拟决定。
            var offset = ArenaProjection.ToWorld(impactDirection*(impact*.10f)-recoilDirection*(recoil*.06f));
            rig.localPosition = transform.InverseTransformVector(offset) + Vector3.up*(walking ? Mathf.Abs(step)*.035f : 0);
            rig.localScale = state.IsDead ? new Vector3(1,.22f,1) : new Vector3(1+impact*.08f,1-impact*.08f,1);
            rig.localRotation = state.IsDead ? Quaternion.identity : Quaternion.Euler(walking ? 3 : 0,0,state.Stun > 0 ? Mathf.Sin(time*28)*4 : 0);
            gunRig.localPosition = Vector3.back*recoil*.12f;
            jets.SetActive(!state.IsDead && state.SkillSpeedBonus > .1f);
            for (int i=0;i<renderers.Count;i++)
            {
                Color tint = state.IsDead ? Color.Lerp(colors[i],new Color(.28f,.33f,.38f),.8f) : state.Stun > 0 ? Color.Lerp(colors[i],new Color(1,.33f,.24f),.35f) : colors[i];
                if (flash > 0 && !state.IsDead) tint = Color.Lerp(tint,flashColor,flash);
                block.SetColor("_Color",tint); renderers[i].SetPropertyBlock(block);
            }
        }

        private static Transform Child(string name, Transform parent)
        { var go = new GameObject(name); go.transform.SetParent(parent,false); return go.transform; }
        private Material Material(string name, Color color, float metallic, float smoothness)
        {
            var material = new Material(Shader.Find("Standard")) { name = name, color = color };
            material.SetFloat("_Metallic",metallic); material.SetFloat("_Glossiness",smoothness);
            materials.Add(material); return material;
        }
        private void Configure(GameObject go, Transform parent, Vector3 position, Material material)
        {
            go.transform.SetParent(parent,false); go.transform.localPosition = position;
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            renderers.Add(renderer); colors.Add(material.color);
            var collider = go.GetComponent<Collider>();
            if (collider != null) { collider.enabled=false; Destroy(collider); }
        }
        private void Box(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var go = new GameObject(name); go.AddComponent<MeshRenderer>();
            var mesh = BeveledBox(size,Mathf.Min(.055f,Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.22f));
            meshes.Add(mesh); go.AddComponent<MeshFilter>().sharedMesh=mesh;
            Configure(go,parent,position,material);
        }
        private void Sphere(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name=name;
            Configure(go,parent,position,material); go.transform.localScale=size;
        }
        private void Cylinder(string name, Transform parent, Vector3 position, Vector3 scale, Material material, Quaternion rotation)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name=name;
            Configure(go,parent,position,material); go.transform.localScale=scale; go.transform.localRotation=rotation;
        }

        internal static Mesh BeveledBox(Vector3 size, float bevel)
        {
            Vector3 half=size*.5f, inset=half-Vector3.one*bevel;
            var vertices=new List<Vector3>(); var normals=new List<Vector3>(); var triangles=new List<int>();
            for (int axis=0;axis<3;axis++)
            {
                int u=(axis+1)%3,v=(axis+2)%3;
                for(int sign=-1;sign<=1;sign+=2)
                {
                    Vector3 a=Vector3.zero; a[axis]=half[axis]*sign; a[u]=-inset[u]; a[v]=-inset[v];
                    Vector3 b=a,c=a,d=a; b[u]=inset[u]; c[u]=inset[u]; c[v]=inset[v]; d[v]=inset[v];
                    Vector3 n=Vector3.zero; n[axis]=sign; Quad(a,b,c,d,n,vertices,normals,triangles);
                }
            }
            for(int free=0;free<3;free++)
            {
                int u=(free+1)%3,v=(free+2)%3;
                for(int su=-1;su<=1;su+=2) for(int sv=-1;sv<=1;sv+=2)
                {
                    Vector3 a=Vector3.zero; a[free]=-inset[free]; a[u]=su*half[u]; a[v]=sv*inset[v];
                    Vector3 b=a,c=a,d=a; b[u]=su*inset[u]; b[v]=sv*half[v]; c=b; c[free]=inset[free]; d[free]=inset[free];
                    Vector3 n=Vector3.zero; n[u]=su; n[v]=sv; Quad(a,b,c,d,n.normalized,vertices,normals,triangles);
                }
            }
            for(int x=-1;x<=1;x+=2) for(int y=-1;y<=1;y+=2) for(int z=-1;z<=1;z+=2)
                Triangle(new Vector3(x*half.x,y*inset.y,z*inset.z),new Vector3(x*inset.x,y*half.y,z*inset.z),new Vector3(x*inset.x,y*inset.y,z*half.z),new Vector3(x,y,z).normalized,vertices,normals,triangles);
            var mesh=new Mesh { name="Beveled Armor" }; mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds(); return mesh;
        }
        private static void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal,List<Vector3> vertices,List<Vector3> normals,List<int> triangles)
        { Triangle(a,b,c,normal,vertices,normals,triangles); Triangle(a,c,d,normal,vertices,normals,triangles); }
        private static void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 normal,List<Vector3> vertices,List<Vector3> normals,List<int> triangles)
        {
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),normal)<0) { var swap=b; b=c; c=swap; }
            int start=vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            triangles.Add(start); triangles.Add(start+1); triangles.Add(start+2);
        }
        private void OnDestroy()
        { foreach(var mesh in meshes) Destroy(mesh); foreach(var material in materials) Destroy(material); }
    }
}
