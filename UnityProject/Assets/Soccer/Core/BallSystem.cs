using System;

namespace Soccer.Core
{
    /// <summary>唯一球权入口与扫掠球碰撞。动画播放只订阅 Kick 事件，不决定释放时刻。</summary>
    internal sealed class BallSystem
    {
        private readonly MatchSimulation sim;
        private MatchState State => sim.State;
        private MatchRules Rules => sim.Rules;
        public BallSystem(MatchSimulation sim) { this.sim = sim; }

        public void ProcessInput(PlayerState p, PlayerCommand command, float dt)
        {
            if (command.Cancel || p.IsDead || p.Stun > 0 || p.PushRemaining > 0) return;
            if (State.Ball.OwnerId != p.Id) { MatchSimulation.CancelCharge(p); return; }
            if (p.KickCooldown > 0) { MatchSimulation.CancelCharge(p); return; }
            if (command.KickPressed) { p.Charging = true; p.ChargeTime = 0; }
            if (!p.Charging) return; // 无球按住左键，后来接球不会自动开始蓄力。
            p.ChargeTime += dt;
            float chargeLimit = p.Preparation == ShotPreparation.Power ? p.Stats.PowerChargeSeconds : Rules.ChargeSeconds;
            if (command.KickReleased || p.ChargeTime >= chargeLimit + Rules.MaxChargeHold) Kick(p, chargeLimit);
        }

        private void Kick(PlayerState p, float chargeLimit)
        {
            var b = State.Ball;
            float charge = Geometry.Clamp(p.ChargeTime / chargeLimit, 0, 1);
            var preparation = p.Preparation;
            float distanceFactor = p.Stats.KickDistance;
            if (p.Preparation == ShotPreparation.Power) distanceFactor *= p.Stats.PowerDistance;
            float speed = (Rules.TapKickSpeed + (Rules.FullKickSpeed - Rules.TapKickSpeed) * charge) * (float)Math.Sqrt(distanceFactor);
            UpdateHeldBall();
            if (State.Phase == MatchPhase.SetPiece) sim.Restarts.Release(p);
            b.OwnerId = -1;
            b.DroppedOwnerId = -1;
            b.LastKickerId = b.LastTouchId = p.Id;
            b.SelfCatchRemaining = Rules.SelfCatchLock;
            b.SinceKick = 0;
            ClearFlight();
            b.Velocity = p.Facing * Math.Min(speed, Rules.MaxBallSpeed);
            if (p.Preparation == ShotPreparation.Curve)
            {
                float signed = (float)(Math.Atan2(Vec2.Cross(p.PreparationFacing, p.Facing), Vec2.Dot(p.PreparationFacing, p.Facing)) * 180 / Math.PI);
                int sign = Math.Abs(signed) <= p.Stats.CurveDeadZoneDegrees ? 0 : Math.Sign(signed);
                b.CurveRemaining = p.Stats.CurveSeconds;
                b.CurveRadiansPerSecond = sign * p.Stats.CurveDegrees * (float)Math.PI / 180 / p.Stats.CurveSeconds;
            }
            p.Preparation = ShotPreparation.None;
            p.ActionLock = Rules.KickRecovery;
            p.KickVisual = 0.4f;
            p.Kicks++;
            p.KickCooldown = Rules.KickCooldown;
            MatchSimulation.CancelCharge(p);
            MatchSimulation.RemoveKickoffProtection(p);
            sim.Emit(MatchEventKind.Kick, p.Id, shot: preparation, strength: charge);
        }

