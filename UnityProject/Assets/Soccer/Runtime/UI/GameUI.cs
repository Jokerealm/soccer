using System;
using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    /// <summary>UXML 负责层级，USS 负责样式，此文件只绑定按钮和刷新展示字段。</summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(200)]
    public sealed class GameUI : MonoBehaviour
    {
        public StyleSheet Styles;
        private VisualElement root;
        private AppController app;
        private readonly string[] pageNames = { "home-page", "select-page", "match-page", "results-page", "settings-page", "loading-page", "mode-details-page", "network-page", "room-page", "login-page" };
        private NetworkUI network;
        private LoginUI login;
        private readonly string[] scoreFields = { "stat-goals", "stat-steals", "stat-kicks", "stat-passes" };
        public VisualElement Root => root;
        private CharacterId? hudCharacter;
        public HudMotion Motion { get; private set; }
        private SkillTooltip skillHelp;
        private BallLocator ballLocator;
        public MatchMiniMap MiniMap { get; private set; }
        public bool BallLocatorVisible => ballLocator!=null && ballLocator.Visible;
        public bool SkillHelpVisible => skillHelp!=null && skillHelp.Visible;
        public const string FullHudPreferenceKey = "soccer.fullHud";
        public bool FullHudEnabled { get; private set; }
        public static bool LoadFullHudPreference() => PlayerPrefs.GetInt(FullHudPreferenceKey, 1) != 0;

        public void Bind(AppController controller)
        {
            app = controller;
            root = GetComponent<UIDocument>().rootVisualElement;
            root.focusable = true;
            root.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.target != root) return;
                bool compact = e.newRect.height < 840, narrow = e.newRect.width < 1400;
                // Apply responsive styles after the layout pass instead of invalidating it recursively.
                root.schedule.Execute(() =>
                {
                    if (root.ClassListContains("compact-front-end") != compact) root.EnableInClassList("compact-front-end", compact);
                    if (root.ClassListContains("narrow-front-end") != narrow) root.EnableInClassList("narrow-front-end", narrow);
                });
            });
            if (Styles != null) root.styleSheets.Add(Styles);
            BindButton("train-button", () => app.OpenSelection(MatchMode.Training));
            BindButton("bots-button", () => app.OpenSelection(MatchMode.BotMatch));
            BindButton("roster-button", () => app.OpenSelection(MatchMode.Training));
            BindButton("settings-button", app.OpenSettings);
            BindButton("select-back", app.BackFromSelection);
            BindButton("normal-mode", () => app.OpenMode(GamePlaylist.Normal));
            BindButton("ranked-mode", () => app.OpenMode(GamePlaylist.Ranked));
            BindButton("party-mode", () => app.OpenMode(GamePlaylist.Entertainment));
            BindButton("mode-back", app.GoHome);
            BindButton("internet-route", () => app.SelectConnection(ConnectionRoute.Internet));
            BindButton("lan-route", () => app.SelectConnection(ConnectionRoute.LAN));
            BindButton("online-play", () => app.OpenNetwork(true));
            BindButton("join-room", () => app.OpenNetwork(false));
            BindButton("mode-practice", () => app.OpenSelection(MatchMode.BotMatch));
            BindButton("mode-training", () => app.OpenSelection(MatchMode.Training));
            BindButton("settings-back", app.GoHome);
            BindButton("start-button", app.StartMatch);
            BindButton("pause-button", () => app.SetPaused(true));
            BindButton("hud-mode-toggle", () => SetFullHud(!FullHudEnabled));
            BindButton("hud-mode-setting", () => SetFullHud(!FullHudEnabled));
            BindButton("resume-button", () => app.SetPaused(false));
            BindButton("restart-button", app.StartMatch);
            BindButton("pause-home", app.GoHome);
            BindButton("results-home", app.GoHome);
            BindButton("play-again", app.StartMatch);
            BindButton("reset-ball", () => app.ResetTraining(false));
            BindButton("give-ball", () => app.ResetTraining(true));
            BindButton("target-ball", app.GiveTargetBall);
            BindButton("training-corner", () => app.SetupTrainingRestart(RestartKind.Corner));
            BindButton("training-free-kick", () => app.SetupTrainingRestart(RestartKind.FreeKick));
            BindButton("debug-toggle", app.ToggleDebug);
            BindButton("moving-target", app.ToggleTrainingTarget);
            BindButton("end-training", app.EndSession);
            for (int i = 0; i < app.Characters.Length; i++)
            {
                var character = app.Characters[i];
                BindButton($"character-{i}", () => app.SelectCharacter(character.Stats.Id));
                SetImage($"equipment-{i}", character.Stats.Id == CharacterId.Marine ? "equipment-marine" : "equipment-thor");
            }
            string[] modeNames = { "normal", "ranked", "party" };
            for (int i = 0; i < modeNames.Length; i++)
            {
                SetImage(modeNames[i] + "-icon", "badge-" + modeNames[i]);
            }
            SetImage("brand-mark", "brand-mark");
            string[] symbols = { "weapon", "tackle", "kick", "g" };
            string[] assets = { "rifle", "tackle", "kick", "trigger" };
            for (int i=0;i<symbols.Length;i++) SetImage(symbols[i]+"-symbol", "skill-"+assets[i]+"-v2");
            var splash = Resources.Load<Texture2D>("FrontEnd/lobby-v3");
            if (splash != null)
            {
                root.Q("loading-art").style.backgroundImage = new StyleBackground(splash);
                root.Q("menu-backdrop").style.backgroundImage = new StyleBackground(splash);
            }
            var volume = root.Q<Slider>("volume-slider");
            volume.SetValueWithoutNotify(app.Audio.SavedMasterVolume);
            volume.RegisterValueChangedCallback(e => app.Audio.SetVolume(e.newValue));
            var music = root.Q<Slider>("music-slider");
            music.SetValueWithoutNotify(app.Audio.MusicVolume);
            music.RegisterValueChangedCallback(e => app.Audio.SetMusicVolume(e.newValue));
            var effects = root.Q<Slider>("effects-slider");
            effects.SetValueWithoutNotify(app.Audio.EffectsVolume);
            effects.RegisterValueChangedCallback(e => app.Audio.SetEffectsVolume(e.newValue));
            BindButton("mute-toggle", () => { app.Audio.SetMuted(!app.Audio.Muted); RefreshAudioSettings(); });
            BindButton("feedback-toggle", () => { app.View.SetFeedbackEnabled(!app.View.FeedbackEnabled); RefreshAudioSettings(); });
            Motion = new HudMotion(root);
            for (int i=0;i<symbols.Length;i++) SetImage(symbols[i]+"-symbol", "skill-"+assets[i]+"-v2");
            skillHelp=new SkillTooltip(root,DescribeSkill);
            ballLocator=new BallLocator(root);
            MiniMap=new MatchMiniMap(root);
            login=new LoginUI(app,root);
            BindButton("home-account", app.OpenLogin);
            network=new NetworkUI(app,root);
            SetFullHud(LoadFullHudPreference(), false);
            RefreshAudioSettings();
            RefreshSelection();
        }

        public void SetLoading(float progress, string status)
        {
            root.Q("loading-fill").style.width = Length.Percent(Mathf.Clamp01(progress) * 100);
            Text("loading-percent", $"{Mathf.RoundToInt(progress * 100):00}%");
            Text("loading-status", status);
        }

        public void RefreshLogin() => login?.Refresh();

        public void RefreshModeDetails()
        {
            var mode = GameModeCatalog.Get(app.SelectedPlaylist);
            Text("detail-name", mode.Name); Text("detail-english", mode.EnglishName);
            Text("detail-subtitle", mode.Subtitle); Text("detail-description", mode.Description);
            Text("detail-rule-one", mode.RuleOne); Text("detail-rule-two", mode.RuleTwo); Text("detail-rule-three", mode.RuleThree);
            string[] names = { "normal", "ranked", "party" };
            for (int i = 0; i < names.Length; i++) root.Q("mode-details-page").EnableInClassList(names[i], i == (int)app.SelectedPlaylist);
            SetImage("detail-art", "badge-" + names[(int)app.SelectedPlaylist]);
            root.Q<Button>("internet-route").SetEnabled(app.EntryMode == GameEntryMode.Online);
            root.Q<Button>("lan-route").SetEnabled(GameModeCatalog.SupportsLAN(app.SelectedPlaylist));
            root.Q("internet-route").EnableInClassList("route-selected", app.Route == ConnectionRoute.Internet);
            root.Q("lan-route").EnableInClassList("route-selected", app.Route == ConnectionRoute.LAN);
            Text("connection-heading", app.Route == ConnectionRoute.Internet ? "互联网对战" : "局域网房间");
            Text("connection-copy", app.Route == ConnectionRoute.Internet
                ? "创建公网直连房间，自选是否启用密码；朋友使用房主地址加入。"
                : "同一网络自动发现房间，也支持输入房主地址加入。最多 6 人，空位由 AI 补齐。");
            bool networkAvailable=app.SelectedPlaylist==GamePlaylist.Normal;
            root.Q<Button>("online-play").text = networkAvailable ? "创建房间" : "联机规则 · 待开放";
            root.Q<Button>("join-room").text = "加入房间";
            root.Q<Button>("online-play").SetEnabled(networkAvailable);
            root.Q<Button>("join-room").SetEnabled(networkAvailable);
            Visible(root.Q("join-room"), true);
            Visible(root.Q("mode-practice"), app.SelectedPlaylist == GamePlaylist.Normal);
            Text("mode-availability", app.SelectedPlaylist == GamePlaylist.Normal ? "可玩：联机房间 / 本地 AI 对练" : "玩法尚未开放 · 可以先进入训练场");
        }

        private void BindButton(string name, Action action)
        {
            var button = root.Q<Button>(name);
            if (button == null) throw new InvalidOperationException($"UXML 缺少按钮 {name}");
            button.RegisterCallback<PointerEnterEvent>(_ => app.Audio.PlayHover());
            button.RegisterCallback<FocusInEvent>(_ => app.Audio.PlayHover());
            button.clicked += () =>
            {
                app.Audio.PlayClick();
                action();
                // 训练工具点击后也归还焦点，空格始终用于球场抢断。
                if (app.Page == AppPage.Match && !app.Paused) root.schedule.Execute(() => root.Focus());
            };
        }

        private static void Visible(VisualElement element, bool show) => element.EnableInClassList("hidden", !show);
        public void RefreshNetwork() => network?.Refresh();
        public void SetFullHud(bool enabled, bool save = true)
        {
            skillHelp?.Hide();
            FullHudEnabled = enabled;
            if (save)
            {
                PlayerPrefs.SetInt(FullHudPreferenceKey, enabled ? 1 : 0);
                PlayerPrefs.Save();
            }
            Visible(root.Q("battle-dock"), enabled);
            Visible(root.Q("compact-player-hud"), !enabled);
            Visible(root.Q("mode-label"), enabled);
            Visible(root.Q("feedback-label"), enabled);
            Visible(root.Q("combat-toast"), enabled);
            root.Q<Button>("hud-mode-toggle").text = enabled ? "精简界面" : "完整界面";
            root.Q<Button>("hud-mode-toggle").tooltip = enabled ? "隐藏底部技能栏，只显示左上角头像与生命、能量" : "显示技能、冷却、资源条与操作提示";
            root.Q<Button>("hud-mode-setting").text = $"完整技能栏：{(enabled ? "开" : "关")}";
            Motion?.SetFullHud(enabled);
            if (app.Page == AppPage.Match && app.Simulation != null) RefreshMatch();
        }
        private void SetImage(string element, string asset)
        {
            var image = Resources.Load<Texture2D>("FrontEnd/" + asset);
            if (image != null) root.Q(element).style.backgroundImage = new StyleBackground(image);
            if (element.EndsWith("-symbol") && element != "hero-symbol" && element != "compact-hero-symbol")
                Motion?.BindIcon(element.Replace("-symbol", ""), image, Resources.Load<Texture2D>("FrontEnd/" + asset + "-gray"));
        }
        private void Text(string name, string text) => root.Q<Label>(name).text = text;
        private void RefreshAudioSettings()
        {
            Motion?.SetEnabled(app.View.FeedbackEnabled);
            root.Q<Button>("mute-toggle").text = $"静音：{(app.Audio.Muted ? "开" : "关")}";
            root.Q<Button>("feedback-toggle").text = $"命中反馈：{(app.View.FeedbackEnabled ? "开" : "关")}";
        }

        public void ShowPage(AppPage page)
        {
            ballLocator?.Hide();
            MiniMap?.Hide();
            for (int i = 0; i < pageNames.Length; i++) Visible(root.Q(pageNames[i]), i == (int)page);
            ShowPause(false);
            RefreshLogin();
            if (page == AppPage.CharacterSelect) RefreshSelection();
            if (page == AppPage.ModeDetails) RefreshModeDetails();
            if (page == AppPage.NetworkMenu || page == AppPage.RoomLobby) RefreshNetwork();
            Visible(root.Q("menu-backdrop"), page != AppPage.Match && page != AppPage.Loading);
            if (page == AppPage.Home) root.schedule.Execute(() => root.Q<Button>("normal-mode").Focus());
            if (page == AppPage.Match)
            {
                RefreshMatch();
                // 清除选角按钮焦点，避免抢断的空格触发按钮。
                root.schedule.Execute(() => root.Focus());
            }
            if (page == AppPage.Results) RefreshResults();
            Motion?.Enter(root.Q(pageNames[(int)page]), page == AppPage.Match);
        }

        public void RefreshSelection()
        {
            var d = app.SelectedDefinition;
            Text("select-title", app.SelectedMode == MatchMode.Training ? "选择角色 · 训练场" : "选择角色 · 合作乱斗");
            Text("select-subtitle", app.SelectedMode == MatchMode.Training ? "枪兵与雷神先上场，试试炸弹控制与高速抢位。" : "你与两名 AI 配合对战三名 AI；两种角色允许重复选择。");
            Text("selected-name", d.Stats.Name);
            Text("selected-role", d.Role);
            Text("selected-stats", $"生命 100  ·  能量 100\n移动速度 {d.Stats.Speed * app.RulesAsset.Rules.MovementSpeedScale:0.##}  ·  推力 {d.Stats.PushPower:0.##}\nV 键踢球 6 秒冷却  ·  空格铲球 5 秒冷却\n铲球消耗 {app.RulesAsset.Rules.TackleHealthCost(d.Stats):0.#} 生命  ·  原地复活 5 秒");
            Text("passive-copy", d.PassiveDescription);
            Text("q-name", $"Q / {d.QName}  · {d.Stats.Loadout.QEnergy:0} 能量 + {d.Stats.QHealthCost:0} 生命 / {d.Stats.QCooldown:0.#} 秒");
            Text("q-copy", d.QDescription);
            Text("e-name", $"E / {d.EName}  · {d.Stats.Loadout.EEnergy:0} 能量" + (d.Stats.EHealthCost > 0 ? $" + {d.Stats.EHealthCost:0} 生命" : "") + $" / {d.Stats.ECooldown:0.#} 秒");
            Text("e-copy", d.EDescription);
            root.Q<Button>("start-button").text = app.SelectedMode == MatchMode.Training ? "准备好了 · 开始训练    →" : "准备好了 · 开始对练    →";
            for (int i = 0; i < app.Characters.Length; i++)
                root.Q($"character-{i}").EnableInClassList("selected", app.Characters[i].Stats.Id == app.Selected);
        }

        public void ShowPause(bool paused)
        {
            bool online=app.IsNetworkMatch;
            Text("pause-title",online ? "联机菜单" : "比赛已暂停");
            Text("pause-copy",online ? "比赛继续进行，关闭菜单后恢复操作。" : "准备好了，就继续回到球场。");
            root.Q<Button>("pause-button").text=online ? "菜单  Esc" : "暂停  Esc";
            root.Q<Button>("restart-button").text=online ? "返回房间" : "重新开始";
            root.Q<Button>("restart-button").SetEnabled(!online || app.Room.IsHost);
            skillHelp?.Hide();
            Visible(root.Q("pause-overlay"), paused);
            if (!paused && app.Page == AppPage.Match) root.schedule.Execute(() => root.Focus());
        }

        public void RefreshMatch()
        {
            var state = app.Simulation.State;
            var p = state.Players[app.LocalPlayerId];
            var d = app.SelectedDefinition;
            bool training = state.Mode == MatchMode.Training;
            Text("mode-label", training ? "训练场" : app.IsNetworkMatch ? $"{(app.Room.Lobby.Route==Soccer.Core.Networking.NetworkRoute.LAN ? "局域网" : "互联网")} · {(state.Overtime ? "加时" : state.Half==1 ? "上半场" : "下半场")}" : state.Overtime ? "AI 对练 · 金球加时" : $"3 v 3 · {(state.Half==1 ? "上半场" : "下半场")}");
            Text("cyan-score", state.CyanScore.ToString());
            Text("amber-score", state.AmberScore.ToString());
            Text("clock-label", training ? "练习" : Clock(state.PeriodRemainingSeconds(app.Simulation.Rules)));
            Text("feedback-label", app.SelectingSkillTarget ? "选择技能方向 · 左键确认 · C 取消" : state.IsOpeningWarmup ? "右键移动热身 · Shift 追加路线 · S 停止 · 开球后可接球" : state.Phase == MatchPhase.SetPiece ? $"{state.RestartName} · {(state.RestartTeam == Team.Cyan ? "青队" : "橙队")}主罚 · {(state.PhaseRemaining > 0 ? "准备中" : state.RestartPlayerId == app.LocalPlayerId ? "V 键蓄力踢出" : "等待对方开球")} · 对手退开 {app.Simulation.Rules.SetPieceDistance:0.#} 米" : app.Feedback);
            Text("local-name", $"{p.Stats.Name} · 你");
            string possession = state.IsOpeningWarmup ? "热身中" : p.IsDead ? $"原地复活 {p.RespawnRemaining:0.0}s" : p.Stun > 0 ? $"眩晕 {p.Stun:0.0}s" :
                state.Ball.OwnerId == app.LocalPlayerId ? "持球中" : "无球";
            if (p.Preparation != ShotPreparation.None) possession += p.Preparation == ShotPreparation.Curve ? " · 弧线准备" : " · 重炮准备";
            Text("possession-label", possession);
            Text("ball-flight-status", state.Ball.VerticalVelocity>0 ? "技能高球上升 · 注意落点" : "技能高球下落 · 碰撞足球接球");
            Visible(root.Q("ball-flight-status"),state.Ball.Airborne);
            var health = root.Q<ProgressBar>("health-progress");
            health.value = p.Health / p.Stats.MaxHealth * 100;
            health.title = $"生命 {p.Health:0}/{p.Stats.MaxHealth:0}" + (p.ShieldHealth > 0 ? $" +{p.ShieldHealth:0}" : "");
            var energy = root.Q<ProgressBar>("energy-progress");
            energy.value = p.Energy / p.Stats.MaxEnergy * 100;
            energy.title = $"能量 {p.Energy:0}/{p.Stats.MaxEnergy:0}";
            Text("compact-health", $"生命 {p.Health:0} / {p.Stats.MaxHealth:0}" + (p.ShieldHealth > 0 ? $" +{p.ShieldHealth:0}" : ""));
            Text("compact-energy", $"能量 {p.Energy:0} / {p.Stats.MaxEnergy:0}");
            root.Q("compact-health").EnableInClassList("low-health", !p.IsDead && p.Health / p.Stats.MaxHealth * 100 < HudMotion.LowHealthPercent);
            bool marine = p.Stats.Id == CharacterId.Marine;
            if (hudCharacter != p.Stats.Id)
            {
                hudCharacter = p.Stats.Id;
                SetImage("hero-symbol", marine ? "portrait-marine-v2" : "portrait-thor-v2");
                SetImage("compact-hero-symbol", marine ? "portrait-marine-v2" : "portrait-thor-v2");
                SetImage("weapon-symbol", marine ? "skill-rifle-v2" : "skill-cannons-v2");
                SetImage("q-symbol", marine ? "skill-stim-v2" : "skill-drive-v2");
                SetImage("e-symbol", marine ? "skill-bomb-v2" : "skill-thruster-v2");
                root.Q("q-tile").tooltip = "Q · " + d.QName + "\n" + d.QDescription;
                root.Q("e-tile").tooltip = "E · " + d.EName + "\n" + d.EDescription;
                root.Q("g-tile").tooltip = "G · 提前引爆自己的炸弹，消耗10能量；与E冷却独立。";
                root.Q("kick-tile").tooltip = "V 键 · 按住蓄力，松开踢球；6秒冷却，C取消蓄力。";
                root.Q("tackle-tile").tooltip = "空格 · 铲球与推人，5秒冷却；推力越大，生命消耗越低。";
                root.Q("weapon-tile").tooltip = "右键敌方球员 · 自动追击，进入射程攻击；S 停止。";
            }
            Text("hud-stats", $"移速 {(p.Stats.Speed + p.SkillSpeedBonus) * app.Simulation.Rules.MovementSpeedScale:0.00}   ·   推力 {p.Stats.PushPower:0.00}");
            Text("weapon-cd", p.Ammo==0 ? "装填中" : "右键追击");
            Text("hud-q-name", d.QName); Text("hud-e-name", d.EName);
            Text("q-cost", $"{p.Stats.Loadout.QEnergy:0}能 / {p.Stats.QHealthCost:0}血");
            Text("e-cost", $"{p.Stats.Loadout.EEnergy:0}能" + (p.Stats.EHealthCost > 0 ? $" / {p.Stats.EHealthCost:0}血" : ""));
            float tackleCost = app.Simulation.Rules.TackleHealthCost(p.Stats);
            Text("tackle-cost", $"{tackleCost:0.#}生命");
            RenderTile("kick", "kick-cd", p, p.KickCooldown, app.Simulation.Rules.KickCooldown);
            RenderTile("tackle", "tackle-cd", p, p.TackleCooldown, app.Simulation.Rules.TackleCooldown, 0, tackleCost);
            RenderTile("q", "q-cd", p, p.QCooldown, p.Stats.QCooldown, p.Stats.Loadout.QEnergy, p.Stats.QHealthCost);
            RenderTile("e", "e-cd", p, p.ECooldown, p.Stats.ECooldown, p.Stats.Loadout.EEnergy, p.Stats.EHealthCost);
            RenderTile("weapon", "weapon-reload-cd", p, p.Ammo == 0 ? p.AmmoReloadRemaining : p.WeaponCooldown,
                p.Ammo == 0 ? p.Stats.Loadout.AmmoReloadSeconds : p.Stats.Loadout.WeaponCooldown);
            Visible(root.Q("g-tile"), marine);
            root.Q("hud-main").style.width = marine ? 400 : 332;
            RemoteBomb bomb = null;
            foreach (var candidate in state.Bombs) if (candidate.ActorId == p.Id) { bomb = candidate; break; }
            Visible(root.Q("bomb-action"), marine && bomb != null);
            Text("bomb-action", bomb == null ? "" : $"{bomb.Remaining:0.0}s 后爆炸");
            var remote = root.Q("g-tile");
            bool remoteBlocked = state.IsOpeningWarmup || p.IsDead || bomb == null || p.Energy < 10;
            remote.EnableInClassList("blocked", remoteBlocked);
            remote.EnableInClassList("armed", !remoteBlocked);
            Motion?.BlockIcon("g",remoteBlocked);
            Text("g-cd", p.IsDead ? "恢复中" : bomb == null ? "" : p.Energy < 10 ? "缺能量" : $"{bomb.Remaining:0.0}");
            var charge = root.Q<ProgressBar>("charge-progress");
            Visible(charge, p.Charging);
            float max = p.Preparation == ShotPreparation.Power ? p.Stats.PowerChargeSeconds : app.Simulation.Rules.ChargeSeconds;
            charge.value = Mathf.Clamp01(p.ChargeTime / max) * 100;
            charge.title = charge.value >= 100 ? "满蓄力 · 松开踢球" : $"蓄力 {charge.value:0}%";
            Visible(root.Q("training-tools"), training && !state.IsOpeningWarmup);
            root.Q("feedback-label").style.top = training && !state.IsOpeningWarmup ? Mathf.Max(141,root.Q("training-tools").layout.yMax+6) : 141;
            root.Q<Button>("debug-toggle").text = $"F1  判定显示：{(app.ShowDebug ? "开" : "关")}";
            root.Q<Button>("moving-target").text = $"目标：{(app.MovingTrainingTarget ? "移动" : "静止")}";
            bool phaseVisible = state.Phase == MatchPhase.GoalCelebration || state.Phase == MatchPhase.SetPiece || p.IsDead;
            Visible(root.Q("phase-label"), phaseVisible);
            root.Q("phase-label").EnableInClassList("recovering", p.IsDead && state.Phase == MatchPhase.Playing);
            if (state.Phase == MatchPhase.GoalCelebration) Text("phase-label", "进球！");
            else if (state.Phase == MatchPhase.SetPiece) Text("phase-label", state.RestartName);
            else if (p.IsDead) Text("phase-label", $"原地复活 {p.RespawnRemaining:0.0}s");
            Motion.Observe(p, state.Ball.OwnerId == app.LocalPlayerId, phaseVisible ? root.Q<Label>("phase-label").text : "");
            Motion.ObserveCountdown(state);
            skillHelp?.Refresh();
        }

        public void PlayEvent(MatchEvent e)
        {
            Motion?.SetEnabled(app.View.FeedbackEnabled);
            Motion?.Event(e,app.Simulation.State.Players[app.LocalPlayerId]);
            if (e.ActorId != app.LocalPlayerId) return;
            string name = e.Kind == MatchEventKind.Kick ? "kick-tile" : e.Kind == MatchEventKind.Weapon ? "weapon-tile" : e.Kind == MatchEventKind.Tackle ? "tackle-tile" :
                e.Kind == MatchEventKind.Skill || e.Kind == MatchEventKind.ActionRejected
                    ? e.Slot == SkillSlot.Q ? "q-tile" : e.Slot == SkillSlot.E ? "e-tile" : e.Slot == SkillSlot.Special ? "g-tile" : e.Slot == SkillSlot.Kick ? "kick-tile" : "tackle-tile" : null;
            if (name == null) return;
            Motion?.Pulse(name.Replace("-tile", ""), e.Kind == MatchEventKind.ActionRejected ? new Color(1,.38f,.25f) : new Color(1,.81f,.45f), 8, e.Kind == MatchEventKind.ActionRejected);
            var tile = root.Q(name);
            string style = e.Kind == MatchEventKind.ActionRejected ? "rejected" : "activated";
            tile.EnableInClassList(style, true);
            tile.schedule.Execute(() => tile.EnableInClassList(style, false)).StartingIn(180);
        }

        private void RefreshResults()
        {
            var state = app.Simulation.State;
            var p = state.Players[app.LocalPlayerId];
            bool training = state.Mode == MatchMode.Training;
            Text("result-title", training ? "训练完成" : state.CyanScore > state.AmberScore ? "青队获胜" : state.CyanScore < state.AmberScore ? "橙队获胜" : "双方平局");
            Text("result-score", $"{state.CyanScore}  :  {state.AmberScore}");
            root.Q<Button>("play-again").text=app.Room!=null ? "返回房间" : "再来一场";
            root.Q<Button>("play-again").SetEnabled(app.Room==null || app.Room.IsHost);
            Text("result-subtitle", $"{p.Stats.Name} · {(training ? "训练场" : app.Room != null ? "联机比赛" : "AI 对练")} · 有效时间 {Clock(state.ElapsedSeconds)}");
            Text("combat-summary", $"击倒 {p.Knockouts}  ·  被击倒 {p.Deaths}  ·  伤害 {p.DamageDealt:0}  ·  治疗 {p.HealingDone:0}");
            int[] scores = { p.Goals, p.Steals, p.Kicks, p.Passes };
            for (int i = 0; i < scoreFields.Length; i++) Text(scoreFields[i], scores[i].ToString());
        }

        private void RenderTile(string id, string label, PlayerState p, float seconds, float total, float energy = 0, float health = 0)
        {
            bool blocked = app.Simulation.State.IsOpeningWarmup || p.IsDead || (seconds <= 0 && (p.Health <= health || p.Energy < energy));
            var tile = root.Q(id + "-tile");
            tile.EnableInClassList("cooling", seconds > 0);
            tile.EnableInClassList("blocked", blocked);
            Motion?.Cooldown(id, p.IsDead ? 0 : seconds, total, blocked);
            Text(label, p.IsDead ? "恢复中" : seconds > 0 ? $"{Mathf.Ceil(seconds*10)/10f:0.0}" : p.Health <= health ? "缺生命" : p.Energy < energy ? "缺能量" : "");
        }
        private static string Clock(float seconds) { int whole = Mathf.CeilToInt(seconds); return $"{whole / 60:00}:{whole % 60:00}"; }

        private SkillHelp DescribeSkill(string id)
        {
            if(app.Simulation==null) return default;
            var state=app.Simulation.State; var p=state.Players[app.LocalPlayerId]; var d=app.SelectedDefinition;
            var kit=p.Stats.Loadout; var rules=app.Simulation.Rules;
            var help=new SkillHelp(); float remaining=0,energy=0,health=0;
            switch(id)
            {
                case "q":
                    help.Title="Q  /  "+d.QName; help.Body=d.QDescription;
                    energy=kit.QEnergy; health=p.Stats.QHealthCost; remaining=p.QCooldown;
                    help.Meta=$"冷却 {p.Stats.QCooldown:0.#} 秒 · {energy:0} 能量"+(health>0 ? $" · {health:0} 生命" : ""); break;
                case "e":
                    help.Title="E  /  "+d.EName; help.Body=d.EDescription + (p.Stats.Id == CharacterId.Thor ? " 按 E 选择方向，左键确认，C 取消。" : "");
                    energy=kit.EEnergy; health=p.Stats.EHealthCost; remaining=p.ECooldown;
                    help.Meta=$"冷却 {p.Stats.ECooldown:0.#} 秒 · {energy:0} 能量"+(health>0 ? $" · {health:0} 生命" : ""); break;
                case "g":
                    help.Title="G  /  提前引爆"; help.Body="提前引爆自己放置或装在足球上的遥控炸弹，击退并震晕范围内敌人。与 E 冷却独立。";
                    energy=10; help.Meta="消耗 10 能量 · 需要已放置的炸弹"; break;
                case "kick":
                    help.Title="V  /  踢球"; help.Body="持球时按住 V 蓄力，松开传球或射门。鼠标决定方向，C 可取消蓄力。";
                    remaining=p.KickCooldown; help.Meta=$"冷却 {rules.KickCooldown:0.#} 秒 · 不耗生命或能量"; break;
                case "tackle":
                    help.Title="空格  /  铲球"; help.Body="接触敌人脚前球即可夺球，接球后有 0.25 秒碰撞保护。铲球推开角色腾出空间；队友／背后推带球移动，正面／侧面推敌人时球留原地。推力越大生命消耗越低。";
                    health=rules.TackleHealthCost(p.Stats); remaining=p.TackleCooldown;
                    help.Meta=$"冷却 {rules.TackleCooldown:0.#} 秒 · {health:0.#} 生命"; break;
                default:
                    help.Title="右键  /  "+(p.Stats.Id==CharacterId.Marine ? "脉冲步枪" : "双联机炮"); help.Body=d.PassiveDescription;
                    remaining=p.Ammo==0 ? p.AmmoReloadRemaining : p.WeaponCooldown;
                    help.Meta=$"攻击间隔 {kit.WeaponCooldown:0.##} 秒 · 每 {kit.AmmoReloadSeconds:0.##} 秒补一格"; break;
            }
            help.State=state.IsOpeningWarmup ? "热身中 · 开球后可使用" : p.IsDead ? $"恢复中 · {p.RespawnRemaining:0.0} 秒" :
                remaining>0 ? $"{(id=="weapon" && p.Ammo==0 ? "装填" : "冷却")}中 · {remaining:0.0} 秒" :
                p.Health<=health ? "生命不足" : p.Energy<energy ? "能量不足" : "准备就绪";
            if(!state.IsOpeningWarmup && !p.IsDead && remaining<=0 && p.Health>health && p.Energy>=energy)
            {
                if(id=="kick" && state.Ball.OwnerId!=app.LocalPlayerId) help.State="接到足球后即可蓄力";
                if(id=="g")
                {
                    bool bomb=false; foreach(var item in state.Bombs) if(item.ActorId==app.LocalPlayerId) bomb=true;
                    if(!bomb) help.State="先使用 E 放置遥控炸弹";
                }
            }
            return help;
        }

        private void LateUpdate()
        {
            bool match=app!=null && app.Page==AppPage.Match;
            Motion?.Tick(Time.unscaledDeltaTime,match,app!=null && app.Paused);
            if(!match) skillHelp?.Hide(); else skillHelp?.Tick(Time.unscaledDeltaTime);
            if(match && app.Simulation!=null)
            {
                Motion?.PositionCountdown(app.WorldCamera,app.Simulation.State);
                MiniMap?.Refresh(app.WorldCamera,app.Simulation.State,app.Simulation.Rules,app.LocalPlayerId);
                ballLocator?.Refresh(app.WorldCamera,app.Simulation.State,MiniMap?.BoundsInRoot ?? default,app.LocalPlayerId);
            }
            else { ballLocator?.Hide(); MiniMap?.Hide(); }
        }

        public bool IsPointerOverControl()
        {
            if (root.panel == null) return false;
            var point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            var picked = root.panel.Pick(point);
            while (picked != null)
            {
                if (picked is Button || picked is Slider || picked is Toggle || picked is TextField || picked is DropdownField || picked.ClassListContains("skill-tile")) return true;
                picked = picked.parent;
            }
            return false;
        }
    }
}
