using System;

namespace Soccer.Core
{
    /// <summary>固定步长的本地权威模拟。先处理取消/输入，再分步移动和接触，最后结算时钟。</summary>
    public sealed class MatchSimulation
    {
        public const float TickSeconds = 1f / 60f;
        public MatchRules Rules { get; }
        public MatchState State { get; } = new MatchState();
        private readonly NavigationSystem navigation;
        internal RestartSystem Restarts { get; }
        private readonly MovementSystem movement;
        private readonly BallSystem ball;
        private readonly SkillSystem skills;
        private readonly CombatSystem combat;
        private readonly StarStrikerSkills starSkills;
        private readonly PlayerActionBuffer[] actionBuffers;

        public MatchSimulation(MatchRules rules, CharacterStats[] roster, CharacterId selected, MatchMode mode, CharacterId[] lineup = null)
        {
            rules.Validate();
            if (roster == null || roster.Length < 2 || roster.Length > 6) throw new ArgumentException("原型需要 2 至 6 名角色的配置。");
            foreach (var stats in roster)
            {
                if (stats != null && Array.FindAll(roster, c => c != null && c.Id == stats.Id).Length != 1)
                    throw new ArgumentException("角色配置 ID 不允许重复。");
                if (stats == null || stats.Radius <= 0 || stats.Speed <= 0 || stats.KickDistance <= 0 ||
                    !Geometry.IsFinite(stats.PushPower) || stats.PushPower <= 0 || stats.PushPower > 1)
                    throw new ArgumentException("角色配置不完整。");
                if (rules.EnableCombat)
                {
                    if (!Geometry.IsFinite(stats.MaxHealth) || stats.MaxHealth <= 0 ||
                        !Geometry.IsFinite(stats.MaxEnergy) || stats.MaxEnergy <= 0 ||
                        !Geometry.IsFinite(stats.EnergyRegen) || stats.EnergyRegen < 0 ||
                        !Geometry.IsFinite(stats.HealthRegen) || stats.HealthRegen < 0 ||
                        !Geometry.IsFinite(stats.QCooldown) || stats.QCooldown <= 0 ||
                        !Geometry.IsFinite(stats.ECooldown) || stats.ECooldown <= 0 || stats.Loadout == null)
                        throw new ArgumentException("生命、能量或战斗冷却配置无效。");
                    stats.Loadout.Validate();
                    if (!Geometry.IsFinite(stats.QHealthCost) || stats.QHealthCost < 0 ||
                        !Geometry.IsFinite(stats.EHealthCost) || stats.EHealthCost < 0)
                        throw new ArgumentException("技能生命消耗无效。");
                    if (StarStrikerLoadouts.IsReference(stats.Id))
                    {
                        if (stats.ReferenceSkills == null) throw new ArgumentException("参考技能配置不完整。");
                        stats.ReferenceSkills.Validate();
                    }
                }
            }
            Rules = rules;
            State.Mode = mode;
            State.RemainingSeconds = rules.MatchSeconds;
            State.Phase = MatchPhase.Countdown;
            State.PhaseRemaining = rules.InitialCountdown;
            var local = Array.Find(roster, c => c.Id == selected);
            if (local == null) throw new ArgumentException("选中的角色不在配置中。");
            if (lineup != null)
            {
                if (mode != MatchMode.BotMatch || lineup.Length != 6) throw new ArgumentException("联机阵容需要六个球员槽位。");
                for (int i = 0; i < lineup.Length; i++)
                    AddPlayer(Array.Find(roster, c => c.Id == lineup[i]) ?? throw new ArgumentException("联机角色配置缺失。"), i < 3 ? Team.Cyan : Team.Amber);
            }
            else
            {
            AddPlayer(local, Team.Cyan);
            if (mode == MatchMode.BotMatch)
            {
                var others = Array.FindAll(roster, c => c.Id != selected);
                for (int i = 0; i < 2; i++) AddPlayer(others.Length > 0 ? others[i % others.Length] : local, Team.Cyan);
                for (int i = 0; i < 3; i++) AddPlayer(State.Players[i].Stats, Team.Amber);
            }
            else AddPlayer(Array.Find(roster, c => c.Id == CharacterId.IronGuard) ?? Array.Find(roster, c => c.Id != selected), Team.Amber);
            }
            navigation = new NavigationSystem(this);
            Restarts = new RestartSystem(this);
            movement = new MovementSystem(this);
            ball = new BallSystem(this);
            skills = new SkillSystem(this);
            combat = new CombatSystem(this);
            starSkills = new StarStrikerSkills(this);
            actionBuffers = new PlayerActionBuffer[State.Players.Count];
            for (int i = 0; i < actionBuffers.Length; i++) actionBuffers[i] = new PlayerActionBuffer();
            ResetPositions(false,true);
        }