        internal bool TryLaunchOverheadSkill(PlayerState p, Vec2 horizontalVelocity, float verticalSpeed)
        {
            if (p.IsDead || p.Stun>0 || p.PushRemaining>0 || State.Ball.OwnerId!=p.Id ||
                !Geometry.IsFinite(horizontalVelocity.X) || !Geometry.IsFinite(horizontalVelocity.Y) ||
                !Geometry.IsFinite(horizontalVelocity.Length) || horizontalVelocity.Length>Rules.MaxBallSpeed ||
                !Geometry.IsFinite(verticalSpeed) || verticalSpeed<=0) return false;
            UpdateHeldBall();
            var b = State.Ball;
            b.DroppedOwnerId = -1;
            b.OwnerId = -1; b.LastKickerId = b.LastTouchId = p.Id;
            b.SelfCatchRemaining = Rules.SelfCatchLock; b.SinceKick = 0;
            b.FlightMode = BallFlightMode.SkillOverhead;
            b.Height = b.PreviousHeight = 0; b.VerticalVelocity = verticalSpeed;
            b.Velocity = horizontalVelocity; b.CurveRemaining = 0;
            MatchSimulation.CancelCharge(p);
            p.Preparation = ShotPreparation.None;
            MatchSimulation.RemoveKickoffProtection(p);
            sim.Emit(MatchEventKind.Kick,p.Id,position:b.Position);
            return true;
        }

        private void ClearFlight()
        {
            var b = State.Ball;
            b.FlightMode = BallFlightMode.Ground;
            b.Height = b.PreviousHeight = b.VerticalVelocity = 0;
        }

        public void Step(float dt)
        {
            var b = State.Ball;
            b.SelfCatchRemaining = Math.Max(0, b.SelfCatchRemaining - dt);
            b.SinceKick += dt;
            if (State.Owner != null)
            {
                UpdateHeldBall();
                CheckHeldGoal();
                return;
            }
            if (b.CurveRemaining > 0)
            {
                float curveDt = Math.Min(dt, b.CurveRemaining);
                b.Velocity = Vec2.Rotate(b.Velocity, b.CurveRadiansPerSecond * curveDt);
                b.CurveRemaining -= curveDt;
            }
            if (!b.Airborne)
            {
                ClearFlight();
                MoveFree(dt,false);
                if (b.OwnerId<0 && State.Phase==MatchPhase.Playing) ReceiveTouchingBall();
                return;
            }
            float remaining = dt;
            // 只有明确由技能启用的高球使用高度；首次落地后立即回到二维。
            for (int part=0;part<4 && remaining>0.0000001f;part++)
            {
                bool air = b.Airborne;
                float duration = air ? Math.Min(remaining,TimeToFloor(b.Height,b.VerticalVelocity)) : remaining;
                if (duration>0.0000001f) MoveFree(duration,air);
                if (b.OwnerId>=0 || State.Phase!=MatchPhase.Playing) return;
                remaining -= duration;
                if (air && b.Height<=0.00001f && b.VerticalVelocity<0) Land();
            }
            if (b.OwnerId < 0 && State.Phase == MatchPhase.Playing) ReceiveTouchingBall();
        }

        private float TimeToFloor(float height, float velocity)
            => (velocity+(float)Math.Sqrt(velocity*velocity+2*Rules.SkillBallGravity*Math.Max(0,height)))/Rules.SkillBallGravity;

        private void MoveFree(float dt, bool air)
        {
            var b = State.Ball; float speed = b.Velocity.Length;
            float drag = air ? Rules.SkillBallAirDeceleration : Rules.BallDeceleration;
            float travelTime = speed>0 ? drag>0 ? Math.Min(dt,speed/drag) : dt : 0;
            float distance = speed*travelTime-0.5f*drag*travelTime*travelTime;
            var delta = speed>0 ? b.Velocity.Normalized*distance : Vec2.Zero;
            b.Velocity = speed>0 ? b.Velocity.Normalized*Math.Max(0,speed-drag*dt) : Vec2.Zero;
            MoveSwept(delta,dt,air?Rules.SkillBallGravity:0);
        }

        private void Land()
        {
            var b = State.Ball; float impact = Math.Abs(b.VerticalVelocity);
            b.Velocity *= Rules.SkillBallLandingSpeedRetention;
            ClearFlight();
            if (impact>=2)
                sim.Emit(MatchEventKind.Bounce,strength:impact/Rules.MaxBallSpeed,position:b.Position);
        }

        internal bool CanCatch(PlayerState p)
        {
            var b = State.Ball;
            return !p.IsDead && !p.RespawnedThisTick && p.Stun<=0 && p.PushRemaining<=0 &&
                !(p.Id==b.LastKickerId && b.SelfCatchRemaining>0) &&
                (b.FlightMode!=BallFlightMode.SkillOverhead || (b.Height<=Rules.SkillBallCatchHeight && b.VerticalVelocity<=0));
        }

