using System;

namespace Soccer.Core
{
    /// <summary>设计稿建议值。唯一规则入口；修改这些值不代表已经冻结正式设计。</summary>
    [Serializable]
    public sealed class MatchRules
    {
        public string RuleVersion = "star-strikers-prototype-0.18.0";
        // 原地图速度保留在角色表；统一倍率用于校准本原型的现实时间跑位。
        public float MovementSpeedScale = 1.6f;
        public bool EnableCombat = true;
        public float RespawnSeconds = 5;
        public float RespawnProtection = 1;
        public float EnergyRegenDelay = 1.25f;
        public float FieldWidth = 56;
        public float FieldHeight = 34;
        public float GoalWidth = 4.5f;
        public float GoalDepth = 1.5f;
        public float PostRadius = 0.10f;
        public float BallRadius = 0.27f;
        public bool EnableOutOfPlay = true;
        public float SetPiecePreparation = 1;
        public float SetPieceDistance = 3;
        public float NavigationClearance = 0.04f;
        // 仅供特定越顶技能使用；常规足球规则不读取高度。
        public float SkillPlayerClearHeight = 1.25f;
        public float SkillBallCatchHeight = 0.3f;
        public float SkillBallGravity = 18;
        public float SkillBallAirDeceleration = 0.6f;
        public float SkillBallLandingSpeedRetention = 0.82f;
        public float MatchSeconds = 300;
        public float OvertimeSeconds = 90;
        public float InitialCountdown = 3;
        // 中圈美术、保护罩美术与阻挡判定共用同一个半径。
        public float CenterCircleRadius = 4.2f;
        public float OpeningShieldRadius => CenterCircleRadius;
        public float RestartCountdown = 2;
        public float GoalCelebration = 2;
        public float KickoffProtection = 1;
        public float PossessionSpeed = 0.8f;
        public float ChargeMoveSpeed = 0.8f;
        public float FootGap = 0.05f;
        public float BallDeceleration = 8;
        public float TapKickSpeed = 9;
        public float FullKickSpeed = 18;
        public float ChargeSeconds = 0.85f;
        public float MaxChargeHold = 0.5f;
        public float KickRecovery = 0.12f;
        public float KickCooldown = 6;
        public float ActionBufferSeconds = 0.12f;
        public float SelfCatchLock = 0.25f;
        // 地图 Untakable 的接球保护；只阻止自动易手，不阻止推开后落球。
        public float PossessionContactLock = 0.25f;
        public float BodyBounce = 0.45f;
        public float WallBounce = 0.75f;
        public float MaxBallSpeed = 24;
        public float TackleDistance = 1.8f;
        public float TackleWindup = 0.06f;
        public float TackleDuration = 0.15f;
        public float TackleRecovery = 0.10f;
        public float TackleCooldown = 5;
        // 表格没有扣血公式；此系数为可调原型值，消耗 = 系数 / 角色推力。
        public float TackleHealthCostFactor = 10;
        // 持球者背后左右各 45° 的接触保留球；正面／侧面推开敌人时球留在原地。
        public float RearPushCarryAngleDegrees = 45;
        public float PushDistance = 0.9f;
        public float PushDuration = 0.18f;
        public float StunSeconds = 0.18f;
        public float ControlProtection = 0.6f;
        public int PhysicsSubsteps = 4;

        public float TackleHealthCost(CharacterStats stats) => TackleHealthCostFactor / stats.PushPower;

