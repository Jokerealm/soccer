using System;

namespace Soccer.Core
{
    public enum WeaponKind { ImpactHammer, PulseBlaster, RelayEmitter }
    public enum SupportKind { TeamShield, TeamBoost, HealingPulse }

    /// <summary>梗角色参考仍是可替换的打法配置，数值与美术可独立调整。</summary>
    [Serializable]
    public sealed class CombatLoadout
    {
        public bool HasWeapon = true;
        public WeaponKind Weapon;
        public SupportKind Support;
        public float WeaponDamage = 28;
        public float WeaponRange = 1.8f;
        public float WeaponCooldown = 0.32f;
        public float WeaponWindup = 0.06f;
        public int AmmoCapacity = 3;
        public float AmmoReloadSeconds = 1.15f;
        public int WeaponShots = 1;
        public float WeaponSpreadDegrees;
        public float WeaponSpeed = 20;
        public float WeaponRadius = 0.16f;
        public float MeleeHalfAngle = 60;
        public float QDamage = 20;
        public float QStun = 0.8f;
        public float QRange = 8;
        public float QSpeed = 12;
        public float QRadius = 0.24f;
        public int QShots = 1;
        public float QSpreadDegrees;
        public int QBounces;
        public float QEnergy = 30;
        public float EEnergy = 35;
        public float SupportRadius = 3;
        public float SupportStrength = 35;
        public float SupportDuration = 2;

        public void Validate()
        {
            float[] values = { WeaponDamage, WeaponRange, WeaponCooldown, WeaponWindup, WeaponSpeed, WeaponRadius,
                MeleeHalfAngle, QDamage, QStun, QRange, QSpeed, QRadius, QEnergy, EEnergy,
                SupportRadius, SupportStrength, SupportDuration, AmmoReloadSeconds, WeaponSpreadDegrees, QSpreadDegrees };
            foreach (float value in values)
                if (!Geometry.IsFinite(value) || value < 0) throw new ArgumentException("战斗装备包含非法数值。");
            if (!Enum.IsDefined(typeof(WeaponKind), Weapon) || !Enum.IsDefined(typeof(SupportKind), Support) ||
                WeaponDamage <= 0 || WeaponRange <= 0 || WeaponCooldown <= 0 || WeaponSpeed <= 0 ||
                WeaponRadius <= 0 || QRange <= 0 || QSpeed <= 0 || QRadius <= 0 || SupportRadius <= 0 ||
                SupportDuration <= 0 || MeleeHalfAngle > 90 || AmmoCapacity < 1 || AmmoCapacity > 6 ||
                AmmoReloadSeconds <= 0 || WeaponShots < 1 || WeaponShots > 5 || QShots < 1 || QShots > 5 ||
                QBounces < 0 || QBounces > 1 || WeaponSpreadDegrees > 60 || QSpreadDegrees > 60)
                throw new ArgumentException("战斗装备的类型、距离或时间无效。");
        }
    }

    public static class PrototypeLoadouts
    {
        public const int Version = 2;
        public static CharacterStats Create(CharacterId slot)
        {
            var stats = new CharacterStats
            {
                Id = slot, Name = slot == CharacterId.WindShuttle ? "奶龙参考" : slot == CharacterId.IronGuard ? "雨姐参考" : "良子参考",
                Speed = 5, Radius = 0.5f, KickDistance = 1, QCooldown = 5, ECooldown = 8,
                RelaySpeedBonus = 0.1f, RelayDuration = 0.8f, RelayCooldown = 6
            };
            if (slot == CharacterId.IronGuard)
                stats.Loadout = new CombatLoadout { Weapon = WeaponKind.PulseBlaster, Support = SupportKind.TeamBoost,
                    WeaponDamage = 10, WeaponRange = 4.8f, WeaponCooldown = 0.3f, WeaponWindup = 0.05f,
                    WeaponShots = 3, WeaponSpreadDegrees = 12, AmmoReloadSeconds = 1.1f,
                    QDamage = 22, QRange = 4.5f, QSpeed = 11, QRadius = 0.55f, QStun = 0.7f, SupportStrength = 0.2f };
            else if (slot == CharacterId.ArcLight)
                stats.Loadout = new CombatLoadout { Weapon = WeaponKind.RelayEmitter, Support = SupportKind.HealingPulse,
                    WeaponDamage = 18, WeaponRange = 7, WeaponCooldown = 0.26f, WeaponWindup = 0.05f,
                    WeaponSpeed = 18, AmmoReloadSeconds = 1.05f,
                    QDamage = 6, QShots = 3, QSpreadDegrees = 18, QStun = 0.55f, EEnergy = 30, SupportStrength = 30 };
            else
                stats.Loadout.QBounces = 1;
            return stats;
        }
    }
}
