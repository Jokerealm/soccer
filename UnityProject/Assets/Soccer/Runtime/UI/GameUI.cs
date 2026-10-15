using System;
using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    /// <summary>UXML 负责层级，USS 负责样式，此文件只绑定按钮和刷新展示字段。</summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(200)]
    public sealed partial class GameUI : MonoBehaviour
    {
        public StyleSheet Styles;
        private VisualElement root;
        private AppController app;
        private readonly string[] pageNames = { "home-page", "select-page", "match-page", "results-page", "settings-page", "loading-page", "mode-details-page", "network-page", "room-page", "login-page", "play-page", "shop-page", "history-page", "profile-page", "friends-page", "nickname-page", "party-page" };
        public CharacterPreviewView CharacterPreview { get; private set; }
        public KeyBindingsUI KeyBindings { get; private set; }
        public bool BlocksKeyNavigation => communityLogin?.Visible==true || Chat?.BlocksGameplay==true || KeyBindings != null && KeyBindings.BlocksNavigation;
        public MenuStageView MenuStage { get; private set; }
        private Texture2D menuVeil;
        private readonly string[] homeButtons = { "bots-button", "normal-mode", "train-button", "profile-button", "shop-button", "settings-button" };
        private DraftUI draftUI;
        private TutorialUI tutorialUI;
        private ShopUI shopUI;
        private HistoryUI historyUI;
        private SocialUI socialUI;
        private CommunityUI communityUI;private CommunityLoginUI communityLogin;
        public void ShowCommunityLogin()=>communityLogin.Show();
        public void HideCommunityLogin()=>communityLogin.Hide();
        public ChatUI Chat {get;private set;}
        public void RefreshCommunity(){communityUI?.Refresh();communityLogin?.Refresh();}
        private NetworkUI network;
        private LoginUI login;
        private NicknameUI nickname;
        private readonly string[] scoreFields = { "stat-goals", "stat-steals", "stat-kicks", "stat-passes" };
        public VisualElement Root => root;
        private CharacterId? hudCharacter;
        public HudMotion Motion { get; private set; }
        private SkillTooltip skillHelp;
        private BallLocator ballLocator;
        public MatchMiniMap MiniMap { get; private set; }
        public MatchTacticsUI Tactics {get;private set;}
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
            BindButton("bots-button", app.OpenPlayMenu);
            BindButton("play-back",app.GoHome);
            BindButton("play-online",()=>app.OpenMode(GamePlaylist.Normal));
            BindButton("play-ai",app.BeginLocalDraft);
            BindButton("play-training",()=>app.OpenSelection(MatchMode.Training));
            BindButton("play-tutorial",app.StartTutorial);
            BindButton("tutorial-next",()=>app.NextTutorialStep());
            BindButton("tutorial-skip",()=>app.NextTutorialStep(true));
            BindButton("tutorial-previous",app.PreviousTutorialStep);
            BindButton("tutorial-retry",app.RestartTutorialStep);
            BindButton("tutorial-exit",()=>{app.GoHome();app.OpenPlayMenu();});
            BindButton("profile-button",()=>app.OpenProfile());BindButton("home-friends",app.OpenFriends);BindButton("home-party",app.OpenParty);BindButton("play-party",app.OpenParty);
            BindButton("history-back",app.BackFromHistory);
            BindButton("results-history",app.OpenHistory);
            BindButton("shop-button",app.OpenShop);
            BindButton("shop-back",app.GoHome);
            BindButton("settings-button", app.OpenSettings);
            BindButton("select-back", app.BackFromSelection);
            BindButton("normal-mode", () => app.OpenMode(GamePlaylist.Normal));
            BindButton("mode-back", app.BackFromMode);
            BindButton("route-login", app.OpenLogin);
            BindButton("login-steam",()=>app.EnterSteam());BindButton("steam-route",app.SelectSteamConnection);
            BindMenuNavigation();
            root.Q<Foldout>("character-parameters").RegisterValueChangedCallback(e=>root.Q("character-parameter-content").EnableInClassList("hidden",!e.newValue));
            BindButton("internet-route", () => app.SelectConnection(ConnectionRoute.Internet));
            BindButton("lan-route", () => app.SelectConnection(ConnectionRoute.LAN));
            BindButton("settings-back", app.GoHome);
            BindButton("start-button", app.ConfirmSelection);
            BindButton("pause-button", () => app.SetPaused(true));
            BindButton("hud-mode-toggle", () => SetFullHud(!FullHudEnabled));
            BindButton("hud-mode-setting", () => SetFullHud(!FullHudEnabled));
            BindButton("resume-button", () => app.SetPaused(false));
            BindButton("restart-button", app.ReplaySession);
            BindButton("pause-home", app.GoHome);
            BindButton("results-home", app.GoHome);
            BindButton("play-again", app.ReplaySession);
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
            }
            string[] symbols = { "weapon", "tackle", "kick", "g" };
            string[] assets = { "rifle", "tackle", "kick", "trigger" };
            for (int i=0;i<symbols.Length;i++) SetImage(symbols[i]+"-symbol", "skill-"+assets[i]+"-v2");
            var splash = Resources.Load<Texture2D>("FrontEnd/lobby-v3");
            if (splash != null)
            {
                root.Q("loading-art").style.backgroundImage = new StyleBackground(splash);

            }
            CreateMenuVeil();
            MenuStage = new GameObject("Main menu presentation").AddComponent<MenuStageView>();
            MenuStage.Initialize(app);
            CharacterPreview = new GameObject("Character selection presentation").AddComponent<CharacterPreviewView>();
            CharacterPreview.Initialize(app,root.Q("character-preview"));
            BindButton("preview-auto",CharacterPreview.ToggleAutoRotate);
            BindButton("preview-reset",CharacterPreview.ResetView);
            KeyBindings = new KeyBindingsUI(root,app.Keys);
            app.Keys.Changed += RefreshBindingHints;
            RefreshBindingHints();
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
            Tactics=new MatchTacticsUI(app,root);
            var aiDifficulty=root.Q<DropdownField>("ai-difficulty");aiDifficulty.choices=new System.Collections.Generic.List<string>(BotDifficulties.Names);
            aiDifficulty.SetValueWithoutNotify(BotDifficulties.Names[(int)app.SelectedBotDifficulty]);
            root.Q<Label>("ai-difficulty-note").text=BotDifficulties.Descriptions[(int)app.SelectedBotDifficulty];
            aiDifficulty.RegisterValueChangedCallback(e=>{int index=Array.IndexOf(BotDifficulties.Names,e.newValue);if(index<0)return;app.SelectBotDifficulty((BotDifficulty)index);root.Q<Label>("ai-difficulty-note").text=BotDifficulties.Descriptions[index];});
            login=new LoginUI(app,root);
            nickname=new NicknameUI(app,root);
            BindButton("home-account", app.OpenLogin);
            network=new NetworkUI(app,root);
            draftUI=new DraftUI(app,root);tutorialUI=new TutorialUI(app,root);shopUI=new ShopUI(app,root);historyUI=new HistoryUI(app,root);socialUI=new SocialUI(app,root);Chat=new ChatUI(app,root);communityUI=new CommunityUI(app,root);communityLogin=new CommunityLoginUI(app,root);
            SetImage("shop-marine-art","portrait-marine-v2");SetImage("shop-thor-art","portrait-thor-v2");
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
        public void BeginNickname()=>nickname?.Begin();
        public void RefreshNickname()=>nickname?.Refresh();
        public bool JoinDialogOpen=>network?.JoinDialogOpen==true;
        public void CloseJoinDialog()=>network?.CloseJoinDialog();
        public void ResetNetworkForm()=>network?.Reset();
        public void PromptRoomPassword(string code)=>network?.OpenJoinDialog(code,true);

        public void RefreshModeDetails()
        {
            bool online = app.EntryMode == GameEntryMode.Online;
            root.Q<Button>("internet-route").SetEnabled(online);
            root.Q<Button>("lan-route").SetEnabled(true);
            root.Q("internet-route").EnableInClassList("route-selected", app.Route == ConnectionRoute.Internet);
            root.Q("lan-route").EnableInClassList("route-selected", app.Route == ConnectionRoute.LAN);
            root.Q("steam-route").EnableInClassList("route-selected",app.Route==ConnectionRoute.Steam);
            Text("connection-copy",app.Route==ConnectionRoute.Steam ? app.Steam.Ready ? "" : app.Steam.Status : online ? "" : "联网登录后可使用互联网房间。" );
            Visible(root.Q("connection-copy"),app.Route==ConnectionRoute.Steam ? !app.Steam.Ready : !online);
            Visible(root.Q("route-login"),!online && !app.SteamEntry);
        }

        private void CreateMenuVeil()
        {
            // Native gradient keeps the menu legible while the actual game world stays visible.
            menuVeil = new Texture2D(256, 128, TextureFormat.RGBA32, false) { name = "Main menu lighting veil", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[256 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 256; x++)
            {
                float horizontal = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.20f, .73f, x / 255f));
                float edge = Mathf.Pow(Mathf.Abs(y / 127f * 2 - 1), 3);
                pixels[y * 256 + x] = new Color(.025f, .055f, .09f, Mathf.Clamp01(.12f + horizontal * .78f + edge * .24f));
            }
            menuVeil.SetPixels(pixels); menuVeil.Apply(false, true);
            var veil = root.Q("menu-backdrop"); veil.style.backgroundColor = Color.clear;
            veil.style.backgroundImage = new StyleBackground(menuVeil); veil.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100)); veil.pickingMode = PickingMode.Ignore;
            var shade = veil.Q(className: "menu-shade"); shade.style.backgroundColor = Color.clear; shade.pickingMode = PickingMode.Ignore;
        }

        private void OnDestroy()
        {
            if (menuVeil != null) Destroy(menuVeil);
            if (MenuStage != null) Destroy(MenuStage.gameObject);
            if (CharacterPreview != null) Destroy(CharacterPreview.gameObject);
            if (app?.Keys != null) app.Keys.Changed -= RefreshBindingHints;
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
        public void RefreshSocial()=>socialUI?.Refresh();
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
        private void Text(string name, string text) => root.Q<Label>(name).text = app.Keys.FormatHint(text);
        private void RefreshAudioSettings()
        {
            Motion?.SetEnabled(app.View.FeedbackEnabled);
            root.Q<Button>("mute-toggle").text = $"静音：{(app.Audio.Muted ? "开" : "关")}";
            root.Q<Button>("feedback-toggle").text = $"命中反馈：{(app.View.FeedbackEnabled ? "开" : "关")}";
        }

        public void ShowPage(AppPage page)
        {
            ballLocator?.Hide();
            MiniMap?.Hide();Tactics?.CancelWheel();
            for (int i = 0; i < pageNames.Length; i++) Visible(root.Q(pageNames[i]), i == (int)page);
            MenuStage?.SetVisible(page != AppPage.Match && page != AppPage.Results);
            MenuStage?.SetLineupVisible(page != AppPage.CharacterSelect);
            CharacterPreview?.SetVisible(page == AppPage.CharacterSelect);
            if (page != AppPage.Settings) KeyBindings?.Cancel();
            else KeyBindings?.Show(false);
            ShowPause(false);
            RefreshLogin();
            shopUI?.Refresh();
            if(page==AppPage.PlayMenu)root.schedule.Execute(()=>{if(app.Page==AppPage.PlayMenu)root.Q<Button>("play-online").Focus();});
            if (page == AppPage.History)historyUI.Open();
            if(page==AppPage.Profile)socialUI.OpenProfile();socialUI?.Refresh();
            if (page == AppPage.CharacterSelect){root.Q<Foldout>("character-parameters").SetValueWithoutNotify(false);root.Q("character-parameter-content").AddToClassList("hidden");RefreshSelection();}
            if (page == AppPage.ModeDetails || page==AppPage.NetworkMenu) RefreshModeDetails();
            if (page == AppPage.NetworkMenu || page == AppPage.RoomLobby) RefreshNetwork();
            Visible(root.Q("menu-backdrop"), page != AppPage.Match && page != AppPage.Loading);
            if (page == AppPage.Home) root.schedule.Execute(() =>
            {
                var focused = root.panel.focusController.focusedElement as VisualElement;
                if (app.Page == AppPage.Home && !(focused is Button && root.Q("home-page").Contains(focused)))
                    root.Q<Button>("bots-button").Focus();
            });
            Motion?.Enter(root.Q(pageNames[(int)page]), page == AppPage.Match);
            if (page == AppPage.Match)
            {
                RefreshMatch();
                // 清除选角按钮焦点，避免抢断的空格触发按钮。
                root.schedule.Execute(() => root.Focus());
            }
            if (page == AppPage.Results) RefreshResults();
            if(page==AppPage.Party || page==AppPage.Home || page==AppPage.PlayMenu)RefreshCommunity();
        }

        public void RefreshDraft() => draftUI?.Refresh();
        public void RefreshSelection()
        {
            var d = app.SelectedDefinition;
            CharacterPreview?.Select(d);
            Text("select-title", app.SelectedMode == MatchMode.Training ? "选择角色 · 训练场" : "选择角色 · 合作乱斗");
            Text("select-subtitle", app.SelectedMode == MatchMode.Training ? "练习移动、传射和角色技能。" : "3v3 AI 对练 · 选择你的角色。");
            Text("selected-name", d.Stats.Name);
            bool marine=d.Stats.Id==CharacterId.Marine;
            Text("selected-role",marine ? "灵活推进 · 区域控制" : "重装突进 · 阵地突破");
            Text("selected-summary",marine ? "利用速度抢位，用炸弹为队伍打开进攻空间。" : "顶住正面压力，用突进打散对手站位。");
            Text("selected-stats",$"生命 {d.Stats.MaxHealth:0} · 能量 {d.Stats.MaxEnergy:0}\n移动速度 {d.Stats.Speed*app.RulesAsset.Rules.MovementSpeedScale:0.##} · 推力 {d.Stats.PushPower:0.##}\nV 键踢球 {app.RulesAsset.Rules.KickCooldown:0.#} 秒冷却\n空格推抢 {app.RulesAsset.Rules.TackleCooldown:0.#} 秒冷却 · 消耗 {app.RulesAsset.Rules.TackleHealthCost(d.Stats):0.#} 生命");
            Text("passive-name",marine ? "脉冲步枪" : "双联机炮");
            Text("passive-copy",marine ? "追击目标，持续火力压制。" : "近距离重火力，逼退防守球员。");
            Text("q-name",$"Q / {d.QName}");Text("q-copy",marine ? "短时加速，快速抢位或脱离围堵。" : "高速驱动，快速调整进攻位置。");
            Text("e-name",$"E / {d.EName}");Text("e-copy",marine ? "布置炸弹制造击退，也可装在足球上。" : "选择方向后突进，突破对手站位。");
            Text("selection-controls",marine ? "V 传射 · 空格推抢 · G 引爆" : "V 传射 · 空格推抢 · E 后左键确认");
            Text("q-detail",$"{d.QName} · {d.Stats.Loadout.QEnergy:0} 能量"+(d.Stats.QHealthCost>0 ? $" + {d.Stats.QHealthCost:0} 生命" : "")+$" · 冷却 {d.Stats.QCooldown:0.#} 秒\n"+d.QDescription);
            Text("e-detail",$"{d.EName} · {d.Stats.Loadout.EEnergy:0} 能量"+(d.Stats.EHealthCost>0 ? $" + {d.Stats.EHealthCost:0} 生命" : "")+$" · 冷却 {d.Stats.ECooldown:0.#} 秒\n"+d.EDescription);
            Text("weapon-detail",d.PassiveDescription);
            SetImage("selection-weapon-art",marine ? "skill-rifle-v2" : "skill-cannons-v2");
            SetImage("selection-q-art",marine ? "skill-stim-v2" : "skill-drive-v2");SetImage("selection-e-art",marine ? "skill-bomb-v2" : "skill-thruster-v2");
            root.Q<Button>("start-button").text = app.SelectedMode == MatchMode.Training ? "开始训练" : "开始比赛";
            root.Q<Button>("start-button").SetEnabled(true);root.Q<Button>("select-back").text="‹ 返回";
            for (int i = 0; i < app.Characters.Length; i++)
            { root.Q($"character-{i}").EnableInClassList("selected", app.Characters[i].Stats.Id == app.Selected);root.Q<Button>($"character-{i}").SetEnabled(true); }
            RefreshDraft();
        }

        public void ShowPause(bool paused)
        {
            bool online=app.IsNetworkMatch || app.IsServiceMatch;
            Text("pause-title",online ? "联机菜单" : "比赛已暂停");
            Text("pause-copy",online ? "比赛继续进行，关闭菜单后恢复操作。" : "准备好了，就继续回到球场。");
            root.Q<Button>("pause-button").text=app.Keys.FormatHint(online ? "菜单  Esc" : "暂停  Esc");
            root.Q<Button>("restart-button").text=online ? "返回房间" : "重新开始";
            root.Q<Button>("restart-button").SetEnabled(!online || (app.IsServiceMatch || app.Room.IsHost));
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
            Text("mode-label", app.Tutorial!=null ? "新手教程" : training ? "训练场" : app.IsServiceMatch ? (app.ServiceMatch.Assignment.Playlist=="ranked" ? "排位 · " : app.ServiceMatch.Assignment.Playlist=="custom" ? "好友房间 · " : "普通匹配 · ")+(state.Half==1 ? "上半场" : "下半场") : app.IsNetworkMatch ? $"{(app.Room.Lobby.Route==Soccer.Core.Networking.NetworkRoute.Steam ? "Steam" : app.Room.Lobby.Route==Soccer.Core.Networking.NetworkRoute.LAN ? "局域网" : "互联网")} · {(state.Overtime ? "加时" : state.Half==1 ? "上半场" : "下半场")}" : state.Overtime ? "AI 对练 · 金球加时" : $"3 v 3 · {(state.Half==1 ? "上半场" : "下半场")}");
            Text("cyan-score", state.CyanScore.ToString());
            Text("amber-score", state.AmberScore.ToString());
            Text("clock-label", training ? "练习" : Clock(state.PeriodRemainingSeconds(app.Simulation.Rules)));
            Text("feedback-label", app.SelectingSkillTarget ? "选择技能方向 · 左键确认 · C 取消" : state.IsOpeningWarmup ? "六边形内右键热身 · 空气墙开球解除 · S 停止" : state.Phase == MatchPhase.SetPiece ? $"{state.RestartName} · {(state.RestartTeam == Team.Cyan ? "青队" : "橙队")}主罚 · {(state.PhaseRemaining > 0 ? "准备中" : state.RestartPlayerId == app.LocalPlayerId ? "V 键蓄力踢出" : "等待对方开球")} · 对手退开 {app.Simulation.Rules.SetPieceDistance:0.#} 米" : app.Feedback);
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
                root.Q("q-tile").tooltip = app.Keys.FormatHint("Q · " + d.QName + "\n" + d.QDescription);
                root.Q("e-tile").tooltip = app.Keys.FormatHint("E · " + d.EName + "\n" + d.EDescription);
                root.Q("g-tile").tooltip = app.Keys.FormatHint("G · 提前引爆自己的炸弹，消耗10能量；与E冷却独立。");
                root.Q("kick-tile").tooltip = app.Keys.FormatHint("V 键 · 按住蓄力，松开踢球；6秒冷却，C取消蓄力。");
                root.Q("tackle-tile").tooltip = app.Keys.FormatHint("空格 · 铲球与推人，5秒冷却；推力越大，生命消耗越低。");
                root.Q("weapon-tile").tooltip = app.Keys.FormatHint("右键敌方球员 · 自动追击，进入射程攻击；S 停止。");
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
            Visible(root.Q("training-tools"), training && !state.IsOpeningWarmup && app.Tutorial==null);
            Visible(root.Q("feedback-label"),FullHudEnabled && app.Tutorial==null);
            root.Q("feedback-label").style.top = training && !state.IsOpeningWarmup ? Mathf.Max(141,root.Q("training-tools").layout.yMax+6) : 141;
            root.Q<Button>("debug-toggle").text = app.Keys.FormatHint($"F1  判定显示：{(app.ShowDebug ? "开" : "关")}");
            root.Q<Button>("moving-target").text = $"目标：{(app.MovingTrainingTarget ? "移动" : "静止")}";
            bool phaseVisible = state.Phase == MatchPhase.GoalCelebration || state.Phase == MatchPhase.SetPiece || p.IsDead;
            Visible(root.Q("phase-label"), phaseVisible);
            root.Q("phase-label").EnableInClassList("recovering", p.IsDead && state.Phase == MatchPhase.Playing);
            if (state.Phase == MatchPhase.GoalCelebration) Text("phase-label", "进球！");
            else if (state.Phase == MatchPhase.SetPiece) Text("phase-label", state.RestartName);
            else if (p.IsDead) Text("phase-label", $"原地复活 {p.RespawnRemaining:0.0}s");
            Motion.Observe(p, state.Ball.OwnerId == app.LocalPlayerId, phaseVisible ? root.Q<Label>("phase-label").text : "");
            Motion.ObserveCountdown(state);
            skillHelp?.Refresh();tutorialUI?.Refresh();
            if(app?.Page==AppPage.Results && app.IsServiceMatch && app.ServiceMatch.Assignment.Playlist=="ranked")
            {
                bool settled=app.Community.Match?.Id==app.ServiceMatch.Assignment.Id && app.Community.Match.Phase=="已结束";
                int delta=app.Community.Match?.RatingChange??0;
                Text("result-rank",settled ? "排位分 "+app.Community.Rating+"（"+(delta>=0 ? "+" : "")+delta+"）" : "服务器正在结算排位分…");
                Visible(root.Q("result-rank"),true);
            }
            else Visible(root.Q("result-rank"),false);
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
            Text("result-reward",app.MatchRewardText);
            Text("result-coins",app.Wallet==null ? "金币余额 —" : "金币余额  "+app.Wallet.Coins.ToString("N0"));
            Text("result-score", $"{state.CyanScore}  :  {state.AmberScore}");
            root.Q<Button>("play-again").text=app.IsServiceMatch ? app.ServiceMatch.Assignment.Playlist=="custom" ? "返回组队大厅" : "返回队伍" : app.Room!=null ? "返回房间" : "再来一场";
            root.Q<Button>("play-again").SetEnabled(app.IsServiceMatch || app.Room==null || app.Room.IsHost);
            Text("result-subtitle", $"{p.Stats.Name} · {(training ? "训练场" : app.IsServiceMatch ? app.ServiceMatch.Assignment.Playlist=="ranked" ? "排位比赛" : "联网比赛" : app.Room != null ? "联机比赛" : "AI 对练")} · 有效时间 {Clock(state.ElapsedSeconds)}");
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
        private void RefreshBindingHints()
        {
            foreach (var pair in new[] { ("q",GameAction.SkillQ), ("e",GameAction.SkillE), ("g",GameAction.Special), ("kick",GameAction.Kick), ("tackle",GameAction.Push) })
                root.Q(pair.Item1 + "-tile").Q<Label>(className:"ability-key").text = app.Keys.Label(pair.Item2);
            foreach (var pair in new[] { ("reset-ball","R  重置球场"), ("give-ball","F  自己持球"), ("target-ball","T  目标持球"), ("training-corner","F2  角球"), ("training-free-kick","F3  任意球") })
                root.Q<Button>(pair.Item1).text = app.Keys.FormatHint(pair.Item2);
            hudCharacter = null;
            if (app.Page == AppPage.CharacterSelect) RefreshSelection();
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
            help.Title = app.Keys.FormatHint(help.Title); help.Body = app.Keys.FormatHint(help.Body); help.State = app.Keys.FormatHint(help.State);
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
                Tactics?.Render();
                RefreshCommunity();
                ballLocator?.Refresh(app.WorldCamera,app.Simulation.State,MiniMap?.BoundsInRoot ?? default,app.LocalPlayerId);
            }
            else { ballLocator?.Hide(); MiniMap?.Hide(); }
            tutorialUI?.Refresh();
            if(app?.Page==AppPage.Results && app.IsServiceMatch && app.ServiceMatch.Assignment.Playlist=="ranked")
            {
                bool settled=app.Community.Match?.Id==app.ServiceMatch.Assignment.Id && app.Community.Match.Phase=="已结束";
                int delta=app.Community.Match?.RatingChange??0;
                Text("result-rank",settled ? "排位分 "+app.Community.Rating+"（"+(delta>=0 ? "+" : "")+delta+"）" : "服务器正在结算排位分…");
                Visible(root.Q("result-rank"),true);
            }
            else Visible(root.Q("result-rank"),false);
        }

        public bool IsPointerOverControl()
            => IsPointerOverControl(Input.mousePosition);
        public bool IsPointerOverControl(Vector3 mousePosition)
        {
            if (Chat?.BlocksGameplay==true || Tactics?.BlocksGameplay==true) return true;
            if (root.panel == null) return false;
            var point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(mousePosition.x, Screen.height - mousePosition.y));
            var picked = root.panel.Pick(point);
            while (picked != null)
            {
                if (picked is Button || picked is Slider || picked is Toggle || picked is TextField || picked is DropdownField || picked is ScrollView || picked.ClassListContains("chat-panel") || picked.ClassListContains("skill-tile") || picked.ClassListContains("tutorial-panel") || picked.ClassListContains("minimap")) return true;
                picked = picked.parent;
            }
            return false;
        }
    }
}
