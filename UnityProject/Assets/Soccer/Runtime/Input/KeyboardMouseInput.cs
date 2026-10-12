using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    /// <summary>逐渲染帧采样并积累边沿；右键提交持续指令，低帧率也不丢点击或松键。</summary>
    public sealed class KeyboardMouseInput : ICommandSource
    {
        private readonly Camera camera;
        private PlayerCommand pending;
        private readonly GameplayGesture gesture = new GameplayGesture();
        private int localPlayerId;
        public bool SelectingSkillTarget => gesture.Targeting;
        public Vec2 PreviewAim => pending.KickHeld || gesture.Targeting ? pending.Aim : Vec2.Zero;
        public KeyboardMouseInput(Camera camera) { this.camera = camera; }
        public void Reset() { pending = default; gesture.Reset(); }

        public void Sample(MatchState state, MatchRules rules, bool pointerBlocked, bool movementOnly = false, int playerId = 0)
        {
            localPlayerId = playerId;
            var player = state.Players[playerId];
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
                var order = ContextOrder.Create(state, rules, playerId, ground, target, ball,
                    Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                pending.OrderPressed = true; pending.OrderKind = order.OrderKind;
                pending.OrderPosition = order.OrderPosition; pending.OrderTargetId = order.OrderTargetId;
                pending.QueueOrder = order.QueueOrder;
            }
            pending.StopPressed |= Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.H);
            if (movementOnly)
            {
                pending.KickPressed = pending.KickHeld = pending.KickReleased = pending.TacklePressed = pending.QPressed = pending.EPressed = pending.EReleased = pending.SpecialPressed = false;
                gesture.Reset();
                return;
            }
            gesture.Apply(ref pending, player.Stats.Id, new GameplayButtons { OnField = onField,
                Cancel = Input.GetKeyDown(KeyCode.C), KickDown = Input.GetKeyDown(KeyCode.V), KickHeld = Input.GetKey(KeyCode.V), KickUp = Input.GetKeyUp(KeyCode.V),
                PushDown = Input.GetKeyDown(KeyCode.Space), QDown = Input.GetKeyDown(KeyCode.Q), EDown = Input.GetKeyDown(KeyCode.E),
                LeftDown = Input.GetMouseButtonDown(0), SpecialDown = Input.GetKeyDown(KeyCode.G) });
        }
        private int PickPlayer(MatchState state)
        {
            int result = -1; float closest = float.PositiveInfinity;
            foreach (var p in state.Players)
            {
                if (p.Id == localPlayerId || p.IsDead) continue;
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
