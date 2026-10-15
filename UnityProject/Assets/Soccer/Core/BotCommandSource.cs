using System;

namespace Soccer.Core
{
    /// <summary>训练机器人只读取公开场上状态；每 0.18 秒重新作决定，不读本地鼠标。</summary>
    public sealed class BotCommandSource : ICommandSource
    {
        private readonly MatchRules rules;
        private PlayerCommand cached;
        private long nextDecision;
        private bool charging;
        private long releaseTick;
        public BotCommandSource(MatchRules rules) { this.rules = rules; }
        public void Reset() { cached = default; nextDecision = 0; charging = false; }

        public PlayerCommand ReadCommand(MatchState state, int playerId)
        {
            var p = state.Players[playerId];
            if(state.IsOpeningWarmup && !p.IsDead)
            {
                Reset();
                float time=state.Tick*MatchSimulation.TickSeconds,phase=playerId*1.7f;
                return new PlayerCommand {Move=new Vec2((float)Math.Sin(time*2+phase),(float)Math.Cos(time*2+phase))*.35f,
                    Aim=state.Ball.Position-p.Position};
            }
            if (state.Phase == MatchPhase.SetPiece && !p.IsDead)
            {
                Reset();
                if (state.RestartPlayerId != p.Id || state.PhaseRemaining > 0) return new PlayerCommand { Aim = p.Facing };
                float side = p.Team == Team.Cyan ? 1 : -1;
                var aim = state.Restart == RestartKind.ThrowIn || state.Restart == RestartKind.Corner
                    ? new Vec2(-state.RestartPosition.X * .35f, -state.RestartPosition.Y)
                    : new Vec2(side, -p.Position.Y * .08f);
                return new PlayerCommand { Aim = aim, KickPressed = !p.Charging, KickHeld = true, KickReleased = p.Charging && p.ChargeTime >= .65f };
            }
            if (state.Phase != MatchPhase.Playing || p.IsDead || p.Stun > 0 || p.PushRemaining > 0)
            { Reset(); return new PlayerCommand { Aim = p.Facing }; }
            if (rules.EnableCombat) return ReadCombatCommand(state, p);
            var command = cached.WithoutEdges();
            if (state.Tick >= nextDecision)
            {
                nextDecision = state.Tick + BotDifficulties.DecisionTicks(rules.BotDifficulty);
                float side = p.Team == Team.Cyan ? 1 : -1;
                Vec2 target;
                if (state.Ball.OwnerId == p.Id)
                {
                    var goal = new Vec2(side * (rules.FieldWidth / 2 + 0.5f), (p.Id % 2 == 0 ? 0.6f : -0.6f));
                    command.Aim = (goal - p.Position).Normalized;
                    command.Move = new Vec2(side, -p.Position.Y * 0.12f).Normalized;
                    if (!charging && p.KickCooldown <= 0 && Vec2.Distance(goal, p.Position) < 16)
                    {
                        charging = true;
                        releaseTick = state.Tick + 43;
                        command.KickPressed = true;
                    }
                }
                else
                {
                    charging = false;
                    int closestId = playerId;
                    float closest = float.MaxValue;
                    foreach (var teammate in state.Players)
                    {
                        if (teammate.Team != p.Team) continue;
                        float d = Vec2.Distance(teammate.Position, state.Ball.Position);
                        if (d < closest) { closest = d; closestId = teammate.Id; }
                    }
                    if (closestId == p.Id) target = state.Ball.Position;
                    else
                    {
                        bool support = state.Owner != null && state.Owner.Team == p.Team;
                        target = new Vec2(support ? state.Owner.Position.X + side * 3 : -side * (rules.FieldWidth / 2 - 4),
                            p.Id % 2 == 0 ? 3.5f : -3.5f);
                    }
                    var offset = target - p.Position;
                    command.Move = offset.Length < 0.4f ? Vec2.Zero : offset.Normalized;
                    command.Aim = (state.Ball.Position - p.Position).Normalized;
                    if (state.Owner != null && state.Owner.Team != p.Team &&
                        Vec2.Distance(state.Owner.Position, p.Position) < 2.4f && p.TackleCooldown <= 0 &&
                        Vec2.Distance(p.Position,state.Ball.Position) > p.Stats.Radius + rules.BallRadius + .12f &&
                        !TackleSystem.IsRearContact(state.Owner,state.Owner.Position-p.Position,rules))
                    { command.TacklePressed = true; command.Aim = (state.Owner.Position - p.Position).Normalized; }
                }
                cached = command.WithoutEdges();
            }
            command.KickHeld = charging;
            if (charging && state.Tick >= releaseTick)
            { command.KickReleased = true; command.KickHeld = false; charging = false; }
            command.Navigate = true;
            return command;
        }

