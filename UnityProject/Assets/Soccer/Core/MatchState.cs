using System;
using System.Collections.Generic;

namespace Soccer.Core
{
    public enum Team { Cyan, Amber }
    public enum MatchMode { Training, BotMatch }
    public enum MatchPhase { Countdown, Playing, GoalCelebration, Finished, SetPiece }
    public enum UnitOrderKind { None, Move, Follow, Attack, ChaseBall }
    public enum RestartKind { None, ThrowIn, Corner, GoalKick, FreeKick }
    public enum DashKind { None, Tackle, Slide, Boost }
    public enum ShotPreparation { None, Curve, Power }
    public enum BallFlightMode { Ground, SkillOverhead }
    public enum FieldEffectKind { SpeedLane, Shield }
    public enum MatchEventKind { Kick, Catch, Steal, Push, Protected, Miss, Skill, Goal, Finished, Tackle, Bounce, ActionRejected, Weapon, Damage, Stunned, Knockout, Respawn, Support, Explosion, BallDropped, OutOfPlay, Restart, RuleViolation }
    public enum SkillSlot { None, Q, E, Weapon, Kick, Special }

    public sealed class PlayerState
    {
        public int Id { get; internal set; }
        public Team Team { get; internal set; }
        public CharacterStats Stats { get; internal set; }
        public Vec2 Position { get; internal set; }
        public Vec2 PreviousPosition { get; internal set; }
        internal Vec2 ContactStart { get; set; }
        public Vec2 Facing { get; internal set; }
        public Vec2 Velocity { get; internal set; }
        public float Health { get; internal set; }
        public float Energy { get; internal set; }
        public float EnergyDelay { get; internal set; }
        public float RespawnRemaining { get; internal set; }
        public bool IsDead => Health <= 0;
        public bool RespawnedThisTick { get; internal set; }
        public float WeaponCooldown { get; internal set; }
        public float WeaponWindup { get; internal set; }
        public bool AttackPending { get; internal set; }
        public Vec2 AttackFacing { get; internal set; }
        public float WeaponVisual { get; internal set; }
        public int Ammo { get; internal set; }
        public float AmmoReloadRemaining { get; internal set; }
        public float ShieldHealth { get; internal set; }
        public float ShieldRemaining { get; internal set; }
        public float BoostRemaining { get; internal set; }
        public float BoostStrength { get; internal set; }
        public int Knockouts { get; internal set; }
        public int Deaths { get; internal set; }
        public float DamageDealt { get; internal set; }
        public float HealingDone { get; internal set; }
        public float TackleCooldown { get; internal set; }
        public float KickCooldown { get; internal set; }
        public float SkillSpeedBonus { get; internal set; }
        public float SkillSpeedRemaining { get; internal set; }
        public float OverdriveDecayTimer { get; internal set; }
        public float QCooldown { get; internal set; }
        public float ECooldown { get; internal set; }
        public float ActionLock { get; internal set; }
        public float FullProtection { get; internal set; }
        public bool IsKickoffProtected { get; internal set; }
        public bool IsRespawnProtected { get; internal set; }
        public float ControlProtection { get; internal set; }
        public float Stun { get; internal set; }
        public float PushRemaining { get; internal set; }
        public Vec2 PushVelocity { get; internal set; }
        public DashKind Dash { get; internal set; }
        public Vec2 DashDirection { get; internal set; }
        public float DashWindup { get; internal set; }
        public float DashRemaining { get; internal set; }
        public float DashSpeed { get; internal set; }
        public bool Charging { get; internal set; }
        public float ChargeTime { get; internal set; }
        public float KickVisual { get; internal set; }
        public bool PreviewingE { get; internal set; }
        public float RootRemaining { get; internal set; }
        public float RelayRemaining { get; internal set; }
        public float RelayCooldown { get; internal set; }
        public ShotPreparation Preparation { get; internal set; }
        public float PreparationRemaining { get; internal set; }
        public Vec2 PreparationFacing { get; internal set; }
        public UnitOrderKind Order { get; internal set; }
        public Vec2 OrderPosition { get; internal set; }
        public int OrderTargetId { get; internal set; } = -1;
        public int QueuedOrders { get; internal set; }
        internal readonly List<Vec2> MutablePath = new List<Vec2>();
        public IReadOnlyList<Vec2> NavigationPath => MutablePath;
        public int Goals { get; internal set; }
        public int Kicks { get; internal set; }
        public int Steals { get; internal set; }
        public int Passes { get; internal set; }
    }

    public sealed class CombatProjectile
    {
        public int ActorId { get; internal set; }
        public Team Team { get; internal set; }
        public Vec2 Position { get; internal set; }
        public Vec2 Direction { get; internal set; }
        public float Speed { get; internal set; }
        public float Radius { get; internal set; }
        public float RemainingDistance { get; internal set; }
        public float Damage { get; internal set; }
        public float StunSeconds { get; internal set; }
        public SkillSlot Slot { get; internal set; }
        public int BouncesRemaining { get; internal set; }
    }

