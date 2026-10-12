using System;

namespace Soccer.Core
{
    /// <summary>出界与定位球独立于暴力战斗；不存在因攻击、推抢而触发的犯规或点球。</summary>
    internal sealed class RestartSystem
    {
        private readonly MatchSimulation sim;
        private MatchState State => sim.State;
        private MatchRules Rules => sim.Rules;
        public RestartSystem(MatchSimulation sim) { this.sim = sim; }
        internal static Team Opponent(Team team) => team == Team.Cyan ? Team.Amber : Team.Cyan;
        public void Out(bool goalLine, int side, Vec2 crossing)
        {
            if (State.Phase != MatchPhase.Playing) return;
            int last = State.Ball.LastTouchId;
            Team defending = side > 0 ? Team.Amber : Team.Cyan;
            Team lastTeam = last >= 0 ? State.Players[last].Team : (goalLine ? Opponent(defending) : Team.Cyan);
            sim.Emit(MatchEventKind.OutOfPlay, last, position: crossing);
            if (!goalLine)
                Begin(RestartKind.ThrowIn, Opponent(lastTeam), new Vec2(crossing.X, Math.Sign(crossing.Y) * (Rules.FieldHeight / 2 - Rules.BallRadius - .03f)));
            else if (lastTeam == defending)
                Begin(RestartKind.Corner, Opponent(defending), new Vec2(side * (Rules.FieldWidth / 2 - Rules.BallRadius - .08f),
                    (crossing.Y >= 0 ? 1 : -1) * (Rules.FieldHeight / 2 - Rules.BallRadius - .08f)));
            else
                Begin(RestartKind.GoalKick, defending, new Vec2(side * (Rules.FieldWidth / 2 - 3), Geometry.Clamp(crossing.Y, -1.6f, 1.6f)));
        }
        public void Begin(RestartKind kind, Team team, Vec2 position)
        {
            sim.CancelPendingInputs();
            State.Restart = kind; State.RestartTeam = team;
            State.RestartPosition = new Vec2(Geometry.Clamp(position.X, -Rules.FieldWidth / 2 + Rules.BallRadius + .03f, Rules.FieldWidth / 2 - Rules.BallRadius - .03f),
                Geometry.Clamp(position.Y, -Rules.FieldHeight / 2 + Rules.BallRadius + .03f, Rules.FieldHeight / 2 - Rules.BallRadius - .03f));
            State.Phase = MatchPhase.SetPiece; State.PhaseRemaining = Rules.SetPiecePreparation;
            State.RestartPlayerId = -1;
            State.MutableBombs.Clear(); State.MutableProjectiles.Clear(); State.MutableEffects.Clear();
            foreach (var p in State.Players)
            {
                p.Dash = DashKind.None; p.PushRemaining = p.Stun = p.ActionLock = 0; p.Velocity = Vec2.Zero;
            }
            var b = State.Ball;
            b.OwnerId = b.LastKickerId = b.LastTouchId = b.RestartKickerId = b.DroppedOwnerId = -1;
            b.ReleasedRestart = RestartKind.None;
            b.Position = b.PreviousPosition = State.RestartPosition; b.Velocity = Vec2.Zero;
            b.Height = b.PreviousHeight = b.VerticalVelocity = b.CurveRemaining = b.SelfCatchRemaining = 0;
            b.PossessionContactRemaining = 0;
            b.FlightMode = BallFlightMode.Ground;
            SelectKicker();
            foreach (var p in State.Players) if (!p.IsDead) { p.Position = OutsideExclusion(p); p.PreviousPosition = p.Position; }
            sim.Emit(MatchEventKind.Restart, State.RestartPlayerId, (int)team, position: State.RestartPosition);
        }
        private void SelectKicker()
        {
            PlayerState nearest = null; float distance = float.PositiveInfinity;
            foreach (var p in State.Players)
            {
                if (p.IsDead || p.Team != State.RestartTeam) continue;
                float d = Vec2.Distance(p.Position, State.RestartPosition);
                if (d < distance) { nearest = p; distance = d; }
            }
            if (nearest == null) { State.RestartPlayerId = State.Ball.OwnerId = -1; return; }
            State.RestartPlayerId = nearest.Id; State.Ball.OwnerId = nearest.Id;
            Vec2 direction = -State.RestartPosition;
            if (State.Restart == RestartKind.FreeKick || State.Restart == RestartKind.GoalKick)
                direction = new Vec2(nearest.Team == Team.Cyan ? 1 : -1, 0);
            nearest.Facing = direction.Normalized;
            nearest.Position = NavigationPathfinder.Clamp(Rules, nearest, State.RestartPosition - nearest.Facing * (nearest.Stats.Radius + Rules.BallRadius + Rules.FootGap));
            // 靠近边线时站在场内，并面向固定球点。
            float offset = nearest.Stats.Radius + Rules.BallRadius + Rules.FootGap;
            if (Vec2.Distance(nearest.Position, State.RestartPosition) < offset - .001f)
                nearest.Position = NavigationPathfinder.Clamp(Rules, nearest, State.RestartPosition + (-State.RestartPosition).Normalized * offset);
            nearest.Facing = (State.RestartPosition - nearest.Position).Normalized;
            nearest.PreviousPosition = nearest.Position;
        }
        public Vec2 OutsideExclusion(PlayerState p)
        {
            if (p.IsDead || p.Id == State.RestartPlayerId) return p.Position;
            float radius = (p.Team != State.RestartTeam ? Rules.SetPieceDistance : Rules.BallRadius) + p.Stats.Radius;
            var offset = p.Position - State.RestartPosition;
            if (offset.Length >= radius) return p.Position;
            // 向场心放置，避免在边角经 Clamp 后重新进入保护圆。
            var direction = offset.LengthSquared > .001f ? offset.Normalized : (-State.RestartPosition).Normalized;
            var result = NavigationPathfinder.Clamp(Rules, p, State.RestartPosition + direction * (radius + .01f));
            if (Vec2.Distance(result, State.RestartPosition) < radius)
                result = NavigationPathfinder.Clamp(Rules, p, State.RestartPosition + (-State.RestartPosition).Normalized * (radius + .02f));
            return result;
        }
        public void Step(PlayerCommand[] commands)
        {
            if (State.RestartPlayerId < 0 || State.Players[State.RestartPlayerId].IsDead) SelectKicker();
            State.PhaseRemaining = Math.Max(0, State.PhaseRemaining - MatchSimulation.TickSeconds);
            foreach (var p in State.Players) if (!p.IsDead) p.Position = OutsideExclusion(p);
            if (State.RestartPlayerId < 0) return; // 等本队原地复活，绝不提前复活。
            var kicker = State.Players[State.RestartPlayerId];
            kicker.KickCooldown = 0; // 定位球不能被上一脚的公共冷却卡住。
            var command = commands[kicker.Id];
            if (command.Aim.LengthSquared > .001f) kicker.Facing = command.Aim.Normalized;
            if (State.PhaseRemaining > 0) { MatchSimulation.CancelCharge(kicker); return; }
            sim.ProcessRestartKick(kicker, command);
        }
        public void Release(PlayerState p)
        {
            State.Ball.RestartKickerId = p.Id; State.Ball.ReleasedRestart = State.Restart;
            State.Restart = RestartKind.None; State.RestartPlayerId = -1;
            State.Phase = MatchPhase.Playing; State.PhaseRemaining = 0;
        }
        public bool Touch(PlayerState p)
        {
            var b = State.Ball;
            if (b.RestartKickerId == p.Id && Rules.EnableOutOfPlay)
            {
                sim.Emit(MatchEventKind.RuleViolation, p.Id, position: b.Position);
                Begin(RestartKind.FreeKick, Opponent(p.Team), b.Position);
                return false;
            }
            b.LastTouchId = p.Id;
            b.RestartKickerId = -1; b.ReleasedRestart = RestartKind.None;
            return true;
        }
        public bool AllowDirectGoal(Team scoring)
        {
            var b = State.Ball;
            if (b.RestartKickerId < 0) return true;
            Team kickerTeam = State.Players[b.RestartKickerId].Team;
            // 界外球不能直接得分；任意球、角球、球门球不能直接踢进本方门。
            if (b.ReleasedRestart != RestartKind.ThrowIn && scoring == kickerTeam) return true;
            Team defending = Opponent(scoring);
            int side = scoring == Team.Cyan ? 1 : -1;
            Begin(scoring == kickerTeam ? RestartKind.GoalKick : RestartKind.Corner,
                scoring == kickerTeam ? defending : scoring,
                new Vec2(side * (Rules.FieldWidth / 2 - (scoring == kickerTeam ? 3 : Rules.BallRadius + .08f)),
                    scoring == kickerTeam ? 0 : (b.Position.Y >= 0 ? 1 : -1) * (Rules.FieldHeight / 2 - Rules.BallRadius - .08f)));
            return false;
        }
    }
}
