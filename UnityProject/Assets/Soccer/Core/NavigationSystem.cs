using System;
using System.Collections.Generic;

namespace Soccer.Core
{
    internal struct UnitOrder
    {
        public UnitOrderKind Kind;
        public Vec2 Position;
        public int TargetId;
    }

    /// <summary>持续右键指令、Shift 队列、动态重算路径。圆形可见图 A* 与局部扫掠共用角色半径。</summary>
    internal sealed class NavigationSystem
    {
        private readonly MatchSimulation sim;
        private readonly Queue<UnitOrder>[] queues;
        private readonly long[] nextPlan;
        private readonly Vec2[] plannedGoal;
        public NavigationSystem(MatchSimulation sim)
        {
            this.sim = sim;
            queues = new Queue<UnitOrder>[sim.State.Players.Count];
            nextPlan = new long[queues.Length]; plannedGoal = new Vec2[queues.Length];
            for (int i = 0; i < queues.Length; i++) queues[i] = new Queue<UnitOrder>();
        }
        public void Clear(PlayerState p)
        {
            queues[p.Id].Clear(); p.Order = UnitOrderKind.None; p.OrderTargetId = -1;
            p.QueuedOrders = 0; p.MutablePath.Clear(); nextPlan[p.Id] = 0;
        }
        public void ClearAll() { foreach (var p in sim.State.Players) Clear(p); }
        private void Set(PlayerState p, UnitOrder order)
        {
            p.Order = order.Kind; p.OrderPosition = order.Position; p.OrderTargetId = order.TargetId;
            p.MutablePath.Clear(); nextPlan[p.Id] = 0;
        }
        private void Complete(PlayerState p)
        {
            p.MutablePath.Clear();
            if (queues[p.Id].Count > 0) Set(p, queues[p.Id].Dequeue());
            else p.Order = UnitOrderKind.None;
            p.QueuedOrders = queues[p.Id].Count;
        }
        public PlayerCommand[] Prepare(PlayerCommand[] inputs)
        {
            var commands = (PlayerCommand[])inputs.Clone();
            foreach (var p in sim.State.Players)
            {
                var c = commands[p.Id];
                if (p.IsDead || p.RespawnedThisTick) { Clear(p); commands[p.Id] = default; continue; }
                if (c.StopPressed || c.Cancel) Clear(p);
                if (c.OrderPressed && !c.Cancel && IsValid(c))
                {
                    var order = new UnitOrder { Kind = c.OrderKind, Position = c.OrderPosition, TargetId = c.OrderTargetId };
                    if (c.QueueOrder && p.Order != UnitOrderKind.None)
                    { if (queues[p.Id].Count < 16) queues[p.Id].Enqueue(order); }
                    else { Clear(p); Set(p, order); }
                    p.QueuedOrders = queues[p.Id].Count;
                }
                if (c.Move.LengthSquared > .001f && !c.Navigate) Clear(p);
                if (p.Order == UnitOrderKind.None)
                {
                    if (c.Navigate && c.Move.LengthSquared > .001f)
                        c.Move = Steer(p, p.Position + c.Move.Normalized * 3);
                    commands[p.Id] = c; continue;
                }
                c.Move = Vec2.Zero;
                var destination = p.OrderPosition;
                PlayerState target = null;
                float stopping = .08f;
                if (p.Order == UnitOrderKind.ChaseBall)
                {
                    if (sim.State.Ball.OwnerId == p.Id) { Complete(p); commands[p.Id] = c; continue; }
                    destination = sim.State.Ball.Position;
                }
                else if (p.Order == UnitOrderKind.Attack || p.Order == UnitOrderKind.Follow)
                {
                    if (p.OrderTargetId < 0 || p.OrderTargetId >= sim.State.Players.Count ||
                        (target = sim.State.Players[p.OrderTargetId]).IsDead)
                    { Complete(p); commands[p.Id] = c; continue; }
                    destination = target.Position;
                    bool canAttack = p.Order == UnitOrderKind.Attack && target.Team != p.Team &&
                        sim.Rules.EnableCombat && p.Stats.Loadout.HasWeapon;
                    stopping = canAttack ? Math.Max(p.Stats.Radius + target.Stats.Radius + .1f,
                        p.Stats.Loadout.WeaponRange + target.Stats.Radius - .2f)
                        : p.Stats.Radius + target.Stats.Radius + .15f;
                    if (canAttack && Vec2.Distance(p.Position, target.Position) <= stopping &&
                        !c.KickHeld && !p.Charging && !c.QPressed && !c.EPressed && !c.TacklePressed)
                    { c.Aim = target.Position - p.Position; c.AttackHeld = true; }
                    if (Vec2.Distance(p.Position, destination) > stopping)
                        destination -= (destination - p.Position).Normalized * (stopping - .2f);
                    else { p.MutablePath.Clear(); commands[p.Id] = c; continue; }
                }
                var goal = NavigationPathfinder.ProjectDestination(sim.State, sim.Rules, p, destination);
                p.OrderPosition = target == null ? goal : target.Position;
                if (Vec2.Distance(p.Position, goal) <= .08f)
                { if (p.Order == UnitOrderKind.Move) Complete(p); commands[p.Id] = c; continue; }
                c.Move = Steer(p, goal);
                if (!p.Charging && !c.KickPressed && !c.KickHeld && !c.QPressed && !c.EPressed && !c.TacklePressed && c.Move.LengthSquared > .001f)
                    c.Aim = c.Move;
                commands[p.Id] = c;
            }
            return commands;
        }
        private bool IsValid(PlayerCommand c) => Enum.IsDefined(typeof(UnitOrderKind), c.OrderKind) && c.OrderKind != UnitOrderKind.None &&
            Geometry.IsFinite(c.OrderPosition.X) && Geometry.IsFinite(c.OrderPosition.Y);
        private Vec2 Steer(PlayerState p, Vec2 destination)
        {
            var goal = NavigationPathfinder.ProjectDestination(sim.State, sim.Rules, p, destination);
            while (p.MutablePath.Count > 0 && Vec2.Distance(p.Position, p.MutablePath[0]) < .08f) p.MutablePath.RemoveAt(0);
            bool blocked = p.MutablePath.Count > 0 && !NavigationPathfinder.ClearSegment(sim.State, sim.Rules, p, p.Position, p.MutablePath[0]);
            if (sim.State.Tick >= nextPlan[p.Id] || Vec2.Distance(goal, plannedGoal[p.Id]) > .4f || blocked)
            {
                p.MutablePath.Clear(); p.MutablePath.AddRange(NavigationPathfinder.Find(sim.State, sim.Rules, p, goal));
                plannedGoal[p.Id] = goal; nextPlan[p.Id] = sim.State.Tick + 12;
            }
            if (p.MutablePath.Count == 0) return Vec2.Zero;
            var offset = p.MutablePath[0] - p.Position;
            // 最后一帧按剩余距离减速，避免在目标点左右振荡。
            float speed = sim.MovementSpeed(p);
            return offset.Normalized * Math.Min(1, offset.Length / (speed * MatchSimulation.TickSeconds));
        }
    }

