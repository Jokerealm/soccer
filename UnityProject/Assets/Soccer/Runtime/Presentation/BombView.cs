using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    /// <summary>炸弹使用立体模型与地面危险范围，UI 倒计时直接读取规则状态。</summary>
    public sealed class BombView : MonoBehaviour
    {
        private LineRenderer warning;
        private Transform body;
        private TextMesh countdown;
        private Material material;
        public void Initialize()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Lit Bomb"; sphere.transform.SetParent(transform, false);
            Destroy(sphere.GetComponent<Collider>());
            body = sphere.transform; body.localScale = Vector3.one * 0.38f;
            material = new Material(Shader.Find("Standard"));
            material.color = new Color(0.16f, 0.22f, 0.28f);
            material.SetFloat("_Metallic", 0.55f); material.SetFloat("_Glossiness", 0.65f);
            material.EnableKeyword("_EMISSION");
            sphere.GetComponent<MeshRenderer>().sharedMaterial = material;
            warning = ViewLines.Create(transform, "Bomb Danger Radius", 0.04f, Color.yellow, 55);
            warning.positionCount = 65;
            var label = new GameObject("Bomb Countdown"); label.transform.SetParent(transform, false);
            countdown = label.AddComponent<TextMesh>(); countdown.fontSize = 48; countdown.characterSize = 0.06f;
            countdown.anchor = TextAnchor.MiddleCenter;
            label.GetComponent<MeshRenderer>().sortingOrder = 810;
            label.transform.localRotation = Quaternion.Euler(55, 0, 0);
        }
        public void Render(RemoteBomb bomb, BallState ball)
        {
            var center = bomb.AttachedToBall ? ball.Position : bomb.Position;
            float blink = 0.55f + 0.45f * Mathf.Sin(bomb.Remaining * (bomb.Remaining < 1 ? 24 : 12));
            var color = bomb.Team == Team.Cyan ? new Color(0.45f, 1, 0.8f) : new Color(1, 0.55f, 0.28f);
            material.SetColor("_EmissionColor", color * blink * 1.3f);
            body.position = ArenaProjection.ToWorld(center, bomb.AttachedToBall ? 0.65f : 0.23f);
            countdown.transform.position = ArenaProjection.ToWorld(center) + ArenaProjection.VisualUp * 1.0f;
            countdown.text = $"{bomb.Remaining:0.0}s"; countdown.color = Color.Lerp(color, Color.white, blink * 0.5f);
            color.a = 0.35f + blink * 0.3f;
            warning.startColor = warning.endColor = color;
            for (int i = 0; i <= 64; i++)
            {
                float angle = i * Mathf.PI * 2 / 64;
                warning.SetPosition(i, ArenaProjection.ToWorld(center + new Vec2(Mathf.Cos(angle), Mathf.Sin(angle)) * bomb.Radius, 0.025f));
            }
        }
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
