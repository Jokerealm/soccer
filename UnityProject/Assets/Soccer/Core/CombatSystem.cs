using System;

namespace Soccer.Core
{
    /// <summary>普攻、可躲避的控制弹、团队支援与原地复活。伤害只作用于存活敌人。</summary>
    internal sealed class CombatSystem
    {
        private readonly MatchSimulation sim;
        private MatchState State => sim.State;
        public CombatSystem(MatchSimulation simulation) { sim = simulation; }

        public void UpdateLife(PlayerState p, float dt)
        {
            p.RespawnedThisTick = false;
            if (p.IsDead)
            {
                p.RespawnRemaining = Math.Max(0, p.RespawnRemaining - dt);
                if (p.RespawnRemaining <= 0.0001f)
                {
                    ResetPlayer(p);
                    p.Facing = new Vec2(p.Team == Team.Cyan ? 1 : -1, 0);
                    p.PreviousPosition = p.Position;
                    p.FullProtection = sim.Rules.RespawnProtection;
                    p.IsRespawnProtected = true;
                    p.RespawnedThisTick = true;
                    sim.ClearActions(p);
                    sim.Emit(MatchEventKind.Respawn, p.Id, position: p.Position);
                }
                return;
            }
            if (State.Phase != MatchPhase.Playing) return;
            p.Health = Math.Min(p.Stats.MaxHealth, p.Health + p.Stats.HealthRegen * dt);
            p.WeaponCooldown = Math.Max(0, p.WeaponCooldown - dt);
            p.WeaponVisual = Math.Max(0, p.WeaponVisual - dt);
            if (p.Ammo < p.Stats.Loadout.AmmoCapacity)
            {
                p.AmmoReloadRemaining -= dt;
                while (p.AmmoReloadRemaining <= 0.00001f && p.Ammo < p.Stats.Loadout.AmmoCapacity)
                { p.Ammo++; p.AmmoReloadRemaining += p.Stats.Loadout.AmmoReloadSeconds; }
                if (p.Ammo == p.Stats.Loadout.AmmoCapacity) p.AmmoReloadRemaining = 0;
            }
            float regenTime = Math.Max(0, dt - p.EnergyDelay);
            p.EnergyDelay = Math.Max(0, p.EnergyDelay - dt);
            p.Energy = Math.Min(p.Stats.MaxEnergy, p.Energy + p.Stats.EnergyRegen * regenTime);
            p.ShieldRemaining = Math.Max(0, p.ShieldRemaining - dt);
            if (p.ShieldRemaining <= 0) p.ShieldHealth = 0;
            p.BoostRemaining = Math.Max(0, p.BoostRemaining - dt);
        }

        internal static void ResetPlayer(PlayerState p)
        {
            p.Health = p.Stats.MaxHealth; p.Energy = p.Stats.MaxEnergy;
            p.Ammo = p.Stats.Loadout.AmmoCapacity; p.AmmoReloadRemaining = 0;
            p.RespawnRemaining = p.EnergyDelay = p.WeaponCooldown = p.WeaponWindup = p.WeaponVisual = 0;
            p.RespawnedThisTick = false;
            p.ShieldHealth = p.ShieldRemaining = p.BoostRemaining = p.BoostStrength = 0;
            p.KickCooldown = p.TackleCooldown = p.QCooldown = p.ECooldown = p.ActionLock = 0;
            p.SkillSpeedBonus = p.SkillSpeedRemaining = p.OverdriveDecayTimer = 0;
            p.FullProtection = p.ControlProtection = p.Stun = 0;
            p.PushRemaining = p.RootRemaining = p.RelayRemaining = p.RelayCooldown = 0;
            p.DashWindup = p.DashRemaining = p.DashSpeed = 0;
            p.Dash = DashKind.None; p.PushVelocity = p.Velocity = Vec2.Zero;
            p.Preparation = ShotPreparation.None; p.PreparationRemaining = p.KickVisual = 0;
            p.AttackPending = p.IsKickoffProtected = p.IsRespawnProtected = p.PreviewingE = false;
            p.Order = UnitOrderKind.None; p.QueuedOrders = 0; p.MutablePath.Clear();
            MatchSimulation.CancelCharge(p);
        }

