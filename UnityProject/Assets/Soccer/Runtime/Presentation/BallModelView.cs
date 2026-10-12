using System.Collections.Generic;
using Soccer.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soccer.Runtime
{
    /// <summary>真实球面上的 12 个五边形和 20 个六边形，使用场景灯光与阴影。</summary>
    public sealed class BallModelView : MonoBehaviour
    {
        public const float Radius = 0.27f;
        public int PanelCount => 32;
        public int TriangleCount => ivoryMesh.triangles.Length / 3 + navyMesh.triangles.Length / 3;
        public Quaternion RollingRotation => rolling.rotation;
        private static Mesh ivoryMesh, navyMesh;
        private Transform rolling;
        private Vector3 previous;
        private bool positioned;

        public void Initialize()
        {
            if (ivoryMesh == null) BuildPanels();
            rolling = new GameObject("Rolling Panels").transform;
            rolling.SetParent(transform, false);
            rolling.localRotation = Quaternion.Euler(12, 18, 9);
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Recessed Seams"; core.transform.SetParent(rolling, false);
            core.transform.localScale = Vector3.one * (Radius * 1.96f);
            Destroy(core.GetComponent<Collider>());
            core.GetComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("SceneSurface/Ball-Seam");
            Panel("Ivory Hexagons", ivoryMesh, "Ball-Ivory");
            Panel("Navy Pentagons", navyMesh, "Ball-Navy");
        }

        private void Panel(string label, Mesh mesh, string material)
        {
            var go = new GameObject(label); go.transform.SetParent(rolling, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("SceneSurface/" + material);
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
        }

        public void Render(Vec2 ground, bool animate, float height = 0, float physicsRadius = Radius)
        {
            transform.localScale = Vector3.one * (physicsRadius / Radius);
            var position = ArenaProjection.ToWorld(ground,height) + Vector3.up * physicsRadius;
            var motion = position - previous;
            motion.y = 0;
            // 重开球或训练重置不算滚动；渲染插值中的正常位移按周长旋转。
            if (positioned && animate && motion.sqrMagnitude > 0.0000001f && motion.sqrMagnitude < 9)
                rolling.Rotate(Vector3.Cross(Vector3.up, motion.normalized), motion.magnitude / physicsRadius * Mathf.Rad2Deg, Space.World);
            previous = position; positioned = true; transform.position = position;
        }

        private static void BuildPanels()
        {
            float p = (1 + Mathf.Sqrt(5)) / 2;
            Vector3[] v = {
                new Vector3(-1,p,0), new Vector3(1,p,0), new Vector3(-1,-p,0), new Vector3(1,-p,0),
                new Vector3(0,-1,p), new Vector3(0,1,p), new Vector3(0,-1,-p), new Vector3(0,1,-p),
                new Vector3(p,0,-1), new Vector3(p,0,1), new Vector3(-p,0,-1), new Vector3(-p,0,1)
            };
            for (int i = 0; i < v.Length; i++) v[i].Normalize();
            int[] faces = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var white = new List<Vector3>(); var dark = new List<Vector3>();
            var neighbors = new HashSet<int>[12];
            for (int i = 0; i < 12; i++) neighbors[i] = new HashSet<int>();
            for (int i = 0; i < faces.Length; i += 3)
            {
                int a = faces[i], b = faces[i+1], c = faces[i+2];
                neighbors[a].Add(b); neighbors[a].Add(c); neighbors[b].Add(a);
                neighbors[b].Add(c); neighbors[c].Add(a); neighbors[c].Add(b);
                Polygon(white, new[] { Edge(v[a],v[b]), Edge(v[b],v[a]), Edge(v[b],v[c]),
                    Edge(v[c],v[b]), Edge(v[c],v[a]), Edge(v[a],v[c]) });
            }
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 axis = v[i], tangent = Vector3.Cross(axis, Vector3.up).normalized;
                Vector3 bitangent = Vector3.Cross(axis, tangent);
                var corners = new List<Vector3>();
                foreach (int n in neighbors[i]) corners.Add(Edge(axis,v[n]));
                corners.Sort((a,b) => Mathf.Atan2(Vector3.Dot(a,bitangent),Vector3.Dot(a,tangent))
                    .CompareTo(Mathf.Atan2(Vector3.Dot(b,bitangent),Vector3.Dot(b,tangent))));
                Polygon(dark,corners.ToArray());
            }
            ivoryMesh = MeshFrom("Football 20 Rounded Hexagons", white);
            navyMesh = MeshFrom("Football 12 Rounded Pentagons", dark);
        }

        private static Vector3 Edge(Vector3 a, Vector3 b) => (2 * a + b).normalized;
        private static void Polygon(List<Vector3> vertices, Vector3[] corners)
        {
            Vector3 center = Vector3.zero;
            foreach (var c in corners) center += c;
            center.Normalize();
            for (int i = 0; i < corners.Length; i++) corners[i] = Vector3.Lerp(corners[i],center,0.025f).normalized;
            for (int i = 0; i < corners.Length; i++) Triangle(vertices,center,corners[i],corners[(i+1)%corners.Length],2);
        }
        private static void Triangle(List<Vector3> vertices, Vector3 a, Vector3 b, Vector3 c, int depth)
        {
            if (depth > 0)
            {
                var ab = (a+b).normalized; var bc = (b+c).normalized; var ca = (c+a).normalized;
                Triangle(vertices,a,ab,ca,depth-1); Triangle(vertices,ab,b,bc,depth-1);
                Triangle(vertices,ca,bc,c,depth-1); Triangle(vertices,ab,bc,ca,depth-1); return;
            }
            if (Vector3.Dot(Vector3.Cross(b-a,c-a),a) < 0) { var swap = b; b = c; c = swap; }
            vertices.Add(a*Radius); vertices.Add(b*Radius); vertices.Add(c*Radius);
        }
        private static Mesh MeshFrom(string label, List<Vector3> vertices)
        {
            var normals = new Vector3[vertices.Count]; var indices = new int[vertices.Count];
            for (int i = 0; i < vertices.Count; i++) { normals[i] = vertices[i].normalized; indices[i] = i; }
            var mesh = new Mesh { name = label };
            mesh.SetVertices(vertices); mesh.normals = normals; mesh.triangles = indices; mesh.RecalculateBounds();
            return mesh;
        }
    }
}