        public void UpdateHeldBall()
        {
            var owner = State.Owner;
            if (owner == null || State.Phase == MatchPhase.SetPiece) return;
            ClearFlight();
            float offset = owner.Stats.Radius + Rules.BallRadius + Rules.FootGap;
            Vec2 desired = owner.Position + owner.Facing * offset;
            // 球门净口允许脚前球越过门线；其他边界缩短脚前偏移。
            float xBound = Math.Abs(desired.Y) <= Rules.GoalWidth / 2 - Rules.BallRadius
                ? Rules.FieldWidth / 2 + Rules.GoalDepth - Rules.BallRadius : Rules.FieldWidth / 2 - Rules.BallRadius;
            State.Ball.Position = Rules.EnableOutOfPlay ? desired : new Vec2(Geometry.Clamp(desired.X, -xBound, xBound),
                Geometry.Clamp(desired.Y, -Rules.FieldHeight / 2 + Rules.BallRadius, Rules.FieldHeight / 2 - Rules.BallRadius));
            foreach (var other in State.Players)
            {
                if (other.Id == owner.Id || other.IsDead) continue;
                if (Geometry.SweepCircle(owner.Position, State.Ball.Position - owner.Position, other.Position, other.Stats.Radius + Rules.BallRadius, out float bodyT))
                    State.Ball.Position = owner.Position + (State.Ball.Position - owner.Position) * Math.Max(0, bodyT - .001f);
            }
            if (Rules.EnableOutOfPlay && Vec2.Distance(State.Ball.Position, owner.Position) < offset - .002f)
            {
                // 正前方没有放球空间时，将球护在最接近朝向的可用侧面，保留完整球半径。
                for (int step = 1; step <= 12; step++)
                {
                    bool found = false;
                    foreach (int sign in new[] { 1, -1 })
                    {
                        var candidate = owner.Position + Vec2.Rotate(owner.Facing, sign * step * (float)Math.PI / 12) * offset;
                        bool clear = true;
                        foreach (var other in State.Players)
                            if (other.Id != owner.Id && !other.IsDead && Vec2.Distance(candidate, other.Position) < other.Stats.Radius + Rules.BallRadius)
                            { clear = false; break; }
                        if (!clear) continue;
                        State.Ball.Position = candidate; found = true; break;
                    }
                    if (found) break;
                }
            }
            // 不允许通过持球位置跳到门柱另一侧；最近的门柱接触面截短偏移。
            for (int side = -1; side <= 1; side += 2)
            for (int end = -1; end <= 1; end += 2)
            {
                var post = new Vec2(side * Rules.FieldWidth / 2, end * Rules.GoalWidth / 2);
                if (Geometry.SweepCircle(owner.Position, State.Ball.Position - owner.Position, post, Rules.PostRadius + Rules.BallRadius, out float t))
                    State.Ball.Position = owner.Position + (State.Ball.Position - owner.Position) * Math.Max(0, t - 0.001f);
            }
        }

        private void CheckHeldGoal()
        {
            var position = State.Ball.Position;
            if (Math.Abs(position.Y) <= Rules.GoalWidth / 2 - Rules.BallRadius)
            {
                if (position.X >= Rules.FieldWidth / 2 + Rules.BallRadius) { sim.Score(Team.Cyan); return; }
                if (position.X <= -Rules.FieldWidth / 2 - Rules.BallRadius) { sim.Score(Team.Amber); return; }
            }
            if (!Rules.EnableOutOfPlay) return;
            float w = Rules.FieldWidth / 2 + Rules.BallRadius, h = Rules.FieldHeight / 2 + Rules.BallRadius;
            if (Math.Abs(position.X) >= w || Math.Abs(position.Y) >= h)
            {
                var start = State.Ball.PreviousPosition; var delta = position - start;
                float tx = Math.Abs(position.X) >= w && Math.Abs(delta.X) > .0001f ? (Math.Sign(position.X) * w - start.X) / delta.X : float.PositiveInfinity;
                float ty = Math.Abs(position.Y) >= h && Math.Abs(delta.Y) > .0001f ? (Math.Sign(position.Y) * h - start.Y) / delta.Y : float.PositiveInfinity;
                bool goalLine = tx <= ty;
                var crossing = start + delta * Geometry.Clamp(Math.Min(tx, ty), 0, 1);
                sim.Restarts.Out(goalLine, goalLine ? Math.Sign(position.X) : Math.Sign(position.Y), crossing);
            }
        }