        private void AddPlayer(CharacterStats stats, Team team)
        {
            State.MutablePlayers.Add(new PlayerState { Id = State.Players.Count, Stats = stats, Team = team,
                Health = stats.MaxHealth, Energy = stats.MaxEnergy });
        }

        public void Step(PlayerCommand[] commands)
        {
            if (commands == null || commands.Length != State.Players.Count)
                throw new ArgumentException("每个角色需要一个输入槽。");
            State.MutableEvents.Clear();
            State.Tick++;
            foreach (var p in State.Players) p.PreviousPosition = p.Position;
            State.Ball.PreviousPosition = State.Ball.Position;
            State.Ball.PreviousHeight = State.Ball.Height;
            if (State.Phase == MatchPhase.Finished) return;
            float lifeDt = State.Phase == MatchPhase.Playing && State.Mode != MatchMode.Training
                ? Math.Min(TickSeconds, State.PeriodRemainingSeconds(Rules)) : TickSeconds;
            if (Rules.EnableCombat) foreach (var p in State.Players) combat.UpdateLife(p, lifeDt);
            if (State.Phase == MatchPhase.Playing || State.IsOpeningWarmup || State.Phase == MatchPhase.SetPiece)
                commands = navigation.Prepare(commands);
            if (State.Phase == MatchPhase.SetPiece)
            {
                var walking = new PlayerCommand[commands.Length];
                foreach (var p in State.Players)
                {
                    UpdateTimers(p, TickSeconds);
                    walking[p.Id] = new PlayerCommand { Move = p.Id == State.RestartPlayerId ? Vec2.Zero : commands[p.Id].Move };
                }
                for (int i = 0; i < Rules.PhysicsSubsteps; i++) movement.Step(walking, TickSeconds / Rules.PhysicsSubsteps);
                Restarts.Step(commands);
                return;
            }
            if (State.Phase != MatchPhase.Playing)
            {
                if(State.IsOpeningWarmup)
                {
                    foreach(var p in State.Players)
                    {
                        actionBuffers[p.Id].Clear();
                        if(commands[p.Id].Aim.LengthSquared>.0001f) p.Facing=commands[p.Id].Aim.Normalized;
                    }
                    for(int i=0;i<Rules.PhysicsSubsteps;i++) movement.Step(commands,TickSeconds/Rules.PhysicsSubsteps);
                }
                State.PhaseRemaining -= TickSeconds;
                if (State.PhaseRemaining <= 0)
                {
                    if (State.Phase == MatchPhase.GoalCelebration)
                    {
                        if(State.HalfTimePending) BeginSecondHalf();
                        else
                        {
                            ResetPositions(true);
                            State.Phase = MatchPhase.Countdown;
                            State.PhaseRemaining = Rules.RestartCountdown;
                        }
                    }
                    else
                    {
                        State.Phase = MatchPhase.Playing;
                        if (State.Round > 0 && State.Owner != null)
                        {
                            State.Owner.FullProtection = Rules.KickoffProtection;
                            State.Owner.IsKickoffProtected = true;
                        }
                    }
                }
                return;
            }

            float activeDt = State.Mode == MatchMode.Training ? TickSeconds : Math.Min(TickSeconds, State.PeriodRemainingSeconds(Rules));
            foreach (var p in State.Players)
            {
                if (p.IsDead || p.RespawnedThisTick) { actionBuffers[p.Id].Clear(); continue; }
                UpdateTimers(p, activeDt);
                var input = commands[p.Id];
                bool hasBall = State.Ball.OwnerId == p.Id;
                if (!input.Cancel && input.KickPressed && hasBall && p.KickCooldown > Rules.ActionBufferSeconds)
                    Emit(MatchEventKind.ActionRejected, p.Id, slot: SkillSlot.Kick);
                if (!input.Cancel && (input.TacklePressed || input.QPressed || input.EPressed))
                {
                    bool controlled = p.Stun > 0 || p.PushRemaining > 0;
                    bool requiresBall = !Rules.EnableCombat && p.Stats.Id == CharacterId.ArcLight && !hasBall;
                    if (input.TacklePressed && (controlled || p.TackleCooldown > Rules.ActionBufferSeconds))
                        Emit(MatchEventKind.ActionRejected, p.Id);
                    if (input.QPressed && (controlled || requiresBall || p.QCooldown > Rules.ActionBufferSeconds))
                        Emit(MatchEventKind.ActionRejected, p.Id, slot: SkillSlot.Q);
                    if (input.EPressed && (controlled || requiresBall || p.ECooldown > Rules.ActionBufferSeconds))
                        Emit(MatchEventKind.ActionRejected, p.Id, slot: SkillSlot.E);
                }
                var buffer = actionBuffers[p.Id];
                var command = buffer.Prepare(input, p, Rules, hasBall, activeDt);
                if (command.Aim.LengthSquared > 0.0001f && p.Dash == DashKind.None) p.Facing = command.Aim.Normalized;
                // 被控制或右键取消必须先于同帧松键生效。
                if (command.Cancel || p.Stun > 0 || p.PushRemaining > 0)
                {
                    CancelCharge(p);
                    p.PreviewingE = false;
                    p.AttackPending = false;
                }
                if (p.Stun <= 0 && p.PushRemaining <= 0 && p.ActionLock <= 0 && p.Dash == DashKind.None)
                {
                    if (command.TacklePressed) { StartTackle(p); buffer.ConsumeTackle(); }
                    if (p.Dash == DashKind.None)
                    {
                        bool usedCombat = Rules.EnableCombat && starSkills.ProcessInput(p, command);
                        if (!usedCombat) usedCombat = Rules.EnableCombat && combat.ProcessInput(p, command);
                        if (usedCombat && p.AttackPending) buffer.ConsumeWeapon();
                        if (!Rules.EnableCombat) skills.ProcessInput(p, command);
                        if (command.QPressed) buffer.ConsumeQ();
                        if (command.EPressed) buffer.ConsumeE();
                        if (command.EReleased && !p.PreviewingE) buffer.ConsumeERelease();
                        // 侧滑开始后，射门意图仍留在短缓冲中，不能在突进中放球。
                        if (p.Dash == DashKind.None && !usedCombat)
                        {
                            ball.ProcessInput(p, command, activeDt);
                            if (command.KickPressed) buffer.ConsumeKick();
                        }
                    }
                }
            }

            float dt = activeDt / Rules.PhysicsSubsteps;
            for (int i = 0; i < Rules.PhysicsSubsteps && State.Phase == MatchPhase.Playing; i++)
            {
                skills.Step(dt);
                if (Rules.EnableCombat) starSkills.Step(dt);
                movement.Step(commands, dt);
                if (Rules.EnableCombat) combat.Step(dt);
                if (State.Phase == MatchPhase.Playing) ball.Step(dt);
            }
            State.ElapsedSeconds += activeDt;
            if (State.Mode == MatchMode.Training) return;
            State.RemainingSeconds = Math.Max(0, State.RemainingSeconds - activeDt);
            if(!State.Overtime && State.Half==1 && State.PeriodRemainingSeconds(Rules)<=.00001f)
            {
                State.RemainingSeconds=Rules.MatchSeconds*.5f;
                if(State.Phase==MatchPhase.GoalCelebration) State.HalfTimePending=true;
                else BeginSecondHalf();
            }
            else if (State.Phase == MatchPhase.GoalCelebration && State.Overtime) Finish();
            else if (State.RemainingSeconds <= 0)
            {
                if (!State.Overtime && State.CyanScore == State.AmberScore)
                {
                    State.Overtime = true;
                    State.RemainingSeconds = Rules.OvertimeSeconds;
                }
                else Finish();
            }
        }