        public bool ProcessInput(PlayerState p, PlayerCommand command)
        {
            if (command.Cancel || p.IsDead || p.RespawnedThisTick) return false;
            var kit = p.Stats.Loadout;
            bool reference = StarStrikerLoadouts.IsReference(p.Stats.Id);
            if (!reference && command.QPressed && p.QCooldown <= 0)
            {
                if (!SpendEnergy(p, kit.QEnergy, SkillSlot.Q)) return false;
                Activate(p, SkillSlot.Q);
                p.QCooldown = p.Stats.QCooldown;
                p.ActionLock = 0.14f;
                SpawnVolley(p, p.Facing, kit.QDamage, kit.QStun, kit.QRange, kit.QSpeed, kit.QRadius,
                    SkillSlot.Q, kit.QShots, kit.QSpreadDegrees, kit.QBounces);
                return true;
            }
            if (!reference && command.EPressed && p.ECooldown <= 0)
            {
                if (!SpendEnergy(p, kit.EEnergy, SkillSlot.E)) return false;
                Activate(p, SkillSlot.E);
                p.ECooldown = p.Stats.ECooldown;
                p.ActionLock = 0.14f;
                foreach (var ally in State.Players)
                {
                    if (ally.IsDead || ally.Team != p.Team || Vec2.Distance(ally.Position, p.Position) > kit.SupportRadius) continue;
                    float amount = kit.SupportStrength;
                    if (kit.Support == SupportKind.TeamShield)
                    { ally.ShieldHealth = Math.Max(ally.ShieldHealth, amount); ally.ShieldRemaining = kit.SupportDuration; }
                    else if (kit.Support == SupportKind.TeamBoost)
                    {
                        ally.BoostStrength = ally.BoostRemaining > 0 ? Math.Max(ally.BoostStrength, kit.SupportStrength) : kit.SupportStrength;
                        ally.BoostRemaining = Math.Max(ally.BoostRemaining, kit.SupportDuration);
                    }
                    else
                    {
                        amount = Math.Min(amount, ally.Stats.MaxHealth - ally.Health);
                        ally.Health += amount; p.HealingDone += amount;
                    }
                    sim.Emit(MatchEventKind.Support, p.Id, ally.Id, slot: SkillSlot.E, strength: amount, position: ally.Position);
                }
                return true;
            }
            if (kit.HasWeapon && (command.AttackPressed || command.AttackHeld) && p.Ammo > 0 && p.WeaponCooldown <= 0 && !p.AttackPending)
            {
                MatchSimulation.CancelCharge(p);
                MatchSimulation.RemoveKickoffProtection(p);
                p.WeaponCooldown = kit.WeaponCooldown;
                p.Ammo--; p.AmmoReloadRemaining = kit.AmmoReloadSeconds;
                p.WeaponWindup = kit.WeaponWindup;
                p.AttackFacing = p.Facing;
                p.AttackPending = true;
                p.ActionLock = Math.Max(0.09f, kit.WeaponWindup);
                sim.Emit(MatchEventKind.Weapon, p.Id, slot: SkillSlot.Weapon, position: p.Position);
                return true;
            }
            return false;
        }

        private bool SpendEnergy(PlayerState p, float amount, SkillSlot slot)
        {
            if (p.Energy + 0.0001f < amount)
            { sim.Emit(MatchEventKind.ActionRejected, p.Id, slot: slot); return false; }
            p.Energy = Math.Max(0, p.Energy - amount);
            p.EnergyDelay = sim.Rules.EnergyRegenDelay;
            return true;
        }
        private void Activate(PlayerState p, SkillSlot slot)
        {
            p.AttackPending = false;
            SkillSystem.Activated(sim, p, slot);
        }
        private void SpawnProjectile(PlayerState p, Vec2 facing, float damage, float stun, float range, float speed, float radius, SkillSlot slot)
        {
            // 从身体中心起算射程，避免脚前出生点落在近身目标后面。
            State.MutableProjectiles.Add(new CombatProjectile { ActorId = p.Id, Team = p.Team, Position = p.Position,
                Direction = facing, Speed = speed, Radius = radius, RemainingDistance = range,
                Damage = damage, StunSeconds = stun, Slot = slot });
        }

