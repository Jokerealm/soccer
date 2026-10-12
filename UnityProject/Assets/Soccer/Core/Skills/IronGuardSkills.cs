namespace Soccer.Core
{
    internal sealed class IronGuardSkills : ICharacterSkills
    {
        public void ProcessInput(MatchSimulation sim, PlayerState p, PlayerCommand command)
        {
            var stats = p.Stats;
            if (command.QPressed && p.QCooldown <= 0)
            {
                var position = p.Position + p.Facing * (stats.Radius + 0.5f);
                float margin = stats.ShieldLength / 2 + sim.Rules.BallRadius;
                position.X = Geometry.Clamp(position.X, -sim.Rules.FieldWidth / 2 + margin, sim.Rules.FieldWidth / 2 - margin);
                position.Y = Geometry.Clamp(position.Y, -sim.Rules.FieldHeight / 2 + margin, sim.Rules.FieldHeight / 2 - margin);
                sim.State.MutableEffects.Add(new FieldEffect
                {
                    Kind = FieldEffectKind.Shield, Team = p.Team, Position = position, Direction = p.Facing,
                    Length = stats.ShieldLength, Width = 0.12f, Windup = stats.QWindup,
                    Remaining = stats.QDuration, Strength = stats.ShieldBounce
                });
                p.QCooldown = stats.QCooldown;
                SkillSystem.Activated(sim, p, SkillSlot.Q);
            }
            if (command.EPressed && p.ECooldown <= 0)
            {
                p.RootRemaining = stats.EDuration;
                p.ECooldown = stats.ECooldown;
                SkillSystem.Activated(sim, p, SkillSlot.E);
            }
        }
    }
}