        private void BeginSecondHalf()
        {
            State.Half=2; State.HalfTimePending=false;
            State.NextKickoff=Team.Amber;
            CancelPendingInputs(); ResetPositions(true);
            State.Phase=MatchPhase.Countdown; State.PhaseRemaining=Rules.RestartCountdown;
        }

        private static float Decay(float value, float dt) => Math.Max(0, value - dt);
        private void UpdateTimers(PlayerState p, float dt)
        {
            p.TackleCooldown = Decay(p.TackleCooldown, dt);
            p.KickCooldown = Decay(p.KickCooldown, dt);
            p.QCooldown = Decay(p.QCooldown, dt);
            p.ECooldown = Decay(p.ECooldown, dt);
            p.ActionLock = Decay(p.ActionLock, dt);
            p.FullProtection = Decay(p.FullProtection, dt);
            p.ControlProtection = Decay(p.ControlProtection, dt);
            p.Stun = Decay(p.Stun, dt);
            p.RootRemaining = Decay(p.RootRemaining, dt);
            p.RelayRemaining = Decay(p.RelayRemaining, dt);
            p.RelayCooldown = Decay(p.RelayCooldown, dt);
            p.KickVisual = Decay(p.KickVisual, dt);
            p.PreparationRemaining = Decay(p.PreparationRemaining, dt);
            if (p.PreparationRemaining <= 0 || State.Ball.OwnerId != p.Id) p.Preparation = ShotPreparation.None;
            if (p.IsKickoffProtected && (p.FullProtection <= 0 ||
                (p.Team == Team.Cyan ? p.Position.X > 0 : p.Position.X < 0))) RemoveKickoffProtection(p);
            if (p.IsRespawnProtected && p.FullProtection <= 0) p.IsRespawnProtected = false;
        }