    public sealed class RemoteBomb
    {
        public int ActorId { get; internal set; }
        public Team Team { get; internal set; }
        public Vec2 Position { get; internal set; }
        public bool AttachedToBall { get; internal set; }
        public float Remaining { get; internal set; }
        public float Radius { get; internal set; }
    }

    public sealed class BallState
    {
        public Vec2 Position { get; internal set; }
        public Vec2 PreviousPosition { get; internal set; }
        public Vec2 Velocity { get; internal set; }
        public int OwnerId { get; internal set; } = -1;
        // Height 是球底离地距离；二维 Position 仍是地面投影。
        public float Height { get; internal set; }
        public float PreviousHeight { get; internal set; }
        public float VerticalVelocity { get; internal set; }
        public BallFlightMode FlightMode { get; internal set; }
        public bool Airborne => FlightMode == BallFlightMode.SkillOverhead && (Height > 0.00001f || VerticalVelocity > 0);
        public int LastKickerId { get; internal set; } = -1;
        public int LastTouchId { get; internal set; } = -1;
        public int DroppedOwnerId { get; internal set; } = -1;
        public int RestartKickerId { get; internal set; } = -1;
        public RestartKind ReleasedRestart { get; internal set; }
        public float SelfCatchRemaining { get; internal set; }
        public float SinceKick { get; internal set; }
        public float CurveRemaining { get; internal set; }
        public float CurveRadiansPerSecond { get; internal set; }
    }

    public sealed class FieldEffect
    {
        public FieldEffectKind Kind { get; internal set; }
        public Team Team { get; internal set; }
        public Vec2 Position { get; internal set; }
        public Vec2 Direction { get; internal set; }
        public float Length { get; internal set; }
        public float Width { get; internal set; }
        public float Windup { get; internal set; }
        public float Remaining { get; internal set; }
        public float Strength { get; internal set; }
    }

    public struct MatchEvent
    {
        public MatchEventKind Kind;
        public int ActorId;
        public int TargetId;
        public float Angle;
        public SkillSlot Slot;
        public ShotPreparation Shot;
        public float Strength;
        public Vec2 Position;
        public MatchEvent(MatchEventKind kind, int actor = -1, int target = -1, float angle = 0,
            SkillSlot slot = SkillSlot.None, ShotPreparation shot = ShotPreparation.None, float strength = 0, Vec2 position = default)
        { Kind = kind; ActorId = actor; TargetId = target; Angle = angle; Slot = slot; Shot = shot; Strength = strength; Position = position; }
    }

    /// <summary>模拟器是状态的唯一写入者。UI/视图/AI 只读取；internal setter 限定写入程序集。</summary>
    public sealed class MatchState
    {
        internal readonly List<PlayerState> MutablePlayers = new List<PlayerState>();
        internal readonly List<FieldEffect> MutableEffects = new List<FieldEffect>();
        internal readonly List<MatchEvent> MutableEvents = new List<MatchEvent>();
        internal readonly List<CombatProjectile> MutableProjectiles = new List<CombatProjectile>();
        internal readonly List<RemoteBomb> MutableBombs = new List<RemoteBomb>();
        public IReadOnlyList<PlayerState> Players => MutablePlayers;
        public IReadOnlyList<FieldEffect> Effects => MutableEffects;
        public IReadOnlyList<MatchEvent> Events => MutableEvents;
        public IReadOnlyList<CombatProjectile> Projectiles => MutableProjectiles;
        public IReadOnlyList<RemoteBomb> Bombs => MutableBombs;
        public BallState Ball { get; } = new BallState();
        public MatchMode Mode { get; internal set; }
        public MatchPhase Phase { get; internal set; }
        public float PhaseRemaining { get; internal set; }
        public float RemainingSeconds { get; internal set; }
        public float ElapsedSeconds { get; internal set; }
        public bool Overtime { get; internal set; }
        public int Half { get; internal set; } = 1;
        public bool HalfTimePending { get; internal set; }
        public float PeriodRemainingSeconds(MatchRules rules) => !Overtime && Half==1
            ? Math.Max(0,RemainingSeconds-rules.MatchSeconds*.5f) : RemainingSeconds;
        public int CyanScore { get; internal set; }
        public int AmberScore { get; internal set; }
        public long Tick { get; internal set; }
        public int Round { get; internal set; }
        public bool IsOpeningWarmup => Phase == MatchPhase.Countdown && Round == 0;
        public Team NextKickoff { get; internal set; }
        public RestartKind Restart { get; internal set; }
        public Team RestartTeam { get; internal set; }
        public Vec2 RestartPosition { get; internal set; }
        public int RestartPlayerId { get; internal set; } = -1;
        public string RestartName => Restart == RestartKind.Corner ? "角球" : Restart == RestartKind.FreeKick ? "任意球"
            : Restart == RestartKind.GoalKick ? "球门球" : Restart == RestartKind.ThrowIn ? "界外球" : "";
        public PlayerState Owner => Ball.OwnerId >= 0 ? Players[Ball.OwnerId] : null;
    }
}