        private PlayerCommand ReadCombatCommand(MatchState state, PlayerState p)
        {
            var command = cached.WithoutEdges();
            var kit = p.Stats.Loadout;
            if (state.Tick >= nextDecision)
            {
                nextDecision = state.Tick + BotDifficulties.DecisionTicks(rules.BotDifficulty);
                command = new PlayerCommand();
                float side = p.Team == Team.Cyan ? 1 : -1;
                PlayerState enemy = null;
                float enemyDistance = float.MaxValue;
                foreach (var other in state.Players)
                {
                    if (other.IsDead || other.Team == p.Team) continue;
                    float distance = Vec2.Distance(other.Position, p.Position);
                    if (distance < enemyDistance) { enemyDistance = distance; enemy = other; }
                }
                if (state.Ball.OwnerId == p.Id)
                {
                    var target = new Vec2(side * (rules.FieldWidth / 2 + 0.5f), p.Id % 2 == 0 ? 0.6f : -0.6f);
                    command.Move = new Vec2(side, -p.Position.Y * 0.12f).Normalized;
                    if (rules.BotDifficulty != BotDifficulty.BallBoy && (enemyDistance < 3 || p.Health < 45))
                    {
                        PlayerState receiver = null;
                        float bestSpace = enemyDistance + 0.8f;
                        foreach (var ally in state.Players)
                        {
                            float d = Vec2.Distance(ally.Position, p.Position);
                            if (ally.Id == p.Id || ally.IsDead || ally.Team != p.Team || d < 1.5f || d > 11 ||
                                (ally.Position.X - p.Position.X) * side < -4) continue;
                            float space = float.MaxValue;
                            foreach (var opponent in state.Players)
                                if (!opponent.IsDead && opponent.Team != p.Team)
                                    space = Math.Min(space, Vec2.Distance(opponent.Position, ally.Position));
                            if (space > bestSpace) { bestSpace = space; receiver = ally; }
                        }
                        if (receiver != null) target = receiver.Position;
                    }
                    if (rules.BotDifficulty >= BotDifficulty.God) target = BotTactics.KickTarget(state, rules, p);
                    command.Aim = (target - p.Position).Normalized;
                    if (rules.BotDifficulty == BotDifficulty.BallBoy) command.Aim = Vec2.Rotate(command.Aim, (p.Id % 2 == 0 ? 1 : -1) * .09f);
                    if (!charging && p.KickCooldown <= 0 && Vec2.Distance(target, p.Position) < 16)
                    {
                        charging = true;
                        float speed = (float)Math.Sqrt(2 * rules.BallDeceleration * Math.Min(17, Vec2.Distance(target, p.Position))) + 1;
                        float charge = Geometry.Clamp((speed - rules.TapKickSpeed) / (rules.FullKickSpeed - rules.TapKickSpeed), 0, 0.9f);
                        releaseTick = state.Tick + Math.Max(1, (long)(charge * rules.ChargeSeconds / MatchSimulation.TickSeconds));
                        command.KickPressed = true;
                    }
                }
                else
                {
                    charging = false;
                    int closest = p.Id;
                    float best = float.MaxValue;
                    foreach (var ally in state.Players)
                    {
                        if (ally.IsDead || ally.Team != p.Team) continue;
                        float distance = Vec2.Distance(ally.Position, state.Ball.Position);
                        if (distance < best) { best = distance; closest = ally.Id; }
                    }
                    bool teamHasBall = state.Owner != null && state.Owner.Team == p.Team;
                    Vec2 target = closest == p.Id && !teamHasBall ? state.Ball.Position : teamHasBall
                        ? state.Owner.Position + new Vec2(side * 3, p.Id % 2 == 0 ? 2.5f : -2.5f)
                        : new Vec2(-side * (rules.FieldWidth / 2 - 5), p.Id % 2 == 0 ? 3 : -3);
                    if (!teamHasBall && enemy != null && enemyDistance < 4 && closest != p.Id)
                        target = enemy.Position - (enemy.Position - p.Position).Normalized * Math.Min(3, kit.WeaponRange * 0.6f);
                    if (closest == p.Id && state.Owner != null && !teamHasBall) target = state.Ball.Position;
                    if (rules.BotDifficulty >= BotDifficulty.God) target = BotTactics.Position(state, rules, p, closest);
                    var offset = target - p.Position;
                    command.Move = offset.Length > 0.4f ? offset.Normalized : Vec2.Zero;
                    command.Aim = (state.Ball.Position - p.Position).Normalized;
                    if (state.Owner != null && state.Owner.Team != p.Team && best < 2.4f && closest == p.Id && p.TackleCooldown <= 0 &&
                        Vec2.Distance(p.Position,state.Ball.Position) > p.Stats.Radius + rules.BallRadius + .12f &&
                        p.Health > rules.TackleHealthCost(p.Stats) + 1 &&
                        !TackleSystem.IsRearContact(state.Owner,state.Owner.Position-p.Position,rules))
                    { command.TacklePressed = true; command.Aim = (state.Owner.Position - p.Position).Normalized; }
                    else if (enemy != null && enemyDistance < kit.WeaponRange + enemy.Stats.Radius && enemy.FullProtection <= 0)
                    { command.AttackHeld = true; command.Aim = (enemy.Position - p.Position).Normalized; }
                    if (!command.TacklePressed && enemy != null && enemyDistance < kit.QRange && enemy.FullProtection <= 0 &&
                        enemy.Stun <= 0 && p.QCooldown <= 0 && p.Energy >= kit.QEnergy)
                    { command.QPressed = true; command.Aim = (enemy.Position - p.Position).Normalized; }
                }
                if (!charging && p.ECooldown <= 0 && p.Energy >= kit.EEnergy)
                {
                    bool needsSupport = false;
                    foreach (var ally in state.Players)
                    {
                        if (ally.IsDead || ally.Team != p.Team || Vec2.Distance(ally.Position, p.Position) > kit.SupportRadius) continue;
                        if (kit.Support == SupportKind.HealingPulse && ally.Health < ally.Stats.MaxHealth - 20) needsSupport = true;
                        else if (kit.Support == SupportKind.TeamShield && enemyDistance < 4 && ally.ShieldHealth <= 0) needsSupport = true;
                        else if (kit.Support == SupportKind.TeamBoost && state.Owner != null && state.Owner.Team == p.Team &&
                            state.Owner.Id == ally.Id && ally.BoostRemaining <= 0) needsSupport = true;
                    }
                    if (needsSupport) { command.EPressed = true; command.QPressed = false; command.AttackHeld = false; }
                }
                if (StarStrikerLoadouts.IsReference(p.Stats.Id))
                {
                    command.QPressed = command.EPressed = false;
                    if (!charging && !command.TacklePressed)
                    {
                        bool moving = command.Move.LengthSquared > 0.25f;
                        command.QPressed = moving && p.SkillSpeedRemaining <= 0 && p.QCooldown <= 0 &&
                            p.Health > p.Stats.QHealthCost + 15 && p.Energy >= kit.QEnergy;
                        if (p.Stats.Id == CharacterId.Marine && enemyDistance < 2.5f && p.ECooldown <= 0 && p.Energy >= kit.EEnergy)
                            command.EPressed = true;
                        else if (p.Stats.Id == CharacterId.Thor && moving && Vec2.Distance(state.Ball.Position, p.Position) > 3 &&
                            p.ECooldown <= 0 && p.Energy >= kit.EEnergy && p.Health > p.Stats.EHealthCost + 15)
                        { command.EPressed = true; command.Aim = command.Move.Normalized; }
                        if (command.EPressed) command.QPressed = command.AttackHeld = false;
                        var bomb = state.Bombs.Count > 0 ? FindBomb(state, p.Id) : null;
                        if (bomb != null && p.Energy >= 10)
                            foreach (var target in state.Players)
                                if (!target.IsDead && target.Team != p.Team && target.FullProtection <= 0 &&
                                    Vec2.Distance(target.Position, bomb.AttachedToBall ? state.Ball.Position : bomb.Position) < bomb.Radius + target.Stats.Radius)
                                { command.SpecialPressed = true; command.QPressed = command.EPressed = command.AttackHeld = false; break; }
                    }
                }
                if (rules.BotDifficulty == BotDifficulty.BallBoy)
                    command.QPressed = command.EPressed = command.SpecialPressed = false;
                else if (rules.BotDifficulty >= BotDifficulty.God && state.Ball.OwnerId != p.Id)
                {
                    // Save costly mobility while safely covering; punish an exhausted carrier instead.
                    bool contest = state.Owner != null && state.Owner.Team != p.Team && enemyDistance < 7;
                    if (!contest && Vec2.Distance(state.Ball.Position,p.Position) > 10)
                        command.QPressed = command.EPressed = false;
                    if (p.Health < p.Stats.MaxHealth * .4f) command.QPressed = false;
                }
                cached = command.WithoutEdges();
            }
            if (state.Ball.OwnerId != p.Id) charging = false;
            command.KickHeld = charging;
            if (charging && state.Tick >= releaseTick)
            { command.KickReleased = true; command.KickHeld = false; charging = false; }
            command.Navigate = true;
            return command;
        }

        private static RemoteBomb FindBomb(MatchState state, int actorId)
        {
            foreach (var bomb in state.Bombs) if (bomb.ActorId == actorId) return bomb;
            return null;
        }
    }
}