        private void StartTackle(PlayerState p)
        {
            if (p.TackleCooldown > 0) return;
            if (Rules.EnableCombat)
            {
                float cost = Rules.TackleHealthCost(p.Stats);
                if (p.Health <= cost + 0.0001f) { Emit(MatchEventKind.ActionRejected, p.Id); return; }
                p.Health -= cost;
            }
            CancelCharge(p);
            p.PreviewingE = false;
            p.Dash = DashKind.Tackle;
            p.DashDirection = p.Facing;
            p.DashWindup = Rules.TackleWindup;
            p.DashRemaining = Rules.TackleDuration;
            p.DashSpeed = Rules.TackleDistance / Rules.TackleDuration;
            p.TackleCooldown = Rules.TackleCooldown;
            RemoveKickoffProtection(p);
            Emit(MatchEventKind.Tackle, p.Id);
        }

        internal void Emit(MatchEventKind kind, int actor = -1, int target = -1, float angle = 0,
            SkillSlot slot = SkillSlot.None, ShotPreparation shot = ShotPreparation.None, float strength = 0, Vec2 position = default)
            => State.MutableEvents.Add(new MatchEvent(kind, actor, target, angle, slot, shot, strength, position));

        internal static void CancelCharge(PlayerState p) { p.Charging = false; p.ChargeTime = 0; }
        internal static void RemoveKickoffProtection(PlayerState p)
        {
            if (!p.IsKickoffProtected && !p.IsRespawnProtected) return;
            p.FullProtection = 0;
            p.IsKickoffProtected = p.IsRespawnProtected = false;
        }

