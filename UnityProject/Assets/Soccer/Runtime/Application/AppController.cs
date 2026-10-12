using System;
using System.Collections;
using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    public enum AppPage { Home, CharacterSelect, Match, Results, Settings, Loading, ModeDetails, NetworkMenu, RoomLobby, Login }

    /// <summary>应用流程编排：创建对局、推进固定 Tick、暂停、结束。业务规则留在 Core。</summary>
    public sealed partial class AppController : MonoBehaviour
    {
        public MatchRulesAsset RulesAsset;
        public CharacterDefinition[] Characters;
        public Camera WorldCamera;
        public MatchView View;
        public GameUI UI;
        public AudioFeedback Audio;
        public AppPage Page { get; private set; } = AppPage.Loading;
        public bool StartupReady { get; private set; }
        public GamePlaylist SelectedPlaylist { get; private set; } = GamePlaylist.Normal;
        public ConnectionRoute Route { get; private set; } = ConnectionRoute.Internet;
        private AppPage selectionOrigin = AppPage.Home;
        public MatchSimulation Simulation { get; private set; }
        public CharacterId Selected { get; private set; } = CharacterId.Marine;
        public MatchMode SelectedMode { get; private set; } = MatchMode.Training;
        public bool Paused { get; private set; }
        public bool ShowDebug { get; private set; } = true;
        public bool MovingTrainingTarget { get; private set; }
        public float FinalGoalRemaining { get; private set; }
        public bool CelebratingFinalGoal => FinalGoalRemaining>0 && Simulation?.State.Phase==MatchPhase.Finished;
        public string Feedback { get; private set; } = "用角色碰撞体接触足球获得球权。";
        private float accumulator;
        private float feedbackRemaining;
        private KeyboardMouseInput localInput;
        public bool SelectingSkillTarget => localInput?.SelectingSkillTarget == true;
        private ICommandSource[] inputSources;
        private PlayerCommand[] commands;

        private IEnumerator Start()
        {
            Application.targetFrameRate = 120;
            foreach (var definition in Characters) definition.EnsureCombatPrototype();
            if (Array.Find(Characters, d => d.Stats.Id == Selected) == null) Selected = Characters[0].Stats.Id;
            InitializeLoginAddress();
            UI.Bind(this);
            UI.ShowPage(Page);
            Audio.SetContext(Page, false);
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--soccer-smoke") >= 0) gameObject.AddComponent<RuntimeSmokeProbe>();
            if (Array.IndexOf(args, "--soccer-input-probe") >= 0) gameObject.AddComponent<RuntimeInputProbe>();
            if (Array.IndexOf(args, "--soccer-network-smoke") >= 0) gameObject.AddComponent<RuntimeNetworkProbe>();
            float began = Time.realtimeSinceStartup;
            UI.SetLoading(0.08f, "正在准备球员与球场…");
            yield return null;
            string[] clips = { "ui-click", "ui-hover", "ui-reject", "ui-cancel", "kick", "kick-power", "catch", "tackle",
                "impact", "steal", "bounce", "countdown", "whistle", "goal", "finish", "ready", "wind-q", "wind-e",
                "iron-q", "iron-e", "arc-q", "arc-e", "charge-loop", "menu-loop", "match-loop",
                "combat-duang", "combat-pan", "combat-rice", "combat-hit", "combat-shield", "combat-ko" };
            for (int i = 0; i < clips.Length; i++)
            {
                var request = Resources.LoadAsync<AudioClip>("SoccerAudio/" + clips[i]);
                while (!request.isDone) yield return null;
                UI.SetLoading(0.15f + 0.75f * (i + 1) / clips.Length, "正在准备音乐与比赛音效…");
                yield return null;
            }
            UI.SetLoading(1, "准备就绪，欢迎来到赛场。");
            // 给封面与操作提示一个可读的展示时间；进度由真实资源加载驱动。
            while (Time.realtimeSinceStartup - began < 1.6f) yield return null;
            StartupReady = true;
            OpenLogin();
        }

        public CharacterDefinition SelectedDefinition => Array.Find(Characters, c => c.Stats.Id == Selected);
        public void SelectCharacter(CharacterId id) { Selected = id; UI.RefreshSelection(); }

        public void OpenSelection(MatchMode mode)
        {
            if (!EnsureEntered()) return;
            selectionOrigin = Page == AppPage.ModeDetails ? AppPage.ModeDetails : AppPage.Home;
            SelectedMode = mode;
            Page = AppPage.CharacterSelect;
            UI.ShowPage(Page);
            Audio.SetContext(Page, false);
        }

        public void OpenMode(GamePlaylist mode)
        {
            if (!EnsureEntered()) return;
            SelectedPlaylist = mode;
            if (EntryMode == GameEntryMode.Offline && GameModeCatalog.SupportsLAN(mode)) Route = ConnectionRoute.LAN;
            if (!GameModeCatalog.SupportsLAN(mode)) Route = ConnectionRoute.Internet;
            Page = AppPage.ModeDetails;
            UI.ShowPage(Page); Audio.SetContext(Page, false);
        }

        public void SelectConnection(ConnectionRoute route)
        {
            if (route == ConnectionRoute.Internet && EntryMode != GameEntryMode.Online) return;
            if (route == ConnectionRoute.LAN && !GameModeCatalog.SupportsLAN(SelectedPlaylist)) return;
            Route = route; UI.RefreshModeDetails();
        }

        public void BackFromSelection()
        {
            if (selectionOrigin == AppPage.ModeDetails) OpenMode(SelectedPlaylist);
            else GoHome();
        }

        public void StartMatch()
        {
            if (!EnsureEntered()) return;
            if (Room != null) { if (Room.IsHost) Room.ReturnToLobby(); return; }
            LocalPlayerId = 0; View.LocalPlayerId = Audio.LocalPlayerId = 0;
            var roster = Array.ConvertAll(Characters, c => c.CreateStats());
            Simulation = new MatchSimulation(RulesAsset.CreateRules(), roster, Selected, SelectedMode);
            localInput = new KeyboardMouseInput(WorldCamera);
            inputSources = new ICommandSource[Simulation.State.Players.Count];
            commands = new PlayerCommand[inputSources.Length];
            inputSources[0] = localInput;
            for (int i = 1; i < inputSources.Length; i++) inputSources[i] = new BotCommandSource(Simulation.Rules);
            View.Bind(Simulation.State, Characters);
            accumulator = 0;
            FinalGoalRemaining=0;
            Paused = false;
            Feedback = SelectedMode == MatchMode.Training ? "右键移动／追击 · Shift 追加 · S 停止 · V 键踢球 · 空格推抢 · Q/E 技能" : "传球、控制、保护队友，合力突破；进球决定胜负。除你之外均为 AI。";
            Page = AppPage.Match;
            UI.ShowPage(Page);
            WorldCamera.GetComponent<ArenaCamera>().ResetFraming();
            Audio.ResetMatch();
            Audio.SetContext(Page, false);
            Audio.Observe(Simulation.State, Simulation.Rules);
        }

        private void Update()
        {
            PumpNetworking();
            if (Page != AppPage.Match && Page != AppPage.Loading && Page != AppPage.Login && Input.GetKeyDown(KeyCode.Escape))
            {
                Audio.PlayClick();
                if (Page == AppPage.CharacterSelect) BackFromSelection();
                else if (Page == AppPage.NetworkMenu || Page == AppPage.RoomLobby) BackFromNetwork(); else GoHome();
            }
            if (Page != AppPage.Match || Simulation == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Audio.PlayClick(); SetPaused(!Paused); }
            if (IsNetworkMatch) { UpdateNetworkMatch(); return; }
            if (Paused) { View.Render(Simulation.State, Simulation.Rules, 1, ShowDebug, false); return; }
            var state = Simulation.State;
            if(state.Phase==MatchPhase.Finished)
            {
                // 得分规则已结束；仅保留最后一球的表现，不再推进模拟 Tick。
                if(CelebratingFinalGoal)
                {
                    FinalGoalRemaining=Mathf.Max(0,FinalGoalRemaining-Mathf.Min(Time.unscaledDeltaTime,.1f));
                    View.Render(state,Simulation.Rules,1,ShowDebug,true);
                    UI.RefreshMatch();
                }
                if(FinalGoalRemaining<=0) ShowResults();
                return;
            }
            if (state.Mode == MatchMode.Training && !state.IsOpeningWarmup)
            {
                if (Input.GetKeyDown(KeyCode.R)) { Audio.PlayClick(); ResetTraining(false); }
                if (Input.GetKeyDown(KeyCode.F)) { Audio.PlayClick(); ResetTraining(true); }
                if (Input.GetKeyDown(KeyCode.T)) { Audio.PlayClick(); GiveTargetBall(); }
                if (Input.GetKeyDown(KeyCode.F2)) SetupTrainingRestart(RestartKind.Corner);
                if (Input.GetKeyDown(KeyCode.F3)) SetupTrainingRestart(RestartKind.FreeKick);
                if (Input.GetKeyDown(KeyCode.F1)) { Audio.PlayClick(); ToggleDebug(); }
            }
            if (state.Phase == MatchPhase.Playing || state.IsOpeningWarmup || state.Phase == MatchPhase.SetPiece)
                localInput.Sample(state, Simulation.Rules, UI.IsPointerOverControl(),state.IsOpeningWarmup);
            else localInput.Reset();
            accumulator += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            while (accumulator >= MatchSimulation.TickSeconds && Page == AppPage.Match)
            {
                for (int i = 0; i < commands.Length; i++)
                    commands[i] = state.Mode == MatchMode.Training && i > 0
                        ? TrainingTargetCommand(state.Players[i]) : inputSources[i].ReadCommand(state, i);
                if (commands[0].Cancel && (state.Players[0].Charging || state.Players[0].PreviewingE)) Audio.PlayCancel();
                Simulation.Step(commands);
                accumulator -= MatchSimulation.TickSeconds;
                foreach (var e in state.Events) HandleEvent(e);
                if (state.Phase == MatchPhase.Finished)
                {if(!CelebratingFinalGoal) ShowResults();break;}
            }
            feedbackRemaining -= Time.unscaledDeltaTime;
            if (feedbackRemaining <= 0 && state.Phase == MatchPhase.Playing)
                Feedback = state.Players[0].IsDead ? $"被击倒 · {state.Players[0].RespawnRemaining:0.0} 秒后原地恢复" :
                    state.Owner == null ? "右键足球追球 · 右键敌人追击 · 空格推抢 · Q/E 技能" : state.Ball.OwnerId == 0
                    ? "你在持球 · V 键传射，C 取消 · 留意血量，及时传给队友" : state.Owner.Team==state.Players[0].Team
                    ? "队友持球 · 跑位接应，为传球创造空间" : "敌方持球 · 右键足球追球，接触脚前球即可夺球";
            if (Page == AppPage.Match)
            {
                Audio.Observe(state, Simulation.Rules);
                View.Render(state, Simulation.Rules,state.Phase==MatchPhase.Finished ? 1 : accumulator / MatchSimulation.TickSeconds, ShowDebug, true, localInput.PreviewAim, localInput.SelectingSkillTarget);
                UI.RefreshMatch();
            }
        }

        public void SetupTrainingRestart(RestartKind kind)
        {
            if (Simulation?.State.Mode != MatchMode.Training) return;
            localInput.Reset(); foreach (var source in inputSources) source.Reset();
            Simulation.SetupTrainingRestart(kind); accumulator = 0;
            WorldCamera.GetComponent<ArenaCamera>().ResetFraming();
            View.ResetFeedback(); UI.Motion.Reset(Simulation.State.Players[LocalPlayerId]);
            Feedback = $"{Simulation.State.RestartName}练习 · V 键蓄力并踢出 · 右键走位 · S 停止";
            feedbackRemaining = 3; UI.RefreshMatch();
        }

        private PlayerCommand TrainingTargetCommand(PlayerState target)
        {
            if (Simulation.State.Phase == MatchPhase.SetPiece) return inputSources[target.Id].ReadCommand(Simulation.State, target.Id);
            float laneDirection = Mathf.Sin(Simulation.State.ElapsedSeconds * 0.7f);
            return new PlayerCommand
            {
                Move = MovingTrainingTarget ? new Vec2(0, Math.Sign(laneDirection)) * 0.35f : Vec2.Zero,
                Aim = target.Facing
            };
        }

        internal void HandleEvent(MatchEvent e)
        {
            if(e.Kind==MatchEventKind.Restart) WorldCamera.GetComponent<ArenaCamera>().ResetFraming();
            if(e.Kind==MatchEventKind.Goal && Simulation.State.Phase==MatchPhase.Finished) FinalGoalRemaining=2;
            Audio.PlayEvent(e, Simulation.State);
            View.PlayEvent(e, Simulation.State);
            UI.PlayEvent(e);
            if (e.ActorId != LocalPlayerId && e.TargetId != LocalPlayerId && e.Kind != MatchEventKind.Goal && e.Kind != MatchEventKind.Restart && e.Kind != MatchEventKind.OutOfPlay) return;
            switch (e.Kind)
            {
                case MatchEventKind.OutOfPlay: Feedback = "整个足球越过边线 · 出界"; break;
                case MatchEventKind.Restart: Feedback = $"{Simulation.State.RestartName} · {(Simulation.State.RestartTeam == Team.Cyan ? "青队" : "橙队")}主罚 · V 键蓄力踢出"; break;
                case MatchEventKind.RuleViolation: Feedback = "定位球后重复触球 · 对方任意球"; break;
                case MatchEventKind.Steal: Feedback = "碰撞夺球 · 获得球权"; break;
                case MatchEventKind.BallDropped: Feedback = "敌人被推开 · 球留在原地，接触球获得球权"; break;
                case MatchEventKind.Push: Feedback = Simulation.State.Ball.OwnerId == e.TargetId ? "推开角色 · 球跟随持球者位移" : "推开角色腾出空间 · 接触足球才能接球"; break;
                case MatchEventKind.Protected: Feedback = "保护中 · 本次动作已消耗"; break;
                case MatchEventKind.Miss: Feedback = "未碰到目标 · 需要实际碰撞才能推开角色"; break;
                case MatchEventKind.Skill: Feedback = "技能已施放 · 查看下方冷却"; break;
                case MatchEventKind.Damage: Feedback = e.TargetId == LocalPlayerId ? "被击中 · 注意走位" : "武器命中"; break;
                case MatchEventKind.Stunned: Feedback = e.TargetId == LocalPlayerId ? "被震晕 · 等待控制结束" : "震晕命中 · 趁机配合突破"; break;
                case MatchEventKind.Knockout: Feedback = e.TargetId == LocalPlayerId ? "被击倒 · 原地等待 5 秒复活" : "击倒对手 · 传球推进！"; break;
                case MatchEventKind.Respawn: Feedback = "原地复活 · 生命、能量和冷却已恢复；1 秒保护，主动行动结束保护"; break;
                case MatchEventKind.Support: Feedback = "团队支援生效 · 靠近队友形成配合"; break;
                case MatchEventKind.Explosion: Feedback = "炸弹引爆 · 击退并眩晕范围内敌人"; break;
                case MatchEventKind.ActionRejected:
                    var p = Simulation.State.Players[LocalPlayerId];
                    float cost = e.Slot == SkillSlot.Q ? p.Stats.Loadout.QEnergy : e.Slot == SkillSlot.Special ? 10 : p.Stats.Loadout.EEnergy;
                    float healthCost = e.Slot == SkillSlot.Q ? p.Stats.QHealthCost : e.Slot == SkillSlot.E ? p.Stats.EHealthCost :
                        e.Slot == SkillSlot.None ? Simulation.Rules.TackleHealthCost(p.Stats) : 0;
                    Feedback = p.Stun > 0 || p.PushRemaining > 0 ? "受控中 · 稍后再施放" :
                        p.Health <= healthCost ? "生命不足 · 无法支付本次动作消耗" :
                        (e.Slot == SkillSlot.Q || e.Slot == SkillSlot.E || e.Slot == SkillSlot.Special) && p.Energy < cost
                            ? "能量不足 · 稍等恢复，或右键敌人攻击" : e.Slot == SkillSlot.Special ? "没有可引爆的炸弹" : "动作冷却中 · 等待下方图标亮起";
                    break;
                case MatchEventKind.Goal: Feedback = e.TargetId == (int)Team.Cyan ? "青队进球！" : "橙队进球！"; break;
                default: return;
            }
            feedbackRemaining = 2.5f;
        }

        public void SetPaused(bool paused)
        {
            Paused = paused;
            accumulator = 0;
            localInput?.Reset();
            if (IsNetworkMatch)
            {
                if (Room.IsHost) Simulation.CancelPlayerInputs(LocalPlayerId);
                else Room.SendInput(new PlayerCommand { Cancel = true, StopPressed = true });
            }
            else
            {
                Simulation?.CancelPendingInputs();
                foreach (var source in inputSources ?? Array.Empty<ICommandSource>()) source.Reset();
            }
            UI.ShowPause(paused);
            Audio.SetContext(Page, paused);
        }
        private void OnApplicationFocus(bool focus) { if (!focus && Page == AppPage.Match) SetPaused(true); }
        public void ToggleDebug() { ShowDebug = !ShowDebug; UI.RefreshMatch(); }
        public void ToggleTrainingTarget() { MovingTrainingTarget = !MovingTrainingTarget; UI.RefreshMatch(); }
        public void ResetTraining(bool giveBall)
        {
            if(Simulation==null || Simulation.State.IsOpeningWarmup) return;
            Simulation?.ResetTraining(giveBall);
            WorldCamera.GetComponent<ArenaCamera>().ResetFraming();
            localInput?.Reset();
            accumulator = 0;
            Audio.ResetMatch(); View.ResetFeedback(); UI.Motion.Reset(Simulation.State.Players[LocalPlayerId]);
            View.GetComponent<PremiumArenaView>().Dynamics?.ResetMatch();
            Feedback = giveBall ? "足球已交给你 · 冷却已重置" : "球场与冷却已重置";
            feedbackRemaining = 2;
        }
        public void GiveTargetBall()
        {
            if(Simulation==null || Simulation.State.IsOpeningWarmup) return;
            Simulation?.GiveTrainingTargetBall();
            WorldCamera.GetComponent<ArenaCamera>().ResetFraming();
            localInput?.Reset();
            accumulator = 0;
            Audio.ResetMatch(); View.ResetFeedback(); UI.Motion.Reset(Simulation.State.Players[LocalPlayerId]);
            View.GetComponent<PremiumArenaView>().Dynamics?.ResetMatch();
            Feedback = "目标持球 · 右键足球追球，接触脚前球即可夺球 · 空格推开角色会扣生命";
            feedbackRemaining = 3;
        }
        public void EndSession() { Simulation?.Finish(); ShowResults(); }
        private void ShowResults()
        { FinalGoalRemaining=0;Paused = false; Page = AppPage.Results; UI.ShowPage(Page); Audio.SetContext(Page, false); Audio.PlayFinish(); }
        public void GoHome()
        {
            if (!EnsureEntered()) return;
            CloseNetworking(); LocalPlayerId = 0;
            Page = AppPage.Home; Paused = false; Simulation = null;
            FinalGoalRemaining=0;
            localInput?.Reset(); View.Clear(); UI.ShowPage(Page);
            Audio.SetContext(Page, false);
        }
        public void OpenSettings() { Page = AppPage.Settings; UI.ShowPage(Page); Audio.SetContext(Page, false); }
    }
}