        public void Validate()
        {
            if (!Geometry.IsFinite(MovementSpeedScale) || MovementSpeedScale <= 0 || MovementSpeedScale > 4)
                throw new ArgumentException("移动速度倍率必须在 0 到 4 之间。");
            foreach (float value in new[] { SkillPlayerClearHeight, SkillBallCatchHeight, SkillBallGravity,
                SkillBallAirDeceleration, SkillBallLandingSpeedRetention })
                if (!Geometry.IsFinite(value)) throw new ArgumentException("技能高球参数必须是有限数值。");
            if (SkillPlayerClearHeight <= 0 || SkillBallCatchHeight < 0 ||
                SkillBallCatchHeight > SkillPlayerClearHeight || SkillBallGravity <= 0 ||
                SkillBallAirDeceleration < 0 || SkillBallLandingSpeedRetention < 0 || SkillBallLandingSpeedRetention > 1)
                throw new ArgumentException("技能高球配置无效。");
            if (FieldWidth < 8 || FieldHeight < 6 || GoalWidth <= 2 * BallRadius ||
                GoalWidth >= FieldHeight || GoalDepth <= 2 * BallRadius || BallRadius <= 0 ||
                BallDeceleration <= 0 || ChargeSeconds <= 0 || TackleDuration <= 0 ||
                PushDuration <= 0 || PhysicsSubsteps < 1 || PhysicsSubsteps > 16 ||
                MatchSeconds <= 0 || OvertimeSeconds <= 0 || !Geometry.IsFinite(RearPushCarryAngleDegrees) || RearPushCarryAngleDegrees < 0 || RearPushCarryAngleDegrees > 90 ||
                ActionBufferSeconds < 0 || ActionBufferSeconds > 0.25f ||
                !Geometry.IsFinite(RespawnSeconds) || RespawnSeconds <= 0 ||
                !Geometry.IsFinite(RespawnProtection) || RespawnProtection < 0 ||
                !Geometry.IsFinite(EnergyRegenDelay) || EnergyRegenDelay < 0)
                throw new ArgumentException("比赛配置包含非法尺寸、时间或角度。请检查 PrototypeRules。");
            if (!Geometry.IsFinite(KickCooldown) || KickCooldown <= 0 ||
                !Geometry.IsFinite(TackleCooldown) || TackleCooldown <= 0 ||
                !Geometry.IsFinite(TackleHealthCostFactor) || TackleHealthCostFactor < 0 ||
                !Geometry.IsFinite(PossessionContactLock) || PossessionContactLock < 0 ||
                !Geometry.IsFinite(PossessionSpeed) || PossessionSpeed <= 0 || PossessionSpeed > 1)
                throw new ArgumentException("踢球、铲球冷却或生命消耗配置无效。");
            foreach (float value in new[] { SetPiecePreparation, SetPieceDistance, NavigationClearance, BallRadius })
                if (!Geometry.IsFinite(value) || value <= 0) throw new ArgumentException("定位球或寻路配置无效。");
            if (SetPieceDistance >= Math.Min(FieldWidth, FieldHeight) / 2 || NavigationClearance > 0.5f)
                throw new ArgumentException("定位球距离或寻路间隔过大。");
            if (!Geometry.IsFinite(OpeningShieldRadius) || OpeningShieldRadius <= BallRadius ||
                OpeningShieldRadius >= Math.Min(FieldWidth,FieldHeight)*.25f)
                throw new ArgumentException("开场足球防护罩半径无效。");
        }
    }

    public enum CharacterId { WindShuttle, IronGuard, ArcLight, Marine, Thor }

    [Serializable]
    public sealed class CharacterStats
    {
        public CharacterId Id;
        public string Name;
        public float MaxHealth = 100;
        public float HealthRegen = 2;
        public float MaxEnergy = 100;
        public float EnergyRegen = 12;
        public CombatLoadout Loadout = new CombatLoadout();
        public float Speed = 5;
        public float Radius = 0.5f;
        public float KickDistance = 1;
        public float PushResistance;
        public float PushPower = 0.5f;
        public float QHealthCost;
        public float EHealthCost;
        public StarSkillTuning ReferenceSkills = new StarSkillTuning();
        public float QCooldown = 7;
        public float ECooldown = 11;
        public float QWindup;
        public float QDuration = 0.16f;
        public float QDistance = 1.4f;
        public float EWindup = 0.15f;
        public float EDuration = 2;
        public float LaneLength = 3;
        public float LaneWidth = 0.9f;
        public float LaneSpeedBonus = 0.12f;
        public float RelaySpeedBonus = 0.10f;
        public float RelayDuration = 0.8f;
        public float RelayCooldown = 6;
        public float ShieldLength = 2.2f;
        public float ShieldBounce = 0.55f;
        public float RootResistance = 0.6f;
        public float RootSpeed = 0.8f;
        public float PreparationLifetime = 2;
        public float CurveSeconds = 0.6f;
        public float CurveDegrees = 18;
        public float CurveDeadZoneDegrees = 5;
        public float PowerChargeSeconds = 1;
        public float PowerDistance = 1.2f;
        public float PowerMoveSpeed = 0.65f;
    }
}