        internal void ClearActions(PlayerState p) => actionBuffers[p.Id].Clear();
        internal void ApplySkillHit(PlayerState actor, PlayerState target, float damage, float stun, SkillSlot slot)
            => combat.ApplyHit(actor, target, damage, stun, slot);

        internal bool TryAcquireBallOnContact(PlayerState p, Vec2 playerCenterAtContact)
            => TryAcquireBallOnContact(p, playerCenterAtContact, State.Ball.Position);

        internal bool TryAcquireBallOnContact(PlayerState p, Vec2 playerCenterAtContact, Vec2 ballCenterAtContact)
        {
            var b = State.Ball;
            float radius = p.Stats.Radius + Rules.BallRadius;
            if (State.Phase != MatchPhase.Playing || !ball.CanCatch(p) ||
                Vec2.Distance(playerCenterAtContact, ballCenterAtContact) > radius + .0001f) return false;
            var owner = State.Owner;
            if (owner != null && (owner.Team == p.Team || owner.FullProtection > 0 || b.PossessionContactRemaining > .000001f)) return false;
            b.Position = ballCenterAtContact;
            return AssignBall(p, true);
        }

        // 显式训练／开球准备允许分配球；比赛中的自由球与敌方持球都只能由碰撞入口接收。
        internal void GiveBallForSetup(PlayerState p) => AssignBall(p, false);

        internal void DropBallFromPush(PlayerState owner, PlayerState actor)
        {
            var b = State.Ball;
            if (b.OwnerId != owner.Id) return;
            b.OwnerId = b.LastKickerId = -1;
            b.DroppedOwnerId = b.LastTouchId = owner.Id;
            b.Velocity = Vec2.Zero;
            b.PreviousPosition = b.Position; // 保留接触时球所在的位置，不移动到推者或原持球者中心。
            b.Height = b.PreviousHeight = b.VerticalVelocity = b.CurveRemaining = b.SelfCatchRemaining = 0;
            b.PossessionContactRemaining = 0;
            b.FlightMode = BallFlightMode.Ground;
            // 自由球从本次推的接触时刻起存在；不得使用此前的行进路径远程获得它。
            foreach (var p in State.Players) p.ContactStart = p.Position;
            Emit(MatchEventKind.BallDropped, actor.Id, owner.Id, position: b.Position);
        }

        private bool AssignBall(PlayerState p, bool fromContact)
        {
            if (p.IsDead || State.Phase != MatchPhase.Playing || !Restarts.Touch(p)) return false;
            var b = State.Ball;
            if (b.LastKickerId >= 0 && b.LastKickerId != p.Id && State.Players[b.LastKickerId].Team == p.Team)
            {
                var passer = State.Players[b.LastKickerId];
                passer.Passes++;
                if (!passer.IsDead && (Rules.EnableCombat || passer.Stats.Id == CharacterId.WindShuttle) && b.SinceKick <= 1 && passer.RelayCooldown <= 0)
                {
                    passer.RelayRemaining = passer.Stats.RelayDuration;
                    passer.RelayCooldown = passer.Stats.RelayCooldown;
                }
            }
            int dispossessed = b.OwnerId >= 0 ? b.OwnerId : b.DroppedOwnerId;
            b.DroppedOwnerId = -1;
            b.LastKickerId = -1; // 每脚传球只计一次，不把后续抢断算作传球。
            b.OwnerId = p.Id;
            b.LastTouchId = p.Id;
            b.PossessionContactRemaining = Rules.PossessionContactLock;
            b.Velocity = Vec2.Zero;
            b.Height = b.PreviousHeight = b.VerticalVelocity = 0;
            b.FlightMode = BallFlightMode.Ground;
            b.CurveRemaining = 0;
            Emit(MatchEventKind.Catch, p.Id);
            if (fromContact && dispossessed >= 0 && State.Players[dispossessed].Team != p.Team)
            {
                p.Steals++;
                Emit(MatchEventKind.Steal, p.Id, dispossessed, position: b.Position);
            }
            return true;
        }

