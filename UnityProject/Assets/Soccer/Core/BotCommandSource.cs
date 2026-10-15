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
        private BotTactics.KickPlan kickPlan;
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
            var command=cached.WithoutEdges();
            if(state.Tick>=nextDecision)
            {
                nextDecision=state.Tick+BotDifficulties.DecisionTicks(rules.BotDifficulty);
                command=new PlayerCommand();
                if(state.Ball.OwnerId==p.Id)ReadCarrier(state,p,ref command);
                else
                {
                    charging=false;int closest=BotTactics.Chaser(state,p.Team);
                    var target=BotTactics.Position(state,rules,p,closest);
                    command.Move=Vec2.Distance(p.Position,target)>.4f ? (target-p.Position).Normalized : Vec2.Zero;
                    command.Aim=(state.Ball.Position-p.Position).Normalized;
                    if(state.Owner!=null && state.Owner.Team!=p.Team && closest==p.Id &&
                        Vec2.Distance(state.Owner.Position,p.Position)<2.4f && p.TackleCooldown<=0 &&
                        Vec2.Distance(p.Position,state.Ball.Position)>p.Stats.Radius+rules.BallRadius+.12f &&
                        !TackleSystem.IsRearContact(state.Owner,state.Owner.Position-p.Position,rules))
                    {command.TacklePressed=true;command.Aim=(state.Owner.Position-p.Position).Normalized;}
                }
                cached=command.WithoutEdges();
            }
            FinishKick(state,p,ref command);command.Navigate=true;return command;
        }
        private void ReadCarrier(MatchState state,PlayerState p,ref PlayerCommand command)
        {
            float side=p.Team==Team.Cyan ? 1 : -1;
            command.Move=new Vec2(side,-p.Position.Y*.12f).Normalized;
            command.Aim=command.Move;
            if(!charging && p.KickCooldown<=0)
            {
                var plan=BotTactics.PlanKick(state,rules,p);
                if(plan.Ready)
                {
                    kickPlan=plan;charging=true;command.KickPressed=true;
                    float distance=Vec2.Distance(p.Position,plan.Target);
                    float speed=plan.Pass ? (float)Math.Sqrt(2*rules.BallDeceleration*Math.Max(0,distance-.7f)+9) : rules.FullKickSpeed;
                    speed/=(float)Math.Sqrt(p.Stats.KickDistance);
                    float charge=Geometry.Clamp((speed-rules.TapKickSpeed)/(rules.FullKickSpeed-rules.TapKickSpeed),0,1);
                    releaseTick=state.Tick+Math.Max(1,(long)Math.Ceiling(charge*rules.ChargeSeconds/MatchSimulation.TickSeconds));
                }
            }
            if(charging){command.Aim=(kickPlan.Target-p.Position).Normalized;if(kickPlan.Pass)command.Move=Vec2.Zero;}
        }
        private void FinishKick(MatchState state,PlayerState p,ref PlayerCommand command)
        {
            if(state.Ball.OwnerId!=p.Id)charging=false;
            if(charging)
            {
                if(kickPlan.Pass)
                {
                    var receiver=state.Players[kickPlan.ReceiverId];
                    var target=BotTactics.Lead(rules,p,receiver);
                    if(receiver.IsDead || receiver.Stun>0 || !BotTactics.ClearLane(state,p,target,rules.BallRadius))
                    {charging=false;command.Cancel=true;command.KickPressed=false;nextDecision=state.Tick+1;}
                    else kickPlan.Target=target;
                }
                command.Aim=(kickPlan.Target-p.Position).Normalized;
            }
            command.KickHeld=charging;
            if(charging && state.Tick>=releaseTick)
            {command.KickReleased=true;command.KickHeld=false;charging=false;}
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
                    ReadCarrier(state,p,ref command);
                }
                else
                {
                    charging = false;
                    int closest=BotTactics.Chaser(state,p.Team);
                    float best=Vec2.Distance(p.Position,state.Ball.Position);
                    bool teamHasBall=state.Owner!=null && state.Owner.Team==p.Team;
                    Vec2 target=BotTactics.Position(state,rules,p,closest);
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
            FinishKick(state,p,ref command);
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
