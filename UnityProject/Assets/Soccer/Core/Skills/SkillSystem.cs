using System;

namespace Soccer.Core
{
    internal interface ICharacterSkills
    {
        void ProcessInput(MatchSimulation sim, PlayerState player, PlayerCommand command);
    }

    internal sealed class SkillSystem
    {
        private readonly MatchSimulation sim;
        private readonly ICharacterSkills[] behaviours = { new WindShuttleSkills(), new IronGuardSkills(), new ArcLightSkills() };
        public SkillSystem(MatchSimulation sim) { this.sim = sim; }
        public void ProcessInput(PlayerState p, PlayerCommand command)
        {
            if (command.Cancel) return;
            if ((int)p.Stats.Id >= behaviours.Length) return;
            behaviours[(int)p.Stats.Id].ProcessInput(sim, p, command);
        }

        public void Step(float dt)
        {
            for (int i = sim.State.MutableEffects.Count - 1; i >= 0; i--)
            {
                var e = sim.State.MutableEffects[i];
                if (e.Windup > 0)
                {
                    float previous = e.Windup;
                    e.Windup = Math.Max(0, e.Windup - dt);
                    if (e.Windup <= 0 && e.Kind == FieldEffectKind.Shield) DepenetrateShield(e);
                    e.Remaining -= Math.Max(0, dt - previous);
                }
                else e.Remaining -= dt;
                if (e.Remaining <= 0) sim.State.MutableEffects.RemoveAt(i);
            }
        }

        private void DepenetrateShield(FieldEffect e)
        {
            var b = sim.State.Ball;
            if (b.OwnerId >= 0) return;
            Vec2 relative = b.Position - e.Position;
            var tangent = new Vec2(-e.Direction.Y, e.Direction.X);
            float normalDistance = Vec2.Dot(relative, e.Direction);
            float lateral = Math.Abs(Vec2.Dot(relative, tangent));
            float thickness = sim.Rules.BallRadius + 0.06f;
            if (Math.Abs(normalDistance) >= thickness || lateral > e.Length / 2 + sim.Rules.BallRadius) return;
            int side = normalDistance < 0 ? -1 : 1;
            var candidate = b.Position + e.Direction * (side * thickness - normalDistance);
            // 新建技能物不允许把球推出底线制造进球。
            candidate.X = Geometry.Clamp(candidate.X, -sim.Rules.FieldWidth / 2 + sim.Rules.BallRadius, sim.Rules.FieldWidth / 2 - sim.Rules.BallRadius);
            candidate.Y = Geometry.Clamp(candidate.Y, -sim.Rules.FieldHeight / 2 + sim.Rules.BallRadius, sim.Rules.FieldHeight / 2 - sim.Rules.BallRadius);
            b.Position = candidate;
            var normal = e.Direction * side;
            if (Vec2.Dot(b.Velocity, normal) < 0)
                b.Velocity = (b.Velocity - normal * (2 * Vec2.Dot(b.Velocity, normal))) * e.Strength;
            b.CurveRemaining = 0;
        }

        internal static void Activated(MatchSimulation sim, PlayerState p, SkillSlot slot)
        {
            MatchSimulation.RemoveKickoffProtection(p);
            MatchSimulation.CancelCharge(p);
            sim.Emit(MatchEventKind.Skill, p.Id, slot: slot);
        }
    }
}