        private struct Contact
        {
            public float T;
            public Vec2 Normal;
            public PlayerState Player;
            public Vec2 PlayerCenter;
            public FieldEffect Shield;
            public bool IsOut;
            public bool GoalLine;
            public int Side;
            public bool IsGoal;
            public Team ScoringTeam;
        }

        private void MoveSwept(Vec2 delta, float dt, float gravity)
        {
            var b = State.Ball;
            // 每段寻找最早的几何/身体/进球接触，反弹后继续走余下路径。
            for (int bounce = 0; bounce < 8; bounce++)
            {
                var contact = FindContact(b.Position,delta,b.Height,b.VerticalVelocity,dt,gravity);
                b.Position += delta * contact.T;
                float used = dt*contact.T;
                b.Height = Math.Max(0,HeightAt(b.Height,b.VerticalVelocity,used,gravity));
                b.VerticalVelocity -= gravity*used;
                if (contact.T >= 1 && !contact.IsGoal && !contact.IsOut && contact.Player == null && contact.Normal.LengthSquared == 0) break;
                if (contact.IsGoal) { sim.Score(contact.ScoringTeam); return; }
                if (contact.IsOut) { sim.Restarts.Out(contact.GoalLine, contact.Side, b.Position); return; }
                if (contact.Player != null && !CanCatch(contact.Player) && !sim.Restarts.Touch(contact.Player)) return;
                if (contact.Player != null && CanCatch(contact.Player))
                { sim.TryAcquireBallOnContact(contact.Player, contact.PlayerCenter); UpdateHeldBall(); return; }
                float restitution = contact.Player != null ? Rules.BodyBounce : contact.Shield != null ? contact.Shield.Strength : Rules.WallBounce;
                if (b.Velocity.Length > 2)
                    sim.Emit(MatchEventKind.Bounce, target: contact.Player != null ? contact.Player.Id : -1,
                        strength: b.Velocity.Length / Rules.MaxBallSpeed, position: b.Position);
                var normal = contact.Normal;
                float dot = Vec2.Dot(b.Velocity,normal);
                b.Velocity = (b.Velocity-normal*(2*dot))*restitution;
                dt *= 1-contact.T;
                delta = (delta-normal*(2*Vec2.Dot(delta,normal)))*((1-contact.T)*restitution);
                b.Position += normal * 0.0005f;
                b.CurveRemaining = 0;
                if (dt<=0.0000001f) break;
            }
        }

        private static float HeightAt(float height,float velocity,float time,float gravity)
            => height+velocity*time-0.5f*gravity*time*time;