        internal void Score(Team scoringTeam)
        {
            if (State.Phase != MatchPhase.Playing || !Restarts.AllowDirectGoal(scoringTeam)) return;
            if (scoringTeam == Team.Cyan) State.CyanScore++; else State.AmberScore++;
            int last = State.Ball.OwnerId >= 0 ? State.Ball.OwnerId : State.Ball.LastTouchId;
            if (last >= 0 && State.Players[last].Team == scoringTeam) State.Players[last].Goals++;
            State.NextKickoff = scoringTeam == Team.Cyan ? Team.Amber : Team.Cyan;
            State.Ball.Velocity = Vec2.Zero;
            State.Ball.Height = State.Ball.PreviousHeight = State.Ball.VerticalVelocity = 0;
            State.Ball.FlightMode = BallFlightMode.Ground;
            State.Phase = MatchPhase.GoalCelebration;
            State.MutableBombs.Clear();
            State.PhaseRemaining = Rules.GoalCelebration;
            Emit(MatchEventKind.Goal, last, (int)scoringTeam);
        }

        public void Finish()
        {
            if (State.Phase == MatchPhase.Finished) return;
            State.Phase = MatchPhase.Finished;
            State.MutableBombs.Clear();
            State.Ball.Velocity = Vec2.Zero;
            State.Ball.Height = State.Ball.PreviousHeight = State.Ball.VerticalVelocity = 0;
            State.Ball.FlightMode = BallFlightMode.Ground;
            Emit(MatchEventKind.Finished);
        }

        /// <summary>本地暂停或输入设备失焦时取消尚未提交的动作，不推进时钟、不刷新冷却。</summary>
        public void CancelPendingInputs()
        {
            navigation.ClearAll();
            foreach (var buffer in actionBuffers) buffer.Clear();
            foreach (var p in State.Players)
            {
                CancelCharge(p);
                p.PreviewingE = false;
                p.AttackPending = false;
            }
        }

        public void CancelPlayerInputs(int playerId)
        {
            if (playerId < 0 || playerId >= State.Players.Count) return;
            var p = State.Players[playerId]; navigation.Clear(p); actionBuffers[playerId].Clear();
            CancelCharge(p); p.PreviewingE = p.AttackPending = false;
        }
        internal float MovementSpeed(PlayerState player) => movement.Speed(player);

        private void ResetPositions(bool kickoff,bool opening=false)
        {
            navigation.ClearAll();
            foreach (var buffer in actionBuffers) buffer.Clear();
            foreach (var p in State.Players)
            {
                // 进球重开不提前复活，也不移动尚在恢复中的角色。
                if (kickoff && p.IsDead) continue;
                CombatSystem.ResetPlayer(p);
                int slot = p.Team == Team.Cyan ? p.Id : p.Id - (State.Mode == MatchMode.Training ? 1 : 3);
                float side = p.Team == Team.Cyan ? -1 : 1;
                float front=opening ? Math.Max(3,Rules.CenterCircleRadius+p.Stats.Radius+.9f) : 3;
                p.Position = new Vec2(side * (slot == 0 ? front : slot == 1 ? Math.Max(8,front+2.5f) : Math.Max(12,front+5)), slot == 1 ? -4 : slot == 2 ? 3 : 0);
                p.PreviousPosition = p.Position;
                p.Facing = new Vec2(-side, 0);
                p.Velocity = Vec2.Zero;
                p.TackleCooldown = p.QCooldown = p.ECooldown = p.ActionLock = 0;
                p.FullProtection = p.ControlProtection = p.Stun = 0;
                p.PushRemaining = p.RootRemaining = p.RelayRemaining = p.RelayCooldown = 0;
                p.Dash = DashKind.None;
                p.Preparation = ShotPreparation.None;
                p.PreparationRemaining = p.KickVisual = 0;
                p.IsKickoffProtected = p.PreviewingE = false;
                CancelCharge(p);
            }
            State.MutableEffects.Clear();
            State.MutableProjectiles.Clear();
            State.MutableBombs.Clear();
            State.Restart = RestartKind.None; State.RestartPlayerId = -1;
            State.Ball.OwnerId = State.Ball.LastKickerId = State.Ball.LastTouchId = State.Ball.RestartKickerId = State.Ball.DroppedOwnerId = -1;
            State.Ball.ReleasedRestart = RestartKind.None;
            State.Ball.SelfCatchRemaining = State.Ball.CurveRemaining = 0;
            State.Ball.PossessionContactRemaining = 0;
            State.Ball.Position = State.Ball.PreviousPosition = Vec2.Zero;
            State.Ball.Height = State.Ball.PreviousHeight = State.Ball.VerticalVelocity = 0;
            State.Ball.FlightMode = BallFlightMode.Ground;
            State.Ball.Velocity = Vec2.Zero;
            if (kickoff)
            {
                State.Round++;
                int start = State.NextKickoff == Team.Cyan ? 0 : State.Mode == MatchMode.Training ? 1 : 3;
                int count = State.Mode == MatchMode.Training ? 1 : 3;
                PlayerState owner = null;
                for (int i = 0; i < count; i++)
                {
                    var candidate = State.Players[start + (State.Round - 1 + i) % count];
                    if (!candidate.IsDead) { owner = candidate; break; }
                }
                if (owner == null) return; // 全队被击倒时中点自由球，继续等待原地复活。
                owner.Position = new Vec2(owner.Team == Team.Cyan ? -0.8f : 0.8f, 0);
                owner.PreviousPosition = owner.Position;
                State.Ball.OwnerId = owner.Id;
                ball.UpdateHeldBall();
            }
        }

