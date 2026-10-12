using System;

namespace Soccer.Core
{
    public static class TackleSystem
    {
        public static float ContactAngle(Vec2 direction, Vec2 relative)
            => (float)(Math.Acos(Geometry.Clamp(Vec2.Dot(direction.Normalized, relative.Normalized), -1, 1)) * 180 / Math.PI);

        // relative 从推者的接触点指向被推者。背后推与被推者朝向同向，和推者瞄准无关。
        public static bool IsRearContact(PlayerState target, Vec2 relative, MatchRules rules)
            => ContactAngle(target.Facing, relative) <= rules.RearPushCarryAngleDegrees + .0001f;

        internal static void Resolve(MatchSimulation sim, PlayerState attacker, PlayerState target, Vec2 relative)
        {
            if (attacker.Id == target.Id || attacker.IsDead || target.IsDead) return;
            attacker.Dash = DashKind.None;
            attacker.ActionLock = sim.Rules.TackleRecovery;
            float angle = ContactAngle(target.Facing, relative);
            if (target.FullProtection > 0 || target.ControlProtection > 0)
            { sim.Emit(MatchEventKind.Protected, attacker.Id, target.Id, angle); return; }
            float resistance = target.RootRemaining > 0 ? target.Stats.RootResistance : target.Stats.PushResistance;
            float force = sim.Rules.EnableCombat ? attacker.Stats.PushPower / .5f : 1;
            float distance = sim.Rules.PushDistance * force * (1 - Geometry.Clamp(resistance, 0, .6f));
            ApplyPush(sim, attacker, target, attacker.DashDirection, relative, distance);
        }

        /// <summary>推只腾出空间，不赋予球权。队友／背后受推保留球；敌方正面或侧面受推让球原地成为自由球。</summary>
        internal static void ApplyPush(MatchSimulation sim, PlayerState actor, PlayerState target, Vec2 direction,
            Vec2 relative, float distance, SkillSlot slot = SkillSlot.None)
        {
            if (actor.Id == target.Id || actor.IsDead || target.IsDead || distance <= 0) return;
            var rules = sim.Rules;
            MatchSimulation.CancelCharge(target);
            target.Preparation = ShotPreparation.None;
            target.PreviewingE = target.AttackPending = false;
            target.Dash = DashKind.None;
            sim.ClearActions(target);
            target.PushVelocity = direction.Normalized * (distance / rules.PushDuration);
            target.PushRemaining = rules.PushDuration;
            target.ControlProtection = Math.Max(target.ControlProtection, rules.PushDuration + rules.ControlProtection);
            bool rear = IsRearContact(target, relative, rules);
            if (sim.State.Ball.OwnerId == target.Id && actor.Team != target.Team && !rear)
                sim.DropBallFromPush(target, actor);
            sim.Emit(MatchEventKind.Push, actor.Id, target.Id, ContactAngle(target.Facing, relative), slot: slot, position: target.Position);
        }
    }
}
