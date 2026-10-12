using System;

namespace Soccer.Core
{
    /// <summary>短后摇期间保留输入意图；超时、失球、控制与取消会清除，绝不等待长冷却。</summary>
    internal sealed class PlayerActionBuffer
    {
        private float tackle, q, e, kick, eConfirm, weapon;
        private bool kickReleased, eReleased;

        public void Clear()
        {
            tackle = q = e = kick = eConfirm = weapon = 0;
            kickReleased = eReleased = false;
        }

        public PlayerCommand Prepare(PlayerCommand input, PlayerState p, MatchRules rules, bool hasBall, float dt)
        {
            tackle = Math.Max(0, tackle - dt);
            q = Math.Max(0, q - dt);
            e = Math.Max(0, e - dt);
            kick = Math.Max(0, kick - dt);
            weapon = Math.Max(0, weapon - dt);
            if (eConfirm > 0)
            {
                eConfirm = Math.Max(0, eConfirm - dt);
                if (eConfirm <= 0) p.PreviewingE = false;
            }
            if (kick <= 0) kickReleased = false;
            if (e <= 0) eReleased = false;
            if (input.Cancel || p.IsDead || p.Stun > 0 || p.PushRemaining > 0)
            {
                Clear();
                return new PlayerCommand { Move = input.Move, Aim = input.Aim, Cancel = input.Cancel };
            }
            float window = Math.Max(dt, rules.ActionBufferSeconds);
            if (input.AttackPressed && rules.EnableCombat && p.Ammo > 0 && p.WeaponCooldown <= rules.ActionBufferSeconds)
                weapon = window;
            if (input.TacklePressed && p.TackleCooldown <= rules.ActionBufferSeconds) tackle = window;
            bool skillAllowed = rules.EnableCombat || p.Stats.Id != CharacterId.ArcLight || hasBall;
            if (!skillAllowed) q = e = 0;
            if (input.QPressed && skillAllowed && p.QCooldown <= rules.ActionBufferSeconds) q = window;
            if (input.EPressed && skillAllowed && p.ECooldown <= rules.ActionBufferSeconds) e = window;
            if (!hasBall) { kick = 0; kickReleased = false; }
            if (input.KickPressed && hasBall && p.KickCooldown <= rules.ActionBufferSeconds)
            { kick = window; kickReleased = false; }
            if (kick > 0) kickReleased |= input.KickReleased;
            if (e > 0) eReleased |= input.EReleased;
            if (input.EReleased && (p.PreviewingE || e > 0)) eConfirm = window;
            bool ready = p.ActionLock <= 0 && p.Dash == DashKind.None;
            var result = input.WithoutEdges();
            result.Cancel = input.Cancel;
            result.AttackPressed = ready && weapon > 0 && p.WeaponCooldown <= 0;
            result.TacklePressed = ready && tackle > 0 && p.TackleCooldown <= 0;
            result.QPressed = ready && q > 0 && p.QCooldown <= 0;
            result.EPressed = ready && e > 0 && p.ECooldown <= 0;
            result.KickPressed = ready && kick > 0 && p.KickCooldown <= 0;
            result.SpecialPressed = ready && input.SpecialPressed;
            result.KickReleased = input.KickReleased || (ready && kick > 0 && kickReleased);
            result.EReleased = ready && (input.EReleased || eConfirm > 0 || (e > 0 && eReleased));
            return result;
        }

        public void ConsumeTackle() { tackle = 0; }
        public void ConsumeWeapon() { weapon = 0; }
        public void ConsumeQ() { q = 0; }
        public void ConsumeE() { e = 0; eReleased = false; }
        public void ConsumeERelease() { eConfirm = 0; }
        public void ConsumeKick() { kick = 0; kickReleased = false; }
    }
}
