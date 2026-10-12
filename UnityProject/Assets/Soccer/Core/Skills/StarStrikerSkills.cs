using System;

namespace Soccer.Core
{
    /// <summary>枪兵与雷神：所有技能保持平面规则，计时和费用由 Core 结算。</summary>
    internal sealed class StarStrikerSkills
    {
        private readonly MatchSimulation sim;
        public StarStrikerSkills(MatchSimulation simulation) { sim = simulation; }

        public bool ProcessInput(PlayerState p, PlayerCommand command)
        {
            if (!StarStrikerLoadouts.IsReference(p.Stats.Id) || command.Cancel) return false;
            bool marine = p.Stats.Id == CharacterId.Marine;
            var tuning = p.Stats.ReferenceSkills;
            if (marine && command.SpecialPressed)
            {
                var bomb = sim.State.MutableBombs.Find(b => b.ActorId == p.Id);
                if (bomb == null) { sim.Emit(MatchEventKind.ActionRejected, p.Id, slot: SkillSlot.Special); return false; }
                if (!Spend(p, 10, 0, SkillSlot.Special)) return false;
                SkillSystem.Activated(sim, p, SkillSlot.Special);
                sim.State.MutableBombs.Remove(bomb);
                Explode(bomb);
                return true;
            }
            if (command.QPressed && p.QCooldown <= 0)
            {
                if (!Spend(p, p.Stats.Loadout.QEnergy, p.Stats.QHealthCost, SkillSlot.Q)) return false;
                SkillSystem.Activated(sim, p, SkillSlot.Q);
                p.AttackPending = false; p.QCooldown = p.Stats.QCooldown;
                p.SkillSpeedBonus = marine ? tuning.MarineStimSpeed : tuning.ThorDriveSpeed;
                p.SkillSpeedRemaining = marine ? tuning.MarineStimDuration : tuning.ThorDriveDuration;
                p.OverdriveDecayTimer = marine ? 0 : tuning.ThorDecayInterval;
                return true;
            }
            if (command.EPressed && p.ECooldown <= 0)
            {
                if (!Spend(p, p.Stats.Loadout.EEnergy, p.Stats.EHealthCost, SkillSlot.E)) return false;
                SkillSystem.Activated(sim, p, SkillSlot.E);
                p.AttackPending = false; p.ECooldown = p.Stats.ECooldown;
                if (marine)
                {
                    bool onBall = sim.State.Ball.OwnerId == p.Id;
                    sim.State.MutableBombs.Add(new RemoteBomb { ActorId = p.Id, Team = p.Team,
                        AttachedToBall = onBall, Position = onBall ? sim.State.Ball.Position : p.Position,
                        Remaining = tuning.BombFuse, Radius = tuning.BombRadius });
                }
                else
                {
                    p.QCooldown = 0;
                    p.Dash = DashKind.Boost; p.DashDirection = p.Facing;
                    p.DashWindup = 0; p.DashRemaining = tuning.ThorDashDuration;
                    p.DashSpeed = tuning.ThorDashDistance / tuning.ThorDashDuration;
                }
                return true;
            }
            return false;
        }

        private bool Spend(PlayerState p, float energy, float health, SkillSlot slot)
        {
            if (p.Energy + 0.0001f < energy || p.Health <= health + 0.0001f)
            { sim.Emit(MatchEventKind.ActionRejected, p.Id, slot: slot); return false; }
            p.Energy = Math.Max(0, p.Energy - energy); p.Health -= health;
            p.EnergyDelay = sim.Rules.EnergyRegenDelay;
            return true;
        }

        public void Step(float dt)
        {
            foreach (var p in sim.State.Players)
            {
                if (p.IsDead || !StarStrikerLoadouts.IsReference(p.Stats.Id) || p.SkillSpeedRemaining <= 0) continue;
                p.SkillSpeedRemaining = Math.Max(0, p.SkillSpeedRemaining - dt);
                if (p.Stats.Id == CharacterId.Thor)
                {
                    p.OverdriveDecayTimer -= dt;
                    while (p.OverdriveDecayTimer <= 0.00001f)
                    {
                        p.SkillSpeedBonus *= p.Stats.ReferenceSkills.ThorDecayRetention;
                        p.OverdriveDecayTimer += p.Stats.ReferenceSkills.ThorDecayInterval;
                    }
                }
                if (p.SkillSpeedRemaining <= 0) p.SkillSpeedBonus = p.OverdriveDecayTimer = 0;
            }
            for (int i = sim.State.MutableBombs.Count - 1; i >= 0; i--)
            {
                var bomb = sim.State.MutableBombs[i];
                if (bomb.AttachedToBall) bomb.Position = sim.State.Ball.Position;
                bomb.Remaining -= dt;
                if (bomb.Remaining > 0.0001f) continue;
                sim.State.MutableBombs.RemoveAt(i);
                Explode(bomb);
            }
        }

        private void Explode(RemoteBomb bomb)
        {
            if (bomb.AttachedToBall) bomb.Position = sim.State.Ball.Position;
            var caster = sim.State.Players[bomb.ActorId];
            var tuning = caster.Stats.ReferenceSkills;
            sim.Emit(MatchEventKind.Explosion, caster.Id, slot: SkillSlot.E, strength: bomb.Radius, position: bomb.Position);
            foreach (var target in sim.State.Players)
            {
                if (target.IsDead || target.Team == bomb.Team || target.FullProtection > 0 || target.ControlProtection > 0 ||
                    Vec2.Distance(target.Position, bomb.Position) > bomb.Radius + target.Stats.Radius) continue;
                sim.ApplySkillHit(caster, target, 0, tuning.BombStun, SkillSlot.E);
                var direction = target.Position - bomb.Position;
                if (direction.LengthSquared < 0.0001f) direction = caster.Facing;
                TackleSystem.ApplyPush(sim, caster, target, direction, direction, tuning.BombPushDistance, SkillSlot.E);
            }
        }
    }
}