        /// <summary>训练工具只接受明确的训练模式。UI 不能在对练中重置球和冷却。</summary>
        public void ResetTraining(bool giveBall)
        {
            if (State.Mode != MatchMode.Training || State.Phase == MatchPhase.Finished) return;
            ResetPositions(false);
            State.Phase = MatchPhase.Playing;
            if (giveBall) { GiveBallForSetup(State.Players[0]); ball.UpdateHeldBall(); }
        }

        public void GiveTrainingTargetBall()
        {
            if (State.Mode != MatchMode.Training || State.Phase == MatchPhase.Finished) return;
            ResetTraining(false);
            State.Players[0].Position = State.Players[0].PreviousPosition = new Vec2(-1.5f, 0);
            State.Players[1].Position = State.Players[1].PreviousPosition = new Vec2(0.5f, 0);
            GiveBallForSetup(State.Players[1]);
            ball.UpdateHeldBall();
        }

        internal void ProcessRestartKick(PlayerState p, PlayerCommand command) => ball.ProcessInput(p, command, TickSeconds);

        public void SetupTrainingRestart(RestartKind kind)
        {
            if (State.Mode != MatchMode.Training || State.Phase == MatchPhase.Finished || kind == RestartKind.None) return;
            ResetTraining(false);
            var point = kind == RestartKind.Corner ? new Vec2(Rules.FieldWidth / 2 - Rules.BallRadius - .08f, Rules.FieldHeight / 2 - Rules.BallRadius - .08f)
                : kind == RestartKind.ThrowIn ? new Vec2(0, Rules.FieldHeight / 2 - Rules.BallRadius - .03f)
                : kind == RestartKind.GoalKick ? new Vec2(-Rules.FieldWidth / 2 + 3, 0) : new Vec2(Rules.FieldWidth / 2 - 9, 0);
            Restarts.Begin(kind, Team.Cyan, point);
        }

        // 供未来的 Core 技能调用；普通输入与 UI 没有高球入口。
        internal bool TryLaunchOverheadSkill(int actorId, Vec2 horizontalVelocity, float verticalSpeed)
        {
            if (State.Phase != MatchPhase.Playing || actorId < 0 || actorId >= State.Players.Count) return false;
            return ball.TryLaunchOverheadSkill(State.Players[actorId], horizontalVelocity, verticalSpeed);
        }
    }
}