        private Contact FindContact(Vec2 start, Vec2 delta,float height,float verticalVelocity,float dt,float gravity)
        {
            var best = new Contact { T = 1 };
            float halfW = Rules.FieldWidth / 2, halfH = Rules.FieldHeight / 2, r = Rules.BallRadius;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                if (Rules.EnableOutOfPlay)
                {
                    if (delta.Y * sign > 0)
                    {
                        float t = (sign * (halfH + r) - start.Y) / delta.Y;
                        if (t >= 0 && t <= best.T) best = new Contact { T = t, IsOut = true, Side = sign };
                    }
                    if (delta.X * sign > 0)
                    {
                        float t = (sign * (halfW + r) - start.X) / delta.X;
                        float y = start.Y + delta.Y * t;
                        if (t >= 0 && t <= best.T)
                            best = Math.Abs(y) <= Rules.GoalWidth / 2 - r
                                ? new Contact { T = t, IsGoal = true, ScoringTeam = sign > 0 ? Team.Cyan : Team.Amber }
                                : new Contact { T = t, IsOut = true, GoalLine = true, Side = sign };
                    }
                }
                else
                {
                    if (delta.Y * sign > 0) TryPlane(start.Y, delta.Y, sign * (halfH - r), new Vec2(0, -sign), ref best);
                    if (delta.X * sign > 0)
                    {
                        float wallT = (sign * (halfW - r) - start.X) / delta.X;
                        if (Math.Abs(start.Y + delta.Y * wallT) > Rules.GoalWidth / 2 - r)
                            TryPlane(start.X, delta.X, sign * (halfW - r), new Vec2(-sign, 0), ref best);
                        float goalT = (sign * (halfW + r) - start.X) / delta.X;
                        if (goalT >= 0 && goalT <= best.T && Math.Abs(start.Y + delta.Y * goalT) <= Rules.GoalWidth / 2 - r)
                            best = new Contact { T = goalT, IsGoal = true, ScoringTeam = sign > 0 ? Team.Cyan : Team.Amber };
                    }
                }
                for (int end = -1; end <= 1; end += 2)
                {
                    var post = new Vec2(sign * halfW, end * Rules.GoalWidth / 2);
                    if (Geometry.SweepCircle(start, delta, post, Rules.PostRadius + r, out float t) && t <= best.T)
                        best = new Contact { T = t, Normal = (start + delta * t - post).Normalized };
                }
            }
            // 先几何，后身体；完全同刻几何优先。槽位每回合轮换。
            foreach (var e in State.Effects)
            {
                if (e.Kind != FieldEffectKind.Shield || e.Windup > 0) continue;
                // 护板用一列相交的圆构成胶囊；净口边缘保守，不会漏过球。
                int count = Math.Max(2, (int)Math.Ceiling(e.Length / (r * 0.75f)));
                var tangent = new Vec2(-e.Direction.Y, e.Direction.X);
                for (int i = 0; i <= count; i++)
                {
                    var center = e.Position + tangent * (e.Length * ((float)i / count - 0.5f));
                    if (Geometry.SweepCircle(start, delta, center, r + 0.06f, out float t) && t < best.T)
                        best = new Contact { T = t, Normal = (start + delta * t - center).Normalized, Shield = e };
                }
            }
            for (int n = 0; n < State.Players.Count; n++)
            {
                var p = State.Players[(n + State.Round) % State.Players.Count];
                if (p.IsDead || p.RespawnedThisTick || (p.Id == State.Ball.DroppedOwnerId && p.PushRemaining > 0) || (p.Id == State.Ball.LastKickerId && State.Ball.SelfCatchRemaining > 0)) continue;
                float radius = p.Stats.Radius + r;
                bool touchingAtStart = CanCatch(p) && Vec2.Distance(start,p.ContactStart) <= radius;
                float t = 0;
                bool contact = touchingAtStart || Geometry.SweepCircle(start, delta - (p.Position - p.ContactStart), p.ContactStart, radius, out t);
                if (contact && t < best.T &&
                    (State.Ball.FlightMode!=BallFlightMode.SkillOverhead ||
                        HeightAt(height,verticalVelocity,t*dt,gravity)<=Rules.SkillPlayerClearHeight))
                    best = new Contact { T = t, Normal = (start + delta * t - (p.ContactStart + (p.Position - p.ContactStart) * t)).Normalized, Player = p, PlayerCenter = p.ContactStart + (p.Position - p.ContactStart) * t };
            }
            return best;
        }

        private static void TryPlane(float start, float delta, float plane, Vec2 normal, ref Contact best)
        {
            float t = (plane - start) / delta;
            if (t >= 0 && t <= best.T) best = new Contact { T = t, Normal = normal };
        }

        private void ReceiveTouchingBall()
        {
            var b = State.Ball;
            if (b.FlightMode==BallFlightMode.SkillOverhead && (b.Height>Rules.SkillBallCatchHeight || b.VerticalVelocity>0)) return;
            for (int n = 0; n < State.Players.Count; n++)
            {
                var p = State.Players[(n + State.Round) % State.Players.Count];
                if (!CanCatch(p)) continue;
                if (Vec2.Distance(p.Position, b.Position) <= p.Stats.Radius + Rules.BallRadius)
                { sim.TryAcquireBallOnContact(p, p.Position); UpdateHeldBall(); CheckHeldGoal(); return; }
            }
        }
    }
}
