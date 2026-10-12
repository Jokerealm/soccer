namespace Soccer.Core
{
    internal sealed class WindShuttleSkills : ICharacterSkills
    {
        public void ProcessInput(MatchSimulation sim, PlayerState p, PlayerCommand command)
        {
            var stats = p.Stats;
            if (command.QPressed && p.QCooldown <= 0)
            {
                p.Dash = DashKind.Slide;
                p.DashDirection = p.Facing;
                p.DashWindup = stats.QWindup;
                p.DashRemaining = stats.QDuration;
                p.DashSpeed = stats.QDistance / stats.QDuration;
                p.QCooldown = stats.QCooldown;
                SkillSystem.Activated(sim, p, SkillSlot.Q);
            }
            if (command.EPressed && p.ECooldown <= 0) p.PreviewingE = true;
            if (command.EReleased && p.PreviewingE && p.ECooldown <= 0 && p.Dash == DashKind.None)
            {
                sim.State.MutableEffects.Add(new FieldEffect
                {
                    Kind = FieldEffectKind.SpeedLane, Team = p.Team, Position = p.Position, Direction = p.Facing,
                    Length = stats.LaneLength, Width = stats.LaneWidth, Windup = stats.EWindup,
                    Remaining = stats.EDuration, Strength = stats.LaneSpeedBonus
                });
                p.ECooldown = stats.ECooldown;
                p.PreviewingE = false;
                SkillSystem.Activated(sim, p, SkillSlot.E);
            }
        }
    }
}