        private void SpawnVolley(PlayerState p, Vec2 facing, float damage, float stun, float range, float speed,
            float radius, SkillSlot slot, int count, float spread, int bounces = 0)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = count == 1 ? 0 : (-spread + 2 * spread * i / (count - 1)) * (float)Math.PI / 180;
                SpawnProjectile(p, Vec2.Rotate(facing, angle), damage, stun, range, speed, radius, slot);
                State.MutableProjectiles[State.MutableProjectiles.Count - 1].BouncesRemaining = bounces;
            }
        }

        public void Step(float dt)
        {
            foreach (var p in State.Players)
            {
                if (!p.AttackPending) continue;
                if (p.IsDead || p.Stun > 0 || p.PushRemaining > 0) { p.AttackPending = false; continue; }
                p.WeaponWindup = Math.Max(0, p.WeaponWindup - dt);
                if (p.WeaponWindup > 0.00001f) continue;
                p.AttackPending = false; p.WeaponVisual = 0.18f;
                var kit = p.Stats.Loadout;
                if (kit.Weapon == WeaponKind.ImpactHammer)
                {
                    foreach (var target in State.Players)
                    {
                        if (target.IsDead || target.Team == p.Team) continue;
                        var relative = target.Position - p.Position;
                        if (relative.Length <= kit.WeaponRange + target.Stats.Radius &&
                            TackleSystem.ContactAngle(p.AttackFacing, relative) <= kit.MeleeHalfAngle)
                            ApplyHit(p, target, kit.WeaponDamage, 0, SkillSlot.Weapon);
                    }
                }
                else SpawnVolley(p, p.AttackFacing, kit.WeaponDamage, 0, kit.WeaponRange, kit.WeaponSpeed,
                    kit.WeaponRadius, SkillSlot.Weapon, kit.WeaponShots, kit.WeaponSpreadDegrees);
            }
            for (int i = State.MutableProjectiles.Count - 1; i >= 0; i--)
            {
                var shot = State.MutableProjectiles[i];
                float distance = Math.Min(shot.Speed * dt, shot.RemainingDistance);
                var delta = shot.Direction * distance;
                float wallT = WallFraction(shot.Position, delta, shot.Radius), hitT = wallT;
                PlayerState first = null;
                foreach (var target in State.Players)
                {
                    if (target.IsDead || target.Team == shot.Team) continue;
                    if (Geometry.SweepCircle(shot.Position, delta, target.Position, target.Stats.Radius + shot.Radius, out float t) && t < hitT)
                    { first = target; hitT = t; }
                }
                shot.Position += delta * hitT;
                shot.RemainingDistance -= distance * hitT;
                if (first != null) ApplyHit(State.Players[shot.ActorId], first, shot.Damage, shot.StunSeconds, shot.Slot);
                if (first == null && wallT < 1 && shot.BouncesRemaining > 0 && shot.RemainingDistance > 0.00001f)
                {
                    float halfW = sim.Rules.FieldWidth / 2 - shot.Radius, halfH = sim.Rules.FieldHeight / 2 - shot.Radius;
                    var direction = shot.Direction;
                    if (Math.Abs(shot.Position.X) >= halfW - 0.0001f) direction.X = -direction.X;
                    if (Math.Abs(shot.Position.Y) >= halfH - 0.0001f) direction.Y = -direction.Y;
                    shot.Direction = direction; shot.BouncesRemaining--;
                    // 少量内移避免同一边界反复产生零距离接触；总射程仍由路程限制。
                    shot.Position += direction * 0.001f;
                }
                else if (first != null || wallT < 1 || shot.RemainingDistance <= 0.00001f) State.MutableProjectiles.RemoveAt(i);
            }
        }

        private float WallFraction(Vec2 start, Vec2 delta, float radius)
        {
            float result = 1;
            float halfW = sim.Rules.FieldWidth / 2 - radius, halfH = sim.Rules.FieldHeight / 2 - radius;
            if (delta.X > 0) result = Math.Min(result, (halfW - start.X) / delta.X);
            else if (delta.X < 0) result = Math.Min(result, (-halfW - start.X) / delta.X);
            if (delta.Y > 0) result = Math.Min(result, (halfH - start.Y) / delta.Y);
            else if (delta.Y < 0) result = Math.Min(result, (-halfH - start.Y) / delta.Y);
            return Geometry.Clamp(result, 0, 1);
        }

        internal void ApplyHit(PlayerState attacker, PlayerState target, float damage, float stun, SkillSlot slot)
        {
            if (target.IsDead || target.Team == attacker.Team) return;
            if (target.FullProtection > 0)
            { sim.Emit(MatchEventKind.Protected, attacker.Id, target.Id, slot: slot); return; }
            float absorbed = Math.Min(target.ShieldHealth, damage);
            target.ShieldHealth -= absorbed;
            float actual = Math.Min(target.Health, Math.Max(0, damage - absorbed));
            target.Health -= actual; attacker.DamageDealt += actual;
            if (damage > 0)
                sim.Emit(MatchEventKind.Damage, attacker.Id, target.Id, slot: slot, strength: actual, position: target.Position);
            if (target.Health <= 0)
            {
                var position = target.Position;
                if (State.Ball.OwnerId == target.Id)
                {
                    State.Ball.DroppedOwnerId = -1;
                    State.Ball.LastTouchId = target.Id;
                    State.Ball.OwnerId = State.Ball.LastKickerId = -1;
                    State.Ball.Position = State.Ball.PreviousPosition = position;
                    State.Ball.Velocity = Vec2.Zero;
                    State.Ball.Height = State.Ball.PreviousHeight = State.Ball.VerticalVelocity = 0;
                    State.Ball.FlightMode = BallFlightMode.Ground;
                    State.Ball.SelfCatchRemaining = State.Ball.CurveRemaining = 0;
                    State.Ball.PossessionContactRemaining = 0;
                }
                ResetPlayer(target);
                target.Health = target.Energy = 0;
                target.RespawnRemaining = sim.Rules.RespawnSeconds;
                target.PreviousPosition = position;
                target.Deaths++; attacker.Knockouts++;
                sim.ClearActions(target);
                State.MutableBombs.RemoveAll(b => b.ActorId == target.Id);
                sim.Emit(MatchEventKind.Knockout, attacker.Id, target.Id, position: position);
            }
            else if (stun > 0 && target.ControlProtection <= 0)
            {
                target.Stun = stun;
                target.ControlProtection = stun + sim.Rules.ControlProtection;
                target.Dash = DashKind.None; target.AttackPending = false;
                MatchSimulation.CancelCharge(target); target.PreviewingE = false;
                sim.ClearActions(target);
                sim.Emit(MatchEventKind.Stunned, attacker.Id, target.Id, slot: slot, strength: stun, position: target.Position);
            }
        }
    }
}
