using System;

namespace Soccer.Core
{
    [Serializable]
    public sealed class StarSkillTuning
    {
        public float MarineStimSpeed = 0.65f;
        public float MarineStimDuration = 5;
        public float BombFuse = 3;
        public float BombRadius = 2.5f;
        public float BombPushDistance = 2;
        public float BombStun = 1.75f;
        public float ThorDriveSpeed = 2.4f;
        public float ThorDriveDuration = 5; // 原表未列时长，本轮原型值。
        public float ThorDecayInterval = 0.375f;
        public float ThorDecayRetention = 0.9f;
        public float ThorDashDistance = 2.4f; // 原表仅描述“一段距离”。
        public float ThorDashDuration = 0.16f;

        public void Validate()
        {
            foreach (float value in new[] { MarineStimSpeed, MarineStimDuration, BombFuse, BombRadius,
                BombPushDistance, BombStun, ThorDriveSpeed, ThorDriveDuration, ThorDecayInterval,
                ThorDecayRetention, ThorDashDistance, ThorDashDuration })
                if (!Geometry.IsFinite(value) || value <= 0) throw new ArgumentException("星际参考技能参数无效。");
            if (ThorDecayRetention > 1) throw new ArgumentException("超级驱动衰减比例不能大于 1。");
        }
    }

    /// <summary>来源：用户提供的星际足球表格 Sheet1 第 5、12 行；普通武器为既有乱斗玩法扩展。</summary>
    public static class StarStrikerLoadouts
    {
        public const int Version = 2;
        public static bool IsReference(CharacterId id) => id == CharacterId.Marine || id == CharacterId.Thor;
        public static CharacterStats[] CreateRoster() => new[] { Create(CharacterId.Marine), Create(CharacterId.Thor) };
        public static CharacterStats Create(CharacterId id)
        {
            if (!IsReference(id)) throw new ArgumentException("未实现该星际参考角色。");
            bool marine = id == CharacterId.Marine;
            return new CharacterStats
            {
                Id = id, Name = marine ? "枪兵" : "雷神", MaxHealth = 100, MaxEnergy = 100,
                Speed = marine ? 2.35f : 2, Radius = marine ? 0.5f : 0.8f,
                PushPower = marine ? 0.62f : 0.75f, KickDistance = 1,
                QCooldown = marine ? 6 : 8, ECooldown = marine ? 8 : 10,
                QHealthCost = 10, EHealthCost = marine ? 0 : 10,
                Loadout = new CombatLoadout
                {
                    Weapon = WeaponKind.PulseBlaster, WeaponDamage = marine ? 12 : 14,
                    WeaponRange = marine ? 6 : 4.5f, WeaponCooldown = marine ? 0.24f : 0.4f,
                    WeaponWindup = marine ? 0.05f : 0.07f, WeaponShots = marine ? 1 : 2,
                    WeaponSpreadDegrees = marine ? 0 : 8, AmmoReloadSeconds = marine ? 1.1f : 1.4f,
                    QEnergy = marine ? 12 : 20, EEnergy = marine ? 40 : 18,
                    QDamage = 0, QStun = 0, SupportStrength = 0
                }
            };
        }
    }
}
