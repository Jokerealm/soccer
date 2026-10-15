using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    /// <summary>立体临时角色与头顶战斗信息；造型不绑定最终英雄美术。</summary>
    public sealed class PlayerView : MonoBehaviour
    {
        private SpriteRenderer marker, shadow;
        private LineRenderer aim, shieldRing;
        public PlayerModelView Model { get; private set; }
        public PlayerResourceView ResourcesUI { get; private set; }
        private TextMesh status;
        private CameraBillboard statusBillboard;
        private LineRenderer meleeArc;
        private LineRenderer muzzle;
        private CharacterDefinition definition;
        private float animationTime, flashRemaining, impactRemaining, recoilRemaining;
        private Vec2 impactDirection, recoilDirection;
        private Color teamColor, flashColor;
        public void Flash(Color color) { flashColor = color; flashRemaining = 0.08f; }
        public void Impact(Vec2 direction, bool shield)
        { impactDirection = direction.Normalized; impactRemaining = 0.12f; Flash(shield ? new Color(0.65f, 0.85f, 1) : Color.white); }
        public void Recoil(Vec2 direction) { recoilDirection = direction.Normalized; recoilRemaining = 0.1f; }
        public void ClearFeedback() { flashRemaining = impactRemaining = recoilRemaining = 0; }

        public void Initialize(CharacterDefinition character, Team team, Team localTeam, bool local, Sprite ring, Sprite diamond, Sprite shadowSprite)
        {
            definition = character;
            teamColor = team == Team.Cyan ? new Color(0.3f, 0.93f, 0.87f) : new Color(1, 0.68f, 0.3f);
            shadow = CreateSprite("Contact Shadow", shadowSprite, -1);
            shadow.transform.localRotation = Quaternion.Euler(90,0,0);
            shadow.transform.localScale = new Vector3(1.4f, 0.65f, 1);
            marker = CreateSprite(team == Team.Cyan ? "Cyan Circle" : "Amber Diamond", team == Team.Cyan ? ring : diamond, 1);
            marker.transform.localRotation = Quaternion.Euler(90,0,0);
            marker.transform.localScale = new Vector3(local ? 1.6f : 1.25f, local ? 1.6f : 1.25f, 1);
            marker.color = local ? Color.white : new Color(1, 1, 1, 0.8f);
            var model = new GameObject("Lit Temporary Character"); model.transform.SetParent(transform, false);
            Model = model.AddComponent<PlayerModelView>(); Model.Initialize(character.Stats, team);
            aim = ViewLines.Create(transform, "Aim Direction", 0.035f, Color.white, 600);
            var resources=new GameObject("Life and Energy"); resources.transform.SetParent(transform,false);
            ResourcesUI=resources.AddComponent<PlayerResourceView>(); ResourcesUI.Initialize(team,local,team==localTeam);
            shieldRing = ViewLines.Create(transform, "Shield or Protection", 0.045f, new Color(0.65f, 0.85f, 1), 600);
            shieldRing.positionCount = 33;
            meleeArc = ViewLines.Create(transform, "Melee Cone", 0.05f, new Color(1, 0.8f, 0.3f, 0.7f), 620);
            meleeArc.positionCount = 19;
            muzzle = ViewLines.Create(transform, "Weapon Pop", 0.1f, new Color(1, 0.94f, 0.55f), 621);
            var text = new GameObject("Status / Respawn Countdown");
            text.transform.SetParent(transform, false);
            status = text.AddComponent<TextMesh>();
            statusBillboard=text.AddComponent<CameraBillboard>();
            status.fontSize = 48; status.characterSize = 0.043f;
            status.anchor = TextAnchor.LowerCenter; status.alignment = TextAlignment.Center;
            text.GetComponent<MeshRenderer>().sortingOrder = 800;
        }

        private SpriteRenderer CreateSprite(string label, Sprite sprite, int order)
        {
            var child = new GameObject(label); child.transform.SetParent(transform, false);
            var renderer = child.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = order;
            return renderer;
        }
        private static void Segment(LineRenderer line, Vector3 start, Vector3 end)
        { line.SetPosition(0, start); line.SetPosition(1, end); }

        public void Render(PlayerState state, Vec2 displayPosition, bool local, bool hasBall, bool debug, MatchRules rules, bool animate,
            Vec2 previewAim, bool feedback)
        {
            transform.position = ArenaProjection.ToWorld(displayPosition);
            if (animate)
            {
                animationTime += Time.deltaTime;
                flashRemaining = Mathf.Max(0, flashRemaining - Time.unscaledDeltaTime);
                impactRemaining = Mathf.Max(0, impactRemaining - Time.unscaledDeltaTime);
                recoilRemaining = Mathf.Max(0, recoilRemaining - Time.unscaledDeltaTime);
            }
            int order = 200 - Mathf.RoundToInt(displayPosition.Y * 8);
            marker.sortingOrder = order - 1; shadow.sortingOrder = order - 2;
            var origin = transform.position;
            float impact = feedback ? Mathf.Sin(Mathf.Clamp01(impactRemaining / 0.12f) * Mathf.PI) : 0;
            float recoil = feedback ? Mathf.Sin(Mathf.Clamp01(recoilRemaining / 0.1f) * Mathf.PI) : 0;
            marker.color = state.IsDead ? new Color(1, 1, 1, 0.25f) : local ? Color.white : new Color(1, 1, 1, 0.8f);
            Vec2 facing = state.AttackPending || state.WeaponVisual > 0 ? state.AttackFacing : previewAim;
            if (state.Stats.Loadout.Weapon == WeaponKind.ImpactHammer && state.WeaponVisual > 0)
                facing = Vec2.Rotate(facing, (0.09f - state.WeaponVisual) * 7);
            var direction = ArenaProjection.ToWorld(facing.Normalized);
            meleeArc.enabled = !state.IsDead && state.Stats.Loadout.Weapon == WeaponKind.ImpactHammer && (state.AttackPending || state.WeaponVisual > 0);
            for (int i = 0; i < 19; i++)
            {
                float angle = Mathf.Lerp(-state.Stats.Loadout.MeleeHalfAngle, state.Stats.Loadout.MeleeHalfAngle, Mathf.Clamp01((i - 1) / 16f)) * Mathf.Deg2Rad;
                var point = i == 0 || i == 18 ? displayPosition : displayPosition + Vec2.Rotate(state.AttackFacing, angle) * state.Stats.Loadout.WeaponRange;
                meleeArc.SetPosition(i, ArenaProjection.ToWorld(point));
            }
            Model.Render(state, facing, animationTime, animate, impact, impactDirection, recoil, recoilDirection,
                feedback ? flashRemaining / .08f : 0, flashColor);
            var center = origin + Vector3.up * (Model.IsThor ? 1.55f : .98f);
            var tip = center + direction * .92f;
            var tangent = new Vector3(-facing.Y, 0, facing.X);
            muzzle.enabled = feedback && !state.IsDead && state.WeaponVisual > 0.09f;
            Segment(muzzle, tip + direction * 0.12f - tangent * 0.12f, tip + direction * 0.28f + tangent * 0.12f);
            aim.enabled = local && !state.IsDead;
            Segment(aim, origin, origin + ArenaProjection.ToWorld(previewAim.Normalized * (hasBall ? 1.4f : 1.1f)));
            float top = Model.IsThor ? 2.06f : 1.78f;
            ResourcesUI.Render(state,top,animate);
            shieldRing.enabled = !state.IsDead && (state.ShieldHealth > 0 || state.FullProtection > 0);
            for (int i = 0; i < 33; i++)
            {
                float angle = i * Mathf.PI * 2 / 32;
                shieldRing.SetPosition(i, center + ArenaProjection.VisualOffset(Mathf.Cos(angle) * 0.65f, Mathf.Sin(angle) * 0.65f));
            }
            status.transform.localPosition = ArenaProjection.VisualOffset(0, state.IsDead ? .55f : top + .14f);
            status.transform.localRotation = Quaternion.Euler(55,0,0);
            statusBillboard.Offset=new Vector2(0,state.IsDead ? .55f : top+.14f);
            status.text = state.IsDead ? $"{state.RespawnRemaining:0.0}s" : state.Stun > 0 ? "STUN" : "";
            status.color = state.IsDead ? new Color(1, 0.76f, 0.55f) : Color.white;
        }
    }

    internal static class ViewLines
    {
        private static Material material;
        public static LineRenderer Create(Transform parent, string name, float width, Color color, int order)
        {
            if (material == null) material = new Material(Shader.Find("Sprites/Default"));
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            line.positionCount = 2; line.useWorldSpace = true;
            line.startWidth = line.endWidth = width; line.startColor = line.endColor = color;
            line.sortingOrder = order; return line;
        }
    }
}
