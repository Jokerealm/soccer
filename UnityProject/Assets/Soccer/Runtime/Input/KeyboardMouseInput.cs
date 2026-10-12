using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    /// <summary>逐渲染帧采样并积累边沿；右键提交持续指令，低帧率也不丢点击或松键。</summary>
    public sealed class KeyboardMouseInput : ICommandSource
    {
        private readonly Camera camera;
        private PlayerCommand pending;
        private bool mouseGesture, eGesture;
        public Vec2 PreviewAim => mouseGesture || eGesture ? pending.Aim : Vec2.Zero;
        public KeyboardMouseInput(Camera camera) { this.camera = camera; }
        public void Reset() { pending = default; mouseGesture = eGesture = false; }

        public void Sample(MatchState state, MatchRules rules, bool pointerBlocked, bool movementOnly = false)
        {
            var player = state.Players[0];
            if (player.IsDead || player.RespawnedThisTick) { Reset(); return; }
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            var ground = plane.Raycast(ray, out float distance) ? ArenaProjection.ToGround(ray.GetPoint(distance)) : player.Position;
            bool onField = !pointerBlocked && camera.pixelRect.Contains(Input.mousePosition);
            if (onField) pending.Aim = ground - player.Position;
            if (pending.Aim.LengthSquared < .001f) pending.Aim = player.Facing;
            if (onField && Input.GetMouseButtonDown(1))
            {
                int target = PickPlayer(state);
                bool ball = target < 0 && Vec2.Distance(ground, state.Ball.Position) < rules.BallRadius + .25f;
                var order = ContextOrder.Create(state, rules, 0, ground, target, ball,
                    Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                pending.OrderPressed = true; pending.OrderKind = order.OrderKind;
                pending.OrderPosition = order.OrderPosition; pending.OrderTargetId = order.OrderTargetId;
                pending.QueueOrder = order.QueueOrder;
            }
            pending.StopPressed |= Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.H);
            pending.Cancel |= Input.GetKeyDown(KeyCode.C);
            if (movementOnly)
            {
                pending.KickPressed = pending.KickHeld = pending.KickReleased = pending.TacklePressed = pending.QPressed = pending.EPressed = pending.EReleased = pending.SpecialPressed = false;
                mouseGesture = eGesture = false;
                return;
            }
            if (onField && Input.GetMouseButtonDown(0)) { mouseGesture = true; pending.KickPressed = true; }
            pending.KickHeld = mouseGesture && Input.GetMouseButton(0);
            if (Input.GetMouseButtonUp(0) && mouseGesture)
            {
                pending.KickReleased |= onField; pending.Cancel |= !onField; mouseGesture = false;
            }
            pending.TacklePressed |= onField && Input.GetKeyDown(KeyCode.Space);
            pending.QPressed |= onField && Input.GetKeyDown(KeyCode.Q);
            pending.SpecialPressed |= onField && Input.GetKeyDown(KeyCode.G);
            if (onField && Input.GetKeyDown(KeyCode.E)) { pending.EPressed = true; eGesture = true; }
            if (Input.GetKeyUp(KeyCode.E) && eGesture) { pending.EReleased = true; eGesture = false; }
        }
        private int PickPlayer(MatchState state)
        {
            int result = -1; float closest = float.PositiveInfinity;
            foreach (var p in state.Players)
            {
                if (p.Id == 0 || p.IsDead) continue;
                var foot = camera.WorldToScreenPoint(ArenaProjection.ToWorld(p.Position));
                var head = camera.WorldToScreenPoint(ArenaProjection.ToWorld(p.Position, p.Stats.Id == CharacterId.Thor ? 1.9f : 1.8f));
                var edge = camera.WorldToScreenPoint(ArenaProjection.ToWorld(p.Position + new Vec2(p.Stats.Radius, 0), .8f));
                float width = Mathf.Max(12, Mathf.Abs(edge.x - foot.x));
                float dx = Mathf.Abs(Input.mousePosition.x - foot.x);
                bool inBody = foot.z > 0 && dx <= width && Input.mousePosition.y >= Mathf.Min(foot.y, head.y) - 8 && Input.mousePosition.y <= Mathf.Max(foot.y, head.y) + 8;
                if (inBody && foot.z < closest) { closest = foot.z; result = p.Id; }
            }
            return result;
        }
        public PlayerCommand ReadCommand(MatchState state, int playerId)
        {
            var result = pending; pending = pending.WithoutEdges(); return result;
        }
    }
}
