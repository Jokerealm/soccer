namespace Soccer.Core
{
    internal sealed class ArcLightSkills : ICharacterSkills
    {
        public void ProcessInput(MatchSimulation sim, PlayerState p, PlayerCommand command)
        {
            if (sim.State.Ball.OwnerId != p.Id) return; // 无球不施放、不消耗冷却。
            if (command.QPressed && p.QCooldown <= 0)
            {
                p.Preparation = ShotPreparation.Curve;
                p.PreparationFacing = p.Facing;
                p.PreparationRemaining = p.Stats.PreparationLifetime;
                p.QCooldown = p.Stats.QCooldown;
                SkillSystem.Activated(sim, p, SkillSlot.Q);
            }
            if (command.EPressed && p.ECooldown <= 0)
            {
                p.Preparation = ShotPreparation.Power;
                p.PreparationRemaining = p.Stats.PreparationLifetime;
                p.ECooldown = p.Stats.ECooldown;
                SkillSystem.Activated(sim, p, SkillSlot.E);
            }
        }
    }
}