    public static class NavigationPathfinder
    {
        private struct Obstacle { public Vec2 Position; public float Radius; }
        private static List<Obstacle> Obstacles(MatchState state, MatchRules rules, PlayerState p)
        {
            var circles = new List<Obstacle>();
            foreach (var other in state.Players)
                if (other.Id != p.Id && !other.IsDead)
                    circles.Add(new Obstacle { Position = other.Position, Radius = p.Stats.Radius + other.Stats.Radius + rules.NavigationClearance });
            for (int sign = -1; sign <= 1; sign += 2) for (int end = -1; end <= 1; end += 2)
                circles.Add(new Obstacle { Position = new Vec2(sign * rules.FieldWidth / 2, end * rules.GoalWidth / 2), Radius = p.Stats.Radius + rules.PostRadius + rules.NavigationClearance });
            if (state.IsOpeningWarmup)
                circles.Add(new Obstacle { Position = state.Ball.Position, Radius = rules.OpeningShieldRadius + p.Stats.Radius + rules.NavigationClearance });
            if (state.Phase == MatchPhase.SetPiece && p.Id != state.RestartPlayerId)
                circles.Add(new Obstacle { Position = state.RestartPosition, Radius = (p.Team != state.RestartTeam ? rules.SetPieceDistance : rules.BallRadius) + p.Stats.Radius + rules.NavigationClearance });
            foreach (var e in state.Effects)
            {
                if (e.Kind != FieldEffectKind.Shield || e.Windup > 0) continue;
                var tangent = new Vec2(-e.Direction.Y, e.Direction.X);
                int count = Math.Max(1, (int)Math.Ceiling(e.Length / .4f));
                for (int i = 0; i <= count; i++) circles.Add(new Obstacle { Position = e.Position + tangent * (e.Length * ((float)i / count - .5f)), Radius = p.Stats.Radius + .26f + rules.NavigationClearance });
            }
            return circles;
        }
        internal static Vec2 Clamp(MatchRules rules, PlayerState p, Vec2 point) => new Vec2(
            Geometry.Clamp(point.X, -rules.FieldWidth / 2 + p.Stats.Radius, rules.FieldWidth / 2 - p.Stats.Radius),
            Geometry.Clamp(point.Y, -rules.FieldHeight / 2 + p.Stats.Radius, rules.FieldHeight / 2 - p.Stats.Radius));
        public static Vec2 ProjectDestination(MatchState state, MatchRules rules, PlayerState p, Vec2 destination)
        {
            var result = Clamp(rules, p, destination); var obstacles = Obstacles(state, rules, p);
            for (int pass = 0; pass < 12; pass++)
            {
                bool moved = false;
                foreach (var o in obstacles)
                {
                    var d = result - o.Position;
                    if (d.Length >= o.Radius + .002f) continue;
                    var normal = d.LengthSquared > .00001f ? d.Normalized : (p.Position - o.Position).Normalized;
                    result = Clamp(rules, p, o.Position + normal * (o.Radius + .005f)); moved = true;
                }
                if (!moved) break;
            }
            return result;
        }
        public static bool ClearSegment(MatchState state, MatchRules rules, PlayerState p, Vec2 from, Vec2 to)
            => Clear(from, to, Obstacles(state, rules, p));
        private static bool Clear(Vec2 from, Vec2 to, List<Obstacle> obstacles)
        {
            foreach (var o in obstacles)
            {
                if (Vec2.Distance(to, o.Position) < o.Radius - .0001f) return false;
                if (Geometry.SweepCircle(from, to - from, o.Position, o.Radius, out _)) return false;
            }
            return true;
        }
        public static List<Vec2> Find(MatchState state, MatchRules rules, PlayerState p, Vec2 destination)
        {
            var goal = ProjectDestination(state, rules, p, destination);
            var obstacles = Obstacles(state, rules, p);
            if (Clear(p.Position, goal, obstacles)) return new List<Vec2> { goal };
            var nodes = new List<Vec2> { p.Position, goal };
            const int samples = 16;
            foreach (var o in obstacles)
            {
                float radius = (o.Radius + .015f) / (float)Math.Cos(Math.PI / samples);
                for (int i = 0; i < samples; i++)
                {
                    var candidate = o.Position + Vec2.Rotate(new Vec2(radius, 0), i * 2 * (float)Math.PI / samples);
                    var bounded = Clamp(rules, p, candidate);
                    if (Vec2.Distance(bounded, candidate) > .0001f) continue;
                    bool inside = false;
                    foreach (var other in obstacles) if (Vec2.Distance(candidate, other.Position) < other.Radius) { inside = true; break; }
                    if (!inside) nodes.Add(candidate);
                }
            }
            var cost = new float[nodes.Count]; var previous = new int[nodes.Count]; var closed = new bool[nodes.Count];
            for (int i = 0; i < cost.Length; i++) { cost[i] = float.PositiveInfinity; previous[i] = -1; }
            cost[0] = 0;
            for (int step = 0; step < nodes.Count; step++)
            {
                int current = -1; float best = float.PositiveInfinity;
                for (int i = 0; i < nodes.Count; i++)
                { float priority = cost[i] + Vec2.Distance(nodes[i], goal); if (!closed[i] && priority < best) { current = i; best = priority; } }
                if (current < 0) break;
                if (current == 1)
                {
                    var path = new List<Vec2>(); for (int i = 1; i > 0; i = previous[i]) path.Add(nodes[i]);
                    path.Reverse(); return path;
                }
                closed[current] = true;
                for (int i = 1; i < nodes.Count; i++)
                {
                    float candidate = cost[current] + Vec2.Distance(nodes[current], nodes[i]);
                    if (closed[i] || candidate >= cost[i] || !Clear(nodes[current], nodes[i], obstacles)) continue;
                    cost[i] = candidate; previous[i] = current;
                }
            }
            return new List<Vec2>(); // 无可通行路线时等待重算，绝不穿过阻挡。
        }
    }
}
