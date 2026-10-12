using System;

namespace Soccer.Core
{
    internal sealed class MovementSystem
    {
        private readonly MatchSimulation sim;
        private MatchState State => sim.State;
        private MatchRules Rules => sim.Rules;
        public MovementSystem(MatchSimulation sim) { this.sim = sim; }

        public void Step(PlayerCommand[] commands, float dt)
        {
            // 出生槽优先级每回合轮换；真正网络裁决仍需 M3 验证。
            for (int n = 0; n < State.Players.Count; n++)
            {
                var p = State.Players[(n + State.Round) % State.Players.Count];
                if (p.IsDead || p.RespawnedThisTick) { p.Velocity = Vec2.Zero; continue; }
                var old = p.Position;
                p.ContactStart = old;
                Vec2 delta = Vec2.Zero;
                if (p.PushRemaining > 0)
                {
                    delta = p.PushVelocity * Math.Min(dt, p.PushRemaining);
                    p.PushRemaining = Math.Max(0, p.PushRemaining - dt);
                }
                else if (p.Stun <= 0)
                {
                    if (p.Dash != DashKind.None) delta = DashDelta(p, dt);
                    // 战斗动作的短后摇限制下一次动作，但不中断走位。
                    else if (p.ActionLock <= 0 || Rules.EnableCombat)
                    {
                        float speed = (p.Stats.Speed + (p.SkillSpeedRemaining > 0 ? p.SkillSpeedBonus : 0)) * SpeedMultiplier(p);
                        var move = commands[p.Id].Move;
                        if (move.LengthSquared > 1) move = move.Normalized;
                        delta = move * (speed * dt);
                    }
                }
                MoveWithContact(p, delta);
                p.Velocity = (p.Position - old) / dt;
            }
            SeparateBodies();
            if(State.IsOpeningWarmup) foreach(var p in State.Players)
                if(!p.IsDead) p.Position=OutsideOpeningShield(p.Position,p);
        }

        private float SpeedMultiplier(PlayerState p)
        {
            float bonus = p.RelayRemaining > 0 ? p.Stats.RelaySpeedBonus : 0;
            foreach (var e in State.Effects)
            {
                if (e.Kind != FieldEffectKind.SpeedLane || e.Windup > 0 || e.Team != p.Team) continue;
                Vec2 relative = p.Position - e.Position;
                float forward = Vec2.Dot(relative, e.Direction);
                float lateral = Math.Abs(Vec2.Cross(relative, e.Direction));
                if (forward >= 0 && forward <= e.Length && lateral <= e.Width / 2) bonus = Math.Max(bonus, e.Strength);
            }
            if (p.BoostRemaining > 0) bonus = Math.Max(bonus, p.BoostStrength);
            float multiplier = 1 + Math.Min(bonus, 0.25f);
            if (State.Ball.OwnerId == p.Id) multiplier *= Rules.PossessionSpeed;
            if (p.Charging) multiplier *= p.Preparation == ShotPreparation.Power ? p.Stats.PowerMoveSpeed : Rules.ChargeMoveSpeed;
            if (p.RootRemaining > 0) multiplier *= p.Stats.RootSpeed;
            return multiplier;
        }

        private Vec2 DashDelta(PlayerState p, float dt)
        {
            float available = dt;
            if (p.DashWindup > 0)
            {
                float used = Math.Min(available, p.DashWindup);
                p.DashWindup -= used;
                available -= used;
            }
            float moveTime = Math.Min(available, p.DashRemaining);
            p.DashRemaining -= moveTime;
            Vec2 delta = p.DashDirection * (p.DashSpeed * moveTime);
            // 结束在接触处理之后执行，保证最后一小段仍可命中。
            return delta;
        }

        private void MoveWithContact(PlayerState p, Vec2 delta)
        {
            // 所有移动均做连续扫掠；普通走路沿接触面滑动，突进在首次接触处结束。
            for (int iteration = 0; iteration < 5 && delta.LengthSquared > .00000001f; iteration++)
            {
                float earliest = 1; Vec2 normal = Vec2.Zero; PlayerState body = null;
                foreach (var other in State.Players)
                {
                    if (other.Id == p.Id || other.IsDead) continue;
                    if (Geometry.SweepCircle(p.Position, delta, other.Position, p.Stats.Radius + other.Stats.Radius, out float t) && t <= earliest)
                    { earliest = t; normal = (p.Position + delta * t - other.Position).Normalized; body = other; }
                }
                for (int sign = -1; sign <= 1; sign += 2) for (int end = -1; end <= 1; end += 2)
                    ContactObstacle(new Vec2(sign * Rules.FieldWidth / 2, end * Rules.GoalWidth / 2), Rules.PostRadius + p.Stats.Radius, p.Position, delta, ref earliest, ref normal, ref body);
                if (State.IsOpeningWarmup)
                    ContactObstacle(State.Ball.Position, Rules.OpeningShieldRadius + p.Stats.Radius, p.Position, delta, ref earliest, ref normal, ref body);
                if (State.Phase == MatchPhase.SetPiece && p.Id != State.RestartPlayerId)
                    ContactObstacle(State.RestartPosition, (p.Team != State.RestartTeam ? Rules.SetPieceDistance : Rules.BallRadius) + p.Stats.Radius,
                        p.Position, delta, ref earliest, ref normal, ref body);
                foreach (var effect in State.Effects)
                {
                    if (effect.Kind != FieldEffectKind.Shield || effect.Windup > 0) continue;
                    var tangent = new Vec2(-effect.Direction.Y, effect.Direction.X);
                    int count = Math.Max(1, (int)Math.Ceiling(effect.Length / .3f));
                    for (int i = 0; i <= count; i++) ContactObstacle(effect.Position + tangent * (effect.Length * ((float)i / count - .5f)),
                        p.Stats.Radius + .16f, p.Position, delta, ref earliest, ref normal, ref body);
                }
                p.Position = ClampPlayer(p.Position + delta * Math.Max(0, earliest - .00001f), p.Stats.Radius);
                if (normal.LengthSquared == 0) break;
                if (p.Dash != DashKind.None)
                {
                    if (p.Dash == DashKind.Tackle && body != null)
                        TackleSystem.Resolve(sim, p, body, -normal);
                    else { p.Dash = DashKind.None; p.ActionLock = Rules.TackleRecovery; }
                    break;
                }
                var remainder = delta * (1 - earliest);
                delta = remainder - normal * Math.Min(0, Vec2.Dot(remainder, normal));
            }
            if (State.IsOpeningWarmup) p.Position = OutsideOpeningShield(p.Position, p);
            if (p.Dash != DashKind.None && p.DashRemaining <= 0 && p.DashWindup <= 0)
            {
                if (p.Dash == DashKind.Tackle) { p.ActionLock = Rules.TackleRecovery; sim.Emit(MatchEventKind.Miss, p.Id); }
                p.Dash = DashKind.None;
            }
        }

        private static void ContactObstacle(Vec2 center, float radius, Vec2 start, Vec2 delta, ref float earliest, ref Vec2 normal, ref PlayerState body)
        {
            if (!Geometry.SweepCircle(start, delta, center, radius, out float t) || t > earliest) return;
            earliest = t; normal = (start + delta * t - center).Normalized; body = null;
        }

        private Vec2 ClampPlayer(Vec2 position, float radius)
            => new Vec2(Geometry.Clamp(position.X, -Rules.FieldWidth / 2 + radius, Rules.FieldWidth / 2 - radius),
                Geometry.Clamp(position.Y, -Rules.FieldHeight / 2 + radius, Rules.FieldHeight / 2 - radius));

        private Vec2 OutsideOpeningShield(Vec2 position,PlayerState p)
        {
            Vec2 offset=position-State.Ball.Position;
            float radius=Rules.OpeningShieldRadius+p.Stats.Radius;
            if(offset.Length>=radius) return position;
            Vec2 normal=offset.LengthSquared>.0001f ? offset.Normalized : new Vec2(p.Team==Team.Cyan ? -1 : 1,0);
            return State.Ball.Position+normal*(radius+.0001f);
        }

        private void SeparateBodies()
        {
            // 修复出生、复活和技能后的重叠；大体型按圆面积拥有更大的惯性。
            for (int pass = 0; pass < 20; pass++)
            {
                bool overlapFound = false;
                for (int i = 0; i < State.Players.Count; i++) for (int j = i + 1; j < State.Players.Count; j++)
                {
                    var a = State.Players[i]; var b = State.Players[j];
                    if (a.IsDead || b.IsDead) continue;
                    var offset = b.Position - a.Position;
                    float overlap = a.Stats.Radius + b.Stats.Radius - offset.Length;
                    if (overlap <= .00001f) continue;
                    overlapFound = true;
                    var normal = offset.LengthSquared < .000001f ? new Vec2(0, 1) : offset.Normalized;
                    float massA = a.Stats.Radius * a.Stats.Radius, massB = b.Stats.Radius * b.Stats.Radius;
                    a.Position = ClampPlayer(a.Position - normal * ((overlap + .00001f) * massB / (massA + massB)), a.Stats.Radius);
                    b.Position = ClampPlayer(b.Position + normal * ((overlap + .00001f) * massA / (massA + massB)), b.Stats.Radius);
                    if (State.IsOpeningWarmup) { a.Position = OutsideOpeningShield(a.Position, a); b.Position = OutsideOpeningShield(b.Position, b); }
                    if (State.Phase == MatchPhase.SetPiece) { a.Position = sim.Restarts.OutsideExclusion(a); b.Position = sim.Restarts.OutsideExclusion(b); }
                }
                if (!overlapFound) break;
            }
        }
    }
}
