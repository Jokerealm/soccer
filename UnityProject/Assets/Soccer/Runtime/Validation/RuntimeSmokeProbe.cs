using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    /// <summary>仅 --soccer-smoke 显式启动时执行的真实 Player 检查；不参与正常游戏流程。</summary>
    public sealed partial class RuntimeSmokeProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public string editor;
            public string platform;
            public bool passed;
            public string[] checks;
            public string[] errors;
            public string largeWindowActualResolution;
            public float unobstructedSampleFps;
        }
        private AppController app;
        private string output;
        private readonly List<string> checks = new List<string>();
        private readonly List<string> errors = new List<string>();
        private float unobstructedSampleFps;

        private IEnumerator Start()
        {
            app = GetComponent<AppController>();
            int originalWidth = Screen.width, originalHeight = Screen.height;
            FullScreenMode originalMode = Screen.fullScreenMode;
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--soccer-report-dir");
            output = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "Smoke");
            Directory.CreateDirectory(output);
            Application.logMessageReceived += CaptureError;
            yield return null;
            Check(app.Page == AppPage.Loading && !app.StartupReady, "startup begins on loading artwork page");
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Capture("00-loading");
            float deadline = Time.realtimeSinceStartup + 10;
            while (!app.StartupReady && Time.realtimeSinceStartup < deadline) yield return null;
            Check(app.StartupReady && app.Page == AppPage.Login, "real asset preload opens login before any game mode");
            Check(app.UI.Root.Q<TextField>("login-password").isPasswordField, "startup password entry is masked");
            yield return new WaitForSecondsRealtime(.35f);
            yield return Capture("00-startup-login");
            bool socialPreview=Array.IndexOf(args,"--soccer-social-preview")>=0;
            app.UI.Root.Q<TextField>("login-name").value="a";app.UI.Root.Q<TextField>("login-password").value=socialPreview ? "incorrect" : "a";
            yield return Click("login-submit");
            deadline=Time.realtimeSinceStartup+10;while(app.LoginBusy && Time.realtimeSinceStartup<deadline)yield return null;
            Check(app.Page==AppPage.Login && app.Account==null && !app.LoginBusy && app.LoginStatus.Contains(socialPreview ? "帐号或密码错误" : "离线进入"),
                socialPreview ? "online social login rejects incorrect credentials" : "unavailable login service cannot authenticate locally and keeps offline entry usable");
            Check(app.UI.Root.Q<TextField>("login-password").value.Length==0,"failed online login clears the password field");
            yield return Click("login-offline");
            Check(app.Page == AppPage.Home && app.EntryMode == GameEntryMode.Offline && app.Account == null, "offline entry needs no login service and retains game access");
            if(Array.IndexOf(args,"--soccer-social-preview")>=0)
            {
                yield return CaptureOnlineSocial();
                File.WriteAllText(Path.Combine(output,"runtime-smoke.json"),JsonUtility.ToJson(new Report
                {editor=Application.unityVersion,platform=Application.platform.ToString(),passed=errors.Count==0,checks=checks.ToArray(),errors=errors.ToArray()},true));
                Application.logMessageReceived-=CaptureError;Application.Quit(errors.Count==0?0:1);yield break;
            }
            if (Array.IndexOf(args,"--soccer-history-preview") >= 0)
            {
                yield return CaptureHistoryAndSelection();
                File.WriteAllText(Path.Combine(output,"runtime-smoke.json"),JsonUtility.ToJson(new Report
                { editor=Application.unityVersion,platform=Application.platform.ToString(),passed=errors.Count==0,checks=checks.ToArray(),errors=errors.ToArray() },true));
                Application.logMessageReceived-=CaptureError;Application.Quit(errors.Count==0?0:1);yield break;
            }
            if (Array.IndexOf(args,"--soccer-shop-preview") >= 0)
            {
                yield return CaptureShopAndNavigation();
                File.WriteAllText(Path.Combine(output,"runtime-smoke.json"),JsonUtility.ToJson(new Report
                { editor=Application.unityVersion,platform=Application.platform.ToString(),passed=errors.Count==0,checks=checks.ToArray(),errors=errors.ToArray() },true));
                Application.logMessageReceived-=CaptureError;Application.Quit(errors.Count==0?0:1);yield break;
            }
            if (Array.IndexOf(args,"--soccer-draft-tutorial-preview") >= 0)
            {
                yield return CaptureDraftAndTutorial();
                File.WriteAllText(Path.Combine(output,"runtime-smoke.json"),JsonUtility.ToJson(new Report
                { editor=Application.unityVersion,platform=Application.platform.ToString(),passed=errors.Count==0,checks=checks.ToArray(),errors=errors.ToArray() },true));
                Application.logMessageReceived-=CaptureError;Application.Quit(errors.Count==0?0:1);yield break;
            }
            if (Array.IndexOf(args,"--soccer-settings-preview") >= 0)
            {
                yield return CaptureBindingsAndModels();
                File.WriteAllText(Path.Combine(output,"runtime-smoke.json"),JsonUtility.ToJson(new Report
                { editor=Application.unityVersion, platform=Application.platform.ToString(), passed=errors.Count==0, checks=checks.ToArray(),errors=errors.ToArray() },true));
                Application.logMessageReceived-=CaptureError; Application.Quit(errors.Count==0?0:1); yield break;
            }
            if(Array.IndexOf(args,"--soccer-contact-quick")>=0)
            {
                bool quickSavedHud=app.UI.FullHudEnabled;
                app.OpenSelection(MatchMode.Training);app.SelectCharacter(CharacterId.Marine);app.StartMatch();
                yield return new WaitForSecondsRealtime(3.4f);
                yield return CaptureHeldInterception();
                yield return CapturePossessionContacts();
                yield return CaptureMiniMapTraining();
                app.UI.SetFullHud(quickSavedHud,false);app.GoHome();
                File.WriteAllText(Path.Combine(output,"runtime-smoke.json"),JsonUtility.ToJson(new Report
                {editor=Application.unityVersion,platform=Application.platform.ToString(),passed=errors.Count==0,checks=checks.ToArray(),errors=errors.ToArray()},true));
                Application.logMessageReceived-=CaptureError;Application.Quit(errors.Count==0?0:1);yield break;
            }
            bool hadHudPreference=PlayerPrefs.HasKey(GameUI.FullHudPreferenceKey);
            bool savedFullHud=app.UI.FullHudEnabled;
            Check(app.UI.FullHudEnabled==GameUI.LoadFullHudPreference() && (hadHudPreference || app.UI.FullHudEnabled),
                "HUD loads saved preference and defaults to full skill dock for a new player");
            app.UI.SetFullHud(true,false);
            yield return null;
            Check(app.UI.Root.Q<Label>("loading-percent").text == "100%", "loading reaches ready state");
            Check(app.UI.Root.Q<Button>("train-button") != null, "UXML buttons loaded");
            Check(PicksButton("train-button"), "home button receives pointer hit");
            Check(Resources.Load<Texture2D>("FrontEnd/lobby-v3") != null &&
                Resources.Load<Texture2D>("FrontEnd/arena-v3") != null, "new lobby and arena artwork imported");
            var arena = app.View.GetComponent<PremiumArenaView>();
            Check(arena.Ready && arena.Floor.receiveShadows && arena.Floor.sharedMaterial.shader.name == "Standard",
                "arena image is a lit shadow-receiving ground material");
            Check(arena.KeyLight.shadows == LightShadows.Soft && arena.GetComponentsInChildren<Light>().Length == 3,
                "warm key light and two colored fill lights run in the actual scene");
            Check(app.UI.MenuStage.Visible && app.UI.MenuStage.ActorCount == 2 && app.Simulation == null,
                "main menu presents the two playable 3D models without starting a match");
            Check(app.UI.Root.Q("ranked-mode") == null && app.UI.Root.Q("party-mode") == null && app.UI.Root.Q("roster-button") == null,
                "main menu exposes working entrances and removes future-mode placeholders and duplicate training");
            Check(app.Characters.Length == 2 && app.Characters[0].Stats.Id == CharacterId.Marine && app.Characters[1].Stats.Id == CharacterId.Thor && app.Characters[0].Animations.Run.Length == 14, "two star reference characters and placeholder animation imports");
            Check(Array.TrueForAll(app.Characters, c => c.Stats.MaxHealth == 100 && c.Stats.MaxEnergy == 100), "both characters use 100 health and 100 energy");
            Check(app.UI.Root.Q<Button>("character-2") == null, "selection exposes exactly two playable characters");
            var audioClips = Resources.LoadAll<AudioClip>("SoccerAudio");
            Check(audioClips.Length == 31 && Array.TrueForAll(audioClips, c => c.samples > 0), "31 original audio clips imported");
            Check(app.Audio.MusicPlaying, "background music sources playing");
            float[] musicSamples = new float[2048];
            var musicSource = Array.Find(app.Audio.GetComponentsInChildren<AudioSource>(), source => source.clip != null && source.clip.name == "menu-loop");
            // GetOutputData 的采样缓冲在首次请求后才建立，不能把首次零数组当作静音。
            bool signal = false;
            for (int i = 0; i < 10 && !signal; i++)
            {
                musicSource.GetOutputData(musicSamples, 0);
                signal = Array.Exists(musicSamples, sample => Math.Abs(sample) > 0.00001f);
                if (!signal) yield return new WaitForSecondsRealtime(0.05f);
            }
            Debug.Log($"SOCCER_AUDIO_PROBE signal={signal} playing={musicSource.isPlaying} virtual={musicSource.isVirtual} time={musicSource.time} volume={musicSource.volume} listener={AudioListener.volume} loaded={musicSource.clip.loadState} dsp={AudioSettings.dspTime}");
            Check(signal, "music source produces nonzero audio samples");
            yield return Capture("01-home");
            string[] modeButtons = { "bots-button", "normal-mode", "train-button", "profile-button", "shop-button", "settings-button" };
            yield return CaptureMainMenuFlow();
            yield return CaptureHistoryAndSelection();
            yield return CaptureRefinements();
            yield return CaptureOfflineProfile();
            yield return CaptureShopAndNavigation();
            yield return CaptureDraftAndTutorial();
            yield return CaptureBindingsAndModels();
            if (Array.IndexOf(args, "--soccer-menu-preview") >= 0)
            {
                File.WriteAllText(Path.Combine(output, "runtime-smoke.json"), JsonUtility.ToJson(new Report
                { editor = Application.unityVersion, platform = Application.platform.ToString(), passed = errors.Count == 0,
                  checks = checks.ToArray(), errors = errors.ToArray() }, true));
                Application.logMessageReceived -= CaptureError; Application.Quit(errors.Count == 0 ? 0 : 1); yield break;
            }
            yield return Click("settings-button");
            yield return null;
            Check(app.Page == AppPage.Settings, "settings navigation");
            Check(app.UI.Root.Q<Slider>("music-slider") != null && app.UI.Root.Q<Slider>("effects-slider") != null,
                "separate music and effects controls");
            Check(PicksButton("mute-toggle") && PicksButton("feedback-toggle") &&
                app.UI.Root.Q<Button>("mute-toggle").worldBound.height > 20, "audio and feedback controls visible and receive pointer hit");
            var sliderThumb = app.UI.Root.Q<Slider>("volume-slider").Q(className: "unity-base-slider__dragger");
            Check(sliderThumb != null && sliderThumb.resolvedStyle.width >= 19, "settings slider uses custom large thumb");
            bool savedMute = app.Audio.Muted;
            float savedMusic = app.Audio.MusicVolume, savedEffects = app.Audio.EffectsVolume;
            if (!savedMute) yield return Click("mute-toggle");
            Check(AudioListener.volume == 0, "global mute silences listener");
            yield return Click("mute-toggle");
            Check(Math.Abs(AudioListener.volume - app.Audio.SavedMasterVolume) < 0.001f, "unmute restores master volume");
            app.UI.Root.Q<Slider>("music-slider").value = 0.25f;
            app.UI.Root.Q<Slider>("effects-slider").value = 0.35f;
            Check(Math.Abs(PlayerPrefs.GetFloat("soccer.music") - 0.25f) < 0.001f &&
                Math.Abs(PlayerPrefs.GetFloat("soccer.effects") - 0.35f) < 0.001f, "bus volume preferences persisted");
            app.UI.Root.Q<Slider>("music-slider").value = savedMusic;
            app.UI.Root.Q<Slider>("effects-slider").value = savedEffects;
            if (savedMute) yield return Click("mute-toggle");
            bool savedFeedback = app.View.FeedbackEnabled;
            yield return Click("feedback-toggle");
            Check(app.View.FeedbackEnabled != savedFeedback && PlayerPrefs.GetInt("soccer.feedback") == (savedFeedback ? 0 : 1),
                "feedback control changes and saves presentation preference");
            yield return Click("feedback-toggle");
            Check(PicksButton("hud-mode-setting"), "HUD preference is accessible in settings");
            yield return Click("hud-mode-setting");
            Check(!app.UI.FullHudEnabled && !GameUI.LoadFullHudPreference(), "settings switches to compact HUD and persists it");
            yield return Click("hud-mode-setting");
            yield return Capture("02-settings");
            yield return Click("settings-back");
            yield return Click("train-button");
            yield return null;
            for (int i = 0; i < app.Characters.Length; i++)
            {
                yield return Click($"character-{i}");
                yield return null;
                Check(PicksButton($"character-{i}"), $"character {i} receives pointer hit");
                Check(app.Selected == app.Characters[i].Stats.Id, $"character {i} selection");
                Check(app.UI.Root.Q<Label>("selected-name").text == app.Characters[i].Stats.Name &&
                    app.Characters[i].Stats.Loadout.AmmoCapacity == 3, $"star reference {i} shows matching three-stock weapon");
                yield return Capture($"03-selection-{i}");
            }
            yield return Click("character-0");
            yield return Click("start-button");
            yield return CaptureKickoffPresentation();
            yield return CaptureCraftedArena();
            Check(app.Page == AppPage.Match && app.Simulation.State.Mode == MatchMode.Training, "training scene started");
            Check(app.View.BallModel.PanelCount == 32 && app.View.BallModel.TriangleCount == 2880 &&
                app.View.BallModel.GetComponentsInChildren<MeshRenderer>().Length == 3,
                "football uses rounded 3D pentagon and hexagon meshes with recessed seams");
            var ground = new Vec2(4,3);
            var ray = app.WorldCamera.ScreenPointToRay(app.WorldCamera.WorldToScreenPoint(ArenaProjection.ToWorld(ground)));
            var plane = new Plane(Vector3.up,Vector3.zero);
            Check(plane.Raycast(ray,out float distance) && Vec2.Distance(ground,ArenaProjection.ToGround(ray.GetPoint(distance))) < 0.001f,
                "tilted camera ray preserves ground aim coordinates");
            Check(app.View.PlayerViews.Count == 2 && app.View.PlayerViews[0].Model.PartCount >= 20 &&
                app.View.PlayerViews[1].Model.PartCount >= 20 && app.View.PlayerViews[0].Model.HasRealtimeShadows && app.View.PlayerViews[1].Model.HasRealtimeShadows,
                "characters use volumetric Standard materials and real shadow-casting mesh parts");
            Check(!app.View.PlayerViews[0].Model.IsThor && app.View.PlayerViews[1].Model.IsThor,
                "marine and thor have distinct temporary 3D silhouettes");
            Check(app.UI.Root.Q("q-symbol").resolvedStyle.backgroundImage.texture.name == "skill-stim-v2" &&
                app.UI.Root.Q("e-symbol").resolvedStyle.backgroundImage.texture.name == "skill-bomb-v2",
                "marine HUD uses ability-specific original icons");
            yield return null;
            yield return new WaitForEndOfFrame();
            var strip = app.UI.Root.Q("ability-strip");
            Check(app.UI.Root.Q("health-progress").worldBound.yMin >= strip.worldBound.yMax &&
                app.UI.Root.Q("energy-progress").worldBound.yMin > app.UI.Root.Q("health-progress").worldBound.yMin &&
                app.UI.Root.Q("health-progress").worldBound.width >= strip.worldBound.width - 1,
                "battle dock places full-width health and energy bars below the square ability strip");
            yield return CaptureBallLighting();
            yield return CaptureCharacterModels();
            yield return CaptureHudFeedback();
            yield return CaptureCooldownRevision();
            yield return CaptureHudModes();
            yield return CaptureSkillHelp();
            yield return CaptureLowHealthWarning();
            yield return CaptureDynamicStadium();
            yield return CaptureLargeField();
            yield return CaptureRecoveryAndControls();
            yield return CaptureRtsRules();
            yield return CaptureHeldInterception();
            yield return CapturePossessionContacts();
            yield return CaptureMiniMapTraining();
            yield return CaptureFootballEffects();
            Check(app.UI.Root.Q<Button>("loft-training")==null,"ordinary UI has no universal loft training action");
            Check(!app.Simulation.State.Ball.Airborne && app.Simulation.State.Ball.FlightMode==BallFlightMode.Ground,
                "normal training starts with strictly planar ball rules");
            yield return Click("target-ball");
            var commands = new PlayerCommand[2];
            commands[0] = new PlayerCommand { Aim = new Vec2(1, 0), TacklePressed = true };
            app.Simulation.Step(commands);
            commands[0] = commands[0].WithoutEdges();
            for (int i = 0; i < 25; i++) app.Simulation.Step(commands);
            Check(app.Simulation.State.Ball.OwnerId != 1, "training front push releases the enemy football");
            Check(Math.Abs(app.Simulation.State.Players[0].Health - (100 - 10 / .62f + 25 * MatchSimulation.TickSeconds * app.Simulation.State.Players[0].Stats.HealthRegen)) < .02f && app.Simulation.State.Players[0].TackleCooldown > 4.5f, "training tackle pays force-dependent health then gradually regenerates during its five second cooldown");
            commands[0]=ContextOrder.Create(app.Simulation.State,app.Simulation.Rules,0,app.Simulation.State.Ball.Position,clickedBall:true);
            for(int pickupStep=0;pickupStep<100 && app.Simulation.State.Ball.OwnerId<0;pickupStep++)
            {StepPresented(commands);commands[0]=default;}
            Check(app.Simulation.State.Ball.OwnerId==0,"training player moves into released football to acquire it before charging");
            app.UI.RefreshMatch();
            yield return Capture("04-training");
            commands[0] = new PlayerCommand { Aim = new Vec2(1, 0), KickPressed = true, KickHeld = true };
            app.Simulation.Step(commands);
            app.Audio.Observe(app.Simulation.State, app.Simulation.Rules);
            app.UI.RefreshMatch();
            Check(app.Audio.ChargePlaying, "charging loop begins from gameplay state");
            Check(app.UI.Root.Q<ProgressBar>("charge-progress").value > 0, "HUD charge feedback tracks simulation");
            yield return Capture("04b-charge");
            yield return Click("pause-button");
            yield return null;
            long tick = app.Simulation.State.Tick;
            yield return new WaitForSecondsRealtime(0.25f);
            Check(app.Paused && tick == app.Simulation.State.Tick, "pause freezes simulation");
            Check(!app.Simulation.State.Players[0].Charging, "pause cancels pending shot");
            Check(!app.Audio.ChargePlaying && app.Audio.ActiveVoices == 0, "pause stops charge and gameplay sounds");
            yield return Capture("05-pause");
            yield return Click("resume-button");
            yield return null;
            app.SetPaused(true);
            app.View.SetFeedbackEnabled(true);
            app.GiveTargetBall();
            var moving = app.Simulation.State.Players[0];
            var beforeMove = moving.Position;
            commands[0] = new PlayerCommand { Aim = new Vec2(1, 0), Move = new Vec2(0, 1), AttackHeld = true };
            commands[1] = default;
            for (int i = 0; i < 45; i++) StepPresented(commands);
            Check(moving.Ammo == 0 && Math.Abs(moving.Position.Y - beforeMove.Y - moving.Stats.Speed * app.Simulation.Rules.MovementSpeedScale * .75f) < 0.01f,
                "runtime continuous strafing preserves speed through a three-attack burst");
            app.UI.RefreshMatch();
            Check(app.UI.Root.Q<Label>("weapon-cd").text.Contains("装填") && app.UI.Root.Q<Label>("weapon-reload-cd").text.Length>0,
                "weapon tile exposes actual reload state without a third resource counter");
            app.View.Render(app.Simulation.State, app.Simulation.Rules, 1, false, false);
            app.UI.ShowPause(false);
            yield return Capture("05a-strafe-stock");
            app.UI.ShowPause(true);
            app.GiveTargetBall();
            commands[0] = new PlayerCommand { Aim = new Vec2(1, 0), AttackPressed = true };
            StepPresented(commands); commands[0] = default;
            for (int i = 0; i < 12; i++) StepPresented(commands);
            Check(app.Simulation.State.Players[1].Health < 100 && app.View.ActiveDamageNumbers == 1,
                "marine weapon damages target and displays a hit number");
            app.UI.RefreshMatch(); app.View.Render(app.Simulation.State, app.Simulation.Rules, 1, false, false);
            app.UI.ShowPause(false); yield return Capture("05b-hit-number"); app.UI.ShowPause(true);
            app.View.SetFeedbackEnabled(false);
            Check(app.View.ActiveDamageNumbers == 0, "feedback toggle clears floating damage numbers");
            app.View.SetFeedbackEnabled(true);
            app.GiveTargetBall();
            commands[0] = new PlayerCommand { QPressed = true };
            StepPresented(commands); commands[0] = default;
            var marine = app.Simulation.State.Players[0];
            Check(Math.Abs(marine.Energy - 88) < .01f && Math.Abs(marine.Health - 90) < .01f && marine.QCooldown > 5.9f && marine.SkillSpeedBonus > .64f,
                "marine stim consumes 12 energy and 10 health and starts six second skill cooldown");
            app.UI.RefreshMatch();
            Check(app.UI.Root.Q<Label>("hud-q-name").text.Contains("兴奋剂"), "HUD displays marine skill names");
            app.GiveTargetBall();
            commands[0] = new PlayerCommand { EPressed = true }; StepPresented(commands); commands[0] = default;
            app.View.Render(app.Simulation.State, app.Simulation.Rules, 1, false, false); app.UI.RefreshMatch();
            Check(app.Simulation.State.Bombs.Count == 1 && app.View.ActiveBombViews == 1, "marine bomb has a visible 3D body and danger radius");
            Check(app.UI.Root.Q<Label>("bomb-action").text.Contains("后爆炸"), "HUD displays bomb fuse and G detonation action");
            app.UI.ShowPause(false); yield return Capture("05e-marine-bomb"); app.UI.ShowPause(true);
            commands[0] = new PlayerCommand { SpecialPressed = true }; StepPresented(commands); commands[0] = default;
            Check(app.Simulation.State.Bombs.Count == 0 && Math.Abs(app.Simulation.State.Players[0].Energy - 50) < .01f && app.Simulation.State.Players[1].Stun > 1.7f,
                "G detonates bomb for ten energy during E cooldown and stuns enemy");
            app.UI.RefreshMatch(); app.View.Render(app.Simulation.State, app.Simulation.Rules, 1, false, false);
            app.UI.ShowPause(false); yield return Capture("05f-marine-explosion"); app.UI.ShowPause(true);
            app.ResetTraining(true);
            commands[0] = new PlayerCommand { KickPressed = true, KickReleased = true, Aim = new Vec2(1, 0) };
            StepPresented(commands); commands[0] = default;
            Check(app.Simulation.State.Players[0].KickCooldown > 5.9f && app.Simulation.State.Players[0].TackleCooldown == 0,
                "left kick starts independent six second cooldown");
            for (int i = 0; i < 10; i++) StepPresented(commands);
            commands[0] = new PlayerCommand { TacklePressed = true, Aim = new Vec2(0, -1) }; StepPresented(commands); commands[0] = default;
            app.UI.RefreshMatch();
            yield return null;
            yield return new WaitForEndOfFrame();
            Check(app.Simulation.State.Players[0].KickCooldown > 0 && app.Simulation.State.Players[0].TackleCooldown > 4.9f &&
                app.UI.Root.Q<Label>("tackle-cost").text.Contains("生命") && app.UI.Root.Q<Label>("kick-cd").text.Length > 0 &&
                app.UI.Motion.CooldownFraction("kick") > .8f && app.UI.Motion.CooldownFraction("tackle") > .9f,
                "HUD exposes independent kick tackle timers and tackle health cost");
            app.View.Render(app.Simulation.State, app.Simulation.Rules, 1, false, false);
            app.UI.ShowPause(false); yield return Capture("05g-shared-cooldowns"); app.UI.ShowPause(true);
            app.GiveTargetBall();
            commands[0] = default;
            commands[1] = new PlayerCommand { Aim = new Vec2(-1, 0), AttackHeld = true };
            for (int i = 0; i < 240 && !app.Simulation.State.Players[0].IsDead; i++) app.Simulation.Step(commands);
            var downed = app.Simulation.State.Players[0];
            var deathPosition = downed.Position;
            Check(downed.IsDead && downed.RespawnRemaining > 4.9f, "enemy weapon knocks out local player with five-second countdown");
            app.UI.RefreshMatch();
            Check(app.UI.Root.Q<ProgressBar>("health-progress").value == 0 && app.UI.Root.Q<Label>("possession-label").text.Contains("原地复活"),
                "HUD shows health, energy and local respawn state");
            app.SetPaused(false);
            yield return Capture("05c-combat-respawn-countdown");
            Check(app.UI.Root.Q("minimap-player-0").ClassListContains("minimap-knocked-out") &&
                app.UI.Root.Q("minimap-portrait-0").resolvedStyle.opacity<.35f &&
                app.UI.Root.Q("minimap-down-0").resolvedStyle.display==DisplayStyle.Flex,
                "minimap keeps knocked-out portrait in place with dimming and a downed badge");
            app.SetPaused(true);
            commands[1] = default;
            for (int i = 0; i < 300; i++) app.Simulation.Step(commands);
            Check(!downed.IsDead && Math.Abs(downed.Health - 100) < 0.01f && Math.Abs(downed.Energy - 100) < 0.01f &&
                downed.Ammo == 3 && Vec2.Distance(downed.Position, deathPosition) < 0.01f, "local player revives in place with full resources and attack stock");
            app.UI.RefreshMatch();
            app.SetPaused(false);
            yield return Capture("05d-combat-revived");
            Check(!app.UI.Root.Q("minimap-player-0").ClassListContains("minimap-knocked-out") &&
                app.UI.Root.Q("minimap-portrait-0").resolvedStyle.opacity>.99f &&
                app.UI.Root.Q("minimap-down-0").resolvedStyle.display==DisplayStyle.None,
                "five-second real respawn restores the minimap portrait automatically");
            yield return Click("end-training");
            yield return null;
            Check(app.Page == AppPage.Results, "training results navigation");
            Check(!app.UI.MiniMap.Visible && app.UI.MiniMap.ActivePortraitCount==0,"results hides the minimap and active roster");
            yield return Capture("06-results");
            yield return Click("results-home");
            yield return Click("train-button"); yield return Click("character-1"); yield return Click("start-button");
            yield return new WaitForSecondsRealtime(3.4f);
            Check(app.UI.Root.Q("minimap-portrait-0").resolvedStyle.backgroundImage.texture.name=="portrait-thor-v2" &&
                app.UI.Root.Q("minimap-portrait-1").resolvedStyle.backgroundImage.texture.name=="portrait-marine-v2",
                "changing selected hero rebinds both minimap portraits without stale marine icons");
            app.SetPaused(true); app.ResetTraining(false);
            commands[0] = new PlayerCommand { QPressed = true }; commands[1] = default; StepPresented(commands); commands[0] = default;
            var thor = app.Simulation.State.Players[0];
            Check(thor.Stats.Id == CharacterId.Thor && Math.Abs(thor.Health - 90) < .01f && Math.Abs(thor.Energy - 80) < .01f && thor.SkillSpeedBonus > 2.3f,
                "thor drive starts with table health energy cost and speed gain");
            for (int i = 0; i < 22; i++) StepPresented(commands);
            Check(Math.Abs(thor.SkillSpeedBonus - 2.16f) < .01f, "thor drive decays after 0.375 seconds");
            commands[0] = new PlayerCommand { EPressed = true, Aim = new Vec2(0, 1) }; StepPresented(commands); commands[0] = default;
            for (int i = 0; i < 10; i++) StepPresented(commands);
            Check(thor.QCooldown == 0 && thor.ECooldown > 9.8f && Math.Abs(thor.Position.Y - 2.4f) < .01f && Math.Abs(thor.Health - (80 + 33 * MatchSimulation.TickSeconds * thor.Stats.HealthRegen)) < .02f,
                "thor thrusters dash on the ground and refresh drive cooldown");
            app.UI.RefreshMatch(); app.View.Render(app.Simulation.State, app.Simulation.Rules, 1, false, false);
            Check(app.UI.Root.Q<Label>("hud-e-name").text.Contains("辅助推进器") && app.UI.Root.Q("bomb-action").resolvedStyle.display == DisplayStyle.None,
                "thor HUD displays its own skills and hides marine detonation");
            yield return null;
            Check(app.UI.Root.Q("q-symbol").resolvedStyle.backgroundImage.texture.name == "skill-drive-v2" &&
                app.UI.Root.Q("e-symbol").resolvedStyle.backgroundImage.texture.name == "skill-thruster-v2-gray" &&
                app.UI.Root.Q("g-tile").resolvedStyle.display == DisplayStyle.None,
                "thor HUD swaps both skill icons and hides marine-only detonation");
            yield return new WaitForEndOfFrame();
            Check(Math.Abs(app.UI.Root.Q("battle-dock").worldBound.center.x-app.UI.Root.worldBound.center.x)<1 &&
                app.UI.Root.Q("battle-dock").worldBound.width<500,"five-action thor HUD contracts and stays centered with its portrait");
            app.UI.ShowPause(false); yield return Capture("05h-thor-thrusters");
            using(var evt=PointerEnterEvent.GetPooled()) {evt.target=app.UI.Root.Q("e-tile"); app.UI.Root.Q("e-tile").SendEvent(evt);}
            yield return new WaitForSecondsRealtime(.16f);
            Check(app.UI.SkillHelpVisible && app.UI.Root.Q<Label>("skill-tooltip-title").text.Contains("辅助推进器") &&
                app.UI.Root.Q<Label>("skill-tooltip-body").text.StartsWith(app.SelectedDefinition.EDescription) &&
                app.UI.Root.Q<Label>("skill-tooltip-body").text.Contains("左键确认") &&
                app.UI.Root.Q<Label>("skill-tooltip-meta").text.Contains("10 生命"),
                "thor hover uses its own thruster description and actual resource costs");
            yield return Capture("29-thor-skill-tooltip");
            using(var evt=PointerLeaveEvent.GetPooled()) {evt.target=app.UI.Root.Q("e-tile"); app.UI.Root.Q("e-tile").SendEvent(evt);}
            app.UI.ShowPause(true);
            app.GoHome();
            yield return null;
            yield return null;
            yield return Click("bots-button");
            yield return Click("play-ai");
            Debug.Log("SOCCER_SMOKE_BOT_SELECTION " + app.Page);
            yield return Click("character-1");
            yield return Click("start-button");
            float draftDeadline=Time.realtimeSinceStartup+5;
            while(app.InDraft && Time.realtimeSinceStartup<draftDeadline)yield return null;
            Check(!app.UI.MenuStage.Visible && app.View.ActorRoot.gameObject.activeSelf, "starting a match restores game actors and removes the menu lineup");
            Debug.Log("SOCCER_SMOKE_BOT_START " + app.Page + " simulation=" + (app.Simulation != null));
            yield return new WaitForSecondsRealtime(4.3f);
            Check(app.Simulation.State.Players.Count == 6 && app.Simulation.State.ElapsedSeconds > 0, "3v3 bot simulation running");
            Check(app.Audio.MusicPlaying && app.Audio.ActiveVoices <= 12, "match music active and effect voices bounded");
            yield return Capture("07-bot-match");
            yield return CaptureMiniMapMatch();
            yield return CaptureMatchPeriods();
            yield return CaptureFinalGoal();
            app.View.SetFeedbackEnabled(savedFeedback);
            app.GoHome();
            Screen.SetResolution(1024, 768, false);
            yield return new WaitForSecondsRealtime(0.5f);
            Check(app.WorldCamera.rect==new Rect(0,0,1,1), "4:3 menus use the whole screen without arena letterboxing");
            foreach (string name in modeButtons) Check(PicksButton(name), "4:3 main menu action remains accessible: " + name);
            yield return Capture("08-home-4x3");
            yield return Click("normal-mode");
            yield return Capture("09-normal-4x3");
            Check(PicksButton("network-create-tab") && PicksButton("network-join-tab"), "4:3 multiplayer create and join actions remain accessible");
            app.OpenSelection(MatchMode.Training); app.SelectCharacter(CharacterId.Marine); app.StartMatch();
            yield return new WaitForSecondsRealtime(3.4f);
            var dock = app.UI.Root.Q("battle-dock").worldBound;
            Check(dock.xMin >= 0 && dock.xMax <= app.UI.Root.worldBound.xMax &&
                app.UI.Root.Q("health-progress").worldBound.yMax <= dock.yMax,
                "4:3 battle dock and resource bars fit the actual viewport");
            yield return Capture("09b-match-4x3");
            CheckMiniMapLayout("4:3 full HUD");
            Check(app.UI.MiniMap.ActivePortraitCount==2 && app.UI.Root.Q("minimap-player-5").resolvedStyle.display==DisplayStyle.None,
                "returning from 3v3 to training hides surplus portrait markers");
            yield return Click("hud-mode-toggle");
            yield return new WaitForSecondsRealtime(.55f);
            var compact=app.UI.Root.Q("compact-player-hud").worldBound;
            Check(compact.xMin>=0 && compact.yMin>=0 && compact.xMax<app.UI.Root.Q("cyan-score").worldBound.xMin &&
                PicksButton("hud-mode-toggle") && PicksButton("pause-button"),
                "4:3 compact portrait, score and both corner controls fit without overlap");
            yield return Capture("09c-compact-match-4x3");
            CheckMiniMapLayout("4:3 compact HUD");
            yield return Click("hud-mode-toggle");
            app.GoHome();
            app.GoHome();
            Screen.SetResolution(1920, 800, false);
            yield return new WaitForSecondsRealtime(0.5f);
            foreach (string name in modeButtons) Check(PicksButton(name), "ultrawide main menu action remains accessible: " + name);
            Check(PicksButton("train-button") && PicksButton("settings-button"), "ultrawide utility buttons remain accessible");
            yield return Capture("10-home-ultrawide");
            Check(!app.UI.MiniMap.Visible,"home page never displays a stale minimap");
            app.OpenSelection(MatchMode.Training); app.StartMatch();
            yield return new WaitForSecondsRealtime(3.4f);
            CheckMiniMapLayout("ultrawide full HUD");
            yield return Capture("44-minimap-ultrawide");
            app.UI.SetFullHud(false,false); yield return null;
            CheckMiniMapLayout("ultrawide compact HUD");
            app.UI.SetFullHud(true,false); app.GoHome();
            Screen.SetResolution(2560, 1440, false);
            yield return new WaitForSecondsRealtime(0.5f);
            Check(Screen.width == 2560 && Screen.height == 1440 && PicksButton("normal-mode"), "1440p interface renders and remains interactive");
            yield return Capture("11-home-1440p");
            Screen.SetResolution(3840, 2160, false);
            yield return new WaitForSecondsRealtime(0.5f);
            string largeWindowActual = $"{Screen.width}x{Screen.height}";
            Check(Screen.width >= 2560 && Screen.height >= 1440 && PicksButton("normal-mode"),
                "large-window interface remains interactive at actual resolution " + largeWindowActual);
            yield return Capture("12-home-large-window");
            yield return Click("train-button");
            yield return Click("start-button");
            yield return new WaitForSecondsRealtime(3.4f);
            Check(PicksButton("pause-button") && PicksButton("target-ball"), "large-window HUD and training controls remain interactive");
            yield return Capture("13-training-large-window");
            CheckMiniMapLayout("large-window full HUD");
            app.GoHome();
            app.UI.SetFullHud(savedFullHud,false);
            if(hadHudPreference) PlayerPrefs.SetInt(GameUI.FullHudPreferenceKey,savedFullHud ? 1 : 0);
            else PlayerPrefs.DeleteKey(GameUI.FullHudPreferenceKey);
            PlayerPrefs.Save();
            Screen.SetResolution(originalWidth, originalHeight, originalMode);
            yield return new WaitForSecondsRealtime(0.1f);
            File.WriteAllText(Path.Combine(output, "runtime-smoke.json"), JsonUtility.ToJson(new Report
            {
                editor = Application.unityVersion, platform = Application.platform.ToString(), passed = errors.Count == 0,
                checks = checks.ToArray(), errors = errors.ToArray(), largeWindowActualResolution = largeWindowActual,
                unobstructedSampleFps=unobstructedSampleFps
            }, true));
            Application.logMessageReceived -= CaptureError;
            Application.Quit(errors.Count == 0 ? 0 : 1);
        }

        private IEnumerator CaptureRecoveryAndControls()
        {
            app.SetPaused(true); app.UI.ShowPause(false); app.ResetTraining(false);
            var state=app.Simulation.State;var p=state.Players[0];var commands=new PlayerCommand[state.Players.Count];
            SetSmokeHealth(p,60);
            for(int i=0;i<120;i++) app.Simulation.Step(commands);
            app.UI.RefreshMatch();
            Check(Math.Abs(p.Health-64)<.01f && Math.Abs(app.UI.Root.Q<ProgressBar>("health-progress").value-64)<.01f,
                "real client restores two health per second and displays the recovered health");
            float health=p.Health;yield return new WaitForSecondsRealtime(.2f);
            Check(Math.Abs(p.Health-health)<.001f,"offline pause stops passive health recovery");
            app.ResetTraining(false);
            typeof(PlayerState).GetProperty(nameof(PlayerState.Position)).SetValue(p,new Vec2(-8,-5));
            typeof(PlayerState).GetProperty(nameof(PlayerState.Position)).SetValue(state.Players[1],new Vec2(10,10));
            SetSmokeBall(nameof(BallState.Position),new Vec2(10,-8));
            var origin=p.Position;commands[0]=new PlayerCommand{Move=new Vec2(1,0)};
            for(int i=0;i<60;i++) app.Simulation.Step(commands);
            Check(Math.Abs(Vec2.Distance(p.Position,origin)-p.Stats.Speed*1.6f)<.01f,"real client walking uses the new speed scale");
            Check(app.UI.Root.Q("kick-tile").Q<Label>(className:"ability-key").text=="V","skill dock labels keyboard kick as V");
            app.ResetTraining(true);commands[0]=default;
            var gesture=new GameplayGesture();gesture.Apply(ref commands[0],p.Stats.Id,new GameplayButtons{OnField=true,LeftDown=true});app.Simulation.Step(commands);
            Check(!p.Charging && state.Ball.OwnerId==0,"left click with held ball leaves possession and starts no kick");
            app.View.Render(state,app.Simulation.Rules,1,true,false);
            Check(!app.View.ActorRoot.Find("Shot Direction Preview").GetComponent<LineRenderer>().enabled,"football trajectory stays hidden until keyboard charging");
            commands[0]=default;gesture.Apply(ref commands[0],p.Stats.Id,new GameplayButtons{OnField=true,KickDown=true,KickHeld=true});app.Simulation.Step(commands);
            app.View.Render(state,app.Simulation.Rules,1,true,false);
            Check(p.Charging && app.View.ActorRoot.Find("Shot Direction Preview").GetComponent<LineRenderer>().enabled,"V gesture charges real player and opens shot preview");
            yield return Capture("46-new-controls-and-recovery");app.ResetTraining(false);
        }

        private IEnumerator CaptureRtsRules()
        {
            app.SetPaused(true); app.UI.ShowPause(false); app.ResetTraining(false);
            var state=app.Simulation.State; var rules=app.Simulation.Rules; var p=state.Players[0];
            Check(rules.EnableOutOfPlay && Mathf.Abs(rules.BallRadius-BallModelView.Radius)<.001f,
                "football render and collision share the real sphere radius");
            Check(state.Players[1].Stats.Radius>p.Stats.Radius,"thor has a larger physical footprint than marine");
            typeof(PlayerState).GetProperty(nameof(PlayerState.Position)).SetValue(p,new Vec2(-4,0));
            typeof(PlayerState).GetProperty(nameof(PlayerState.Position)).SetValue(state.Players[1],Vec2.Zero);
            SetSmokeBall(nameof(BallState.Position),new Vec2(-4,6));
            var commands=new PlayerCommand[2]; commands[0]=ContextOrder.Create(state,rules,0,new Vec2(4,0));
            StepPresented(commands); commands[0]=default;
            Check(p.Order==UnitOrderKind.Move && p.NavigationPath.Count>1,"right click builds a body-aware route around opponent");
            app.View.Render(state,rules,1,true,false);app.UI.RefreshMatch();
            Check(app.View.ActorRoot.Find("Right click destination").GetComponent<LineRenderer>().enabled &&
                !app.View.ActorRoot.Find("Navigation route").GetComponent<LineRenderer>().enabled,"right click retains destination feedback without showing a route even in debug mode");
            yield return Capture("47-right-click-route");
            for(int tick=0;tick<320;tick++)StepPresented(commands);
            Check(Vec2.Distance(p.Position,new Vec2(4,0))<.12f && p.Order==UnitOrderKind.None,
                "single native game command reaches destination without holding right mouse");
            app.ResetTraining(false);app.UI.RefreshMatch();yield return null;
            Check(PicksButton("training-corner") && PicksButton("training-free-kick"),"corner and free kick practice controls receive pointer hits");
            yield return Click("training-corner");app.SetPaused(true);app.UI.ShowPause(false);
            Check(state.Restart==RestartKind.Corner && state.RestartPlayerId==0,"corner practice gives local player a fixed set piece");
            app.View.Render(state,rules,1,false,false);app.UI.RefreshMatch();yield return null;
            Check(app.UI.Root.Q<Label>("phase-label").text=="角球" && app.UI.Root.Q<Label>("feedback-label").text.Contains("青队"),
                "corner UI identifies restart type and awarded team");
            Check(app.View.ActorRoot.Find("Set piece exclusion").GetComponent<LineRenderer>().enabled,
                "set piece exclusion distance is visible on pitch");
            yield return Capture("48-corner-kick");
            for(int tick=0;tick<65;tick++)StepPresented(commands);
            commands[0]=new PlayerCommand {Aim=new Vec2(-1,-1),KickPressed=true,KickReleased=true};StepPresented(commands);commands[0]=default;
            Check(state.Phase==MatchPhase.Playing && state.Ball.OwnerId<0 && p.KickCooldown>5.9f,
                "corner kick resumes play with independent six second kick cooldown");
            yield return Click("training-free-kick");app.SetPaused(true);app.UI.ShowPause(false);app.UI.RefreshMatch();
            Check(state.Restart==RestartKind.FreeKick && app.UI.Root.Q<Label>("phase-label").text=="任意球",
                "free kick practice is interactive and labelled");
            app.View.Render(state,rules,1,false,false);yield return Capture("49-free-kick");
            app.ResetTraining(false);SetSmokeBall(nameof(BallState.Position),new Vec2(0,rules.FieldHeight/2+.1f));
            SetSmokeBall(nameof(BallState.LastTouchId),0);SetSmokeBall(nameof(BallState.Velocity),Vec2.Zero);StepPresented(commands);
            Check(state.Phase==MatchPhase.Playing,"partial native ball crossing stays in play");
            SetSmokeBall(nameof(BallState.Velocity),new Vec2(0,18));StepPresented(commands);app.UI.RefreshMatch();
            Check(state.Restart==RestartKind.ThrowIn && state.RestartTeam==Team.Amber,"whole native ball crossing awards opposing throw in");
            app.View.Render(state,rules,1,false,false);yield return Capture("50-throw-in");
            var bot=new BotCommandSource(rules);
            for(int tick=0;tick<180 && state.Phase==MatchPhase.SetPiece;tick++){commands[1]=bot.ReadCommand(state,1);StepPresented(commands);}
            Check(state.Phase==MatchPhase.Playing,"training opponent automatically takes awarded restart");
            app.ResetTraining(false);app.SetPaused(false);app.UI.RefreshMatch();
        }

        private IEnumerator CaptureHeldInterception()
        {
            app.SetPaused(true);app.UI.ShowPause(false);app.GiveTargetBall();
            var state=app.Simulation.State;var rules=app.Simulation.Rules;var p=state.Players[0];var target=state.Players[1];
            Check(Math.Abs(rules.PossessionSpeed-.8f)<.0001f && Math.Abs(rules.PossessionContactLock-.25f)<.0001f,
                "serialized runtime rules use map-derived carrying speed and reception protection");
            var ballPosition=new Vec2(target.Stats.Radius+rules.BallRadius+rules.FootGap,0);
            typeof(PlayerState).GetProperty(nameof(PlayerState.Position)).SetValue(target,Vec2.Zero);
            typeof(PlayerState).GetProperty(nameof(PlayerState.Facing)).SetValue(target,new Vec2(1,0));
            typeof(PlayerState).GetProperty(nameof(PlayerState.Position)).SetValue(p,ballPosition+new Vec2(p.Stats.Radius+rules.BallRadius,0));
            SetSmokeBall(nameof(BallState.Position),ballPosition);
            var commands=new PlayerCommand[2];commands[1]=new PlayerCommand {Aim=new Vec2(1,0)};
            int captureBefore=p.Steals;
            for(int tick=0;tick<14;tick++)StepPossessionPresented(commands);
            Check(state.Ball.OwnerId==target.Id && p.Steals==captureBefore,"fresh holder resists automatic collision for 0.25 seconds");
            StepPossessionPresented(commands);
            Check(state.Ball.OwnerId==p.Id && p.Steals==captureBefore+1,"held football surface contact captures immediately after reception lock expires");
            Check(p.TackleCooldown==0 && p.Health==100 && target.PushRemaining==0,"direct collision steal requires neither tackle nor health cost");
            Check(app.Feedback.Contains("碰撞夺球"),"direct possession transfer presents collision capture feedback");
            commands[0]=new PlayerCommand {Aim=new Vec2(-1,0)};
            for(int tick=0;tick<10;tick++)StepPossessionPresented(commands);
            Check(state.Ball.OwnerId==p.Id && p.Steals==captureBefore+1,"new reception lock prevents immediate return capture on touching bodies");
            app.View.Render(state,rules,1,false,false);app.UI.RefreshMatch();yield return Capture("55-direct-held-ball-interception");

            app.GiveTargetBall();state=app.Simulation.State;p=state.Players[0];target=state.Players[1];
            captureBefore=p.Steals;
            typeof(PlayerState).GetProperty(nameof(PlayerState.Position)).SetValue(target,Vec2.Zero);
            typeof(PlayerState).GetProperty(nameof(PlayerState.Facing)).SetValue(target,new Vec2(1,0));
            typeof(PlayerState).GetProperty(nameof(PlayerState.Position)).SetValue(p,ballPosition+new Vec2(2,0));
            SetSmokeBall(nameof(BallState.Position),ballPosition);
            commands[0]=default;for(int tick=0;tick<16;tick++)StepPossessionPresented(commands);
            commands[0]=ContextOrder.Create(state,rules,p.Id,state.Ball.Position,clickedBall:true);
            for(int tick=0;tick<150 && state.Ball.OwnerId!=p.Id;tick++){StepPossessionPresented(commands);commands[0]=default;}
            Check(state.Ball.OwnerId==p.Id && p.Steals==captureBefore+1 && p.TackleCooldown==0 && p.Health==100,
                "right-click ball pursuit steals enemy held football in the actual client without spacebar");
            app.View.Render(state,rules,1,false,false);app.UI.RefreshMatch();yield return Capture("56-right-click-held-ball-capture");
        }

        private IEnumerator CapturePossessionContacts()
        {
            app.SetPaused(true);app.UI.ShowPause(false);app.GiveTargetBall();
            var state=app.Simulation.State;var rules=app.Simulation.Rules;var p=state.Players[0];var target=state.Players[1];
            int captureBefore=p.Steals;
            typeof(PlayerState).GetProperty(nameof(PlayerState.Facing)).SetValue(target,new Vec2(1,0));
            typeof(PlayerState).GetProperty(nameof(PlayerState.Position)).SetValue(p,target.Position+new Vec2(0,p.Stats.Radius+target.Stats.Radius+.15f));
            var commands=new PlayerCommand[2];commands[1]=new PlayerCommand {Aim=new Vec2(1,0)};StepPossessionPresented(commands);
            var originalBall=state.Ball.Position;var originalTarget=target.Position;
            commands[0]=new PlayerCommand {Aim=new Vec2(0,-1),TacklePressed=true};bool dropped=false,earlyCapture=false;
            for(int tick=0;tick<25;tick++)
            {
                StepPossessionPresented(commands);commands[0]=commands[0].WithoutEdges();
                foreach(var e in state.Events){if(e.Kind==MatchEventKind.BallDropped)dropped=true;if(e.Kind==MatchEventKind.Steal||e.Kind==MatchEventKind.Catch)earlyCapture=true;}
            }
            Check(dropped && !earlyCapture && state.Ball.OwnerId==-1 && p.Steals==captureBefore,
                "side push releases football without automatically granting it to pusher");
            Check(Vec2.Distance(state.Ball.Position,originalBall)<.001f && target.Position.Y<originalTarget.Y-.9f,
                "displaced enemy moves while released football remains at exact original spot");
            app.View.Render(state,rules,1,false,false);app.UI.RefreshMatch();yield return Capture("51-side-push-free-ball");
            commands[0]=ContextOrder.Create(state,rules,0,state.Ball.Position,clickedBall:true);
            for(int tick=0;tick<100 && state.Ball.OwnerId<0;tick++){StepPossessionPresented(commands);commands[0]=default;}
            Check(state.Ball.OwnerId==0 && p.Steals==captureBefore+1,"pusher captures free ball only after physically reaching it");
            app.View.Render(state,rules,1,false,false);app.UI.RefreshMatch();yield return Capture("52-contact-pickup");
            app.GiveTargetBall();state=app.Simulation.State;p=state.Players[0];target=state.Players[1];
            typeof(PlayerState).GetProperty(nameof(PlayerState.Facing)).SetValue(target,new Vec2(1,0));
            commands[0]=default;commands[1]=new PlayerCommand {Aim=new Vec2(1,0)};StepPossessionPresented(commands);
            originalBall=state.Ball.Position;originalTarget=target.Position;commands[0]=new PlayerCommand {Aim=new Vec2(1,0),TacklePressed=true};
            for(int tick=0;tick<35;tick++){StepPossessionPresented(commands);commands[0]=commands[0].WithoutEdges();}
            Check(state.Ball.OwnerId==target.Id && target.Position.X>originalTarget.X+.8f &&
                Vec2.Distance(state.Ball.Position-originalBall,target.Position-originalTarget)<.003f,
                "enemy rear push carries football along with displaced carrier");
            app.View.Render(state,rules,1,false,false);app.UI.RefreshMatch();yield return Capture("53-rear-push-carried-ball");
            // 用实际 3v3 同队模型验证队友推，不伪装训练敌人的队伍。
            app.OpenSelection(MatchMode.BotMatch);app.SelectCharacter(CharacterId.Marine);app.StartMatch();app.SetPaused(true);app.UI.ShowPause(false);
            state=app.Simulation.State;rules=app.Simulation.Rules;p=state.Players[0];target=state.Players[1];
            typeof(MatchState).GetProperty(nameof(MatchState.Phase)).SetValue(state,MatchPhase.Playing);
            foreach(var player in state.Players)typeof(PlayerState).GetProperty(nameof(PlayerState.Position)).SetValue(player,new Vec2(-18+player.Id*3,-10));
            typeof(PlayerState).GetProperty(nameof(PlayerState.Position)).SetValue(p,new Vec2(-1.6f,0));
            typeof(PlayerState).GetProperty(nameof(PlayerState.Position)).SetValue(target,Vec2.Zero);
            typeof(PlayerState).GetProperty(nameof(PlayerState.Facing)).SetValue(target,new Vec2(1,0));
            SetSmokeBall(nameof(BallState.OwnerId),target.Id);SetSmokeBall(nameof(BallState.Position),new Vec2(target.Stats.Radius+rules.BallRadius+rules.FootGap,0));
            commands=new PlayerCommand[6];commands[1]=new PlayerCommand {Aim=new Vec2(1,0)};StepPossessionPresented(commands);
            originalBall=state.Ball.Position;originalTarget=target.Position;commands[0]=new PlayerCommand {Aim=new Vec2(1,0),TacklePressed=true};
            for(int tick=0;tick<35;tick++){StepPossessionPresented(commands);commands[0]=commands[0].WithoutEdges();}
            Check(p.Team==target.Team && state.Ball.OwnerId==target.Id && target.Position.X>.8f &&
                Vec2.Distance(state.Ball.Position-originalBall,target.Position-originalTarget)<.003f,
                "real friendly body collision pushes teammate and their football together");
            app.WorldCamera.GetComponent<ArenaCamera>().ResetFraming();app.View.Render(state,rules,1,false,false);app.UI.RefreshMatch();
            yield return Capture("54-friendly-push-carried-ball");
            app.OpenSelection(MatchMode.Training);app.SelectCharacter(CharacterId.Marine);app.StartMatch();app.SetPaused(true);app.UI.ShowPause(false);
            typeof(MatchState).GetProperty(nameof(MatchState.Phase)).SetValue(app.Simulation.State,MatchPhase.Playing);
            app.ResetTraining(false);app.UI.Motion.Reset(app.Simulation.State.Players[0]);app.UI.RefreshMatch();app.SetPaused(false);
        }

        private void StepPossessionPresented(PlayerCommand[] commands)
        {
            app.Simulation.Step(commands);
            foreach(var e in app.Simulation.State.Events) app.HandleEvent(e);
        }

        private IEnumerator CaptureBallLighting()
        {
            app.SetPaused(true);
            var model = app.View.BallModel;
            var before = model.RollingRotation;
            var ground = app.Simulation.State.Ball.Position;
            model.Render(ground+new Vec2(0.2f,0),true);
            Check(Quaternion.Angle(before,model.RollingRotation) > 10,"football rolls through geometry rotation when moving");
            model.Render(ground,false);
            var camera = app.WorldCamera; var position = camera.transform.position; float size = camera.orthographicSize;
            camera.transform.position = model.transform.position-camera.transform.forward*10;
            camera.orthographicSize = 0.95f;
            var hidden = new List<GameObject>();
            foreach (Transform child in app.View.ActorRoot)
                if (child.gameObject.activeSelf && child != model.transform && child.name != "Ball Shadow")
                { hidden.Add(child.gameObject); child.gameObject.SetActive(false); }
            app.UI.Root.style.visibility = Visibility.Hidden;
            yield return Capture("14-football-lit-closeup");
            yield return new WaitForEndOfFrame();
            var lit = ScreenCapture.CaptureScreenshotAsTexture();
            var key = app.View.GetComponent<PremiumArenaView>().KeyLight;
            key.enabled = false;
            yield return Capture("15-football-key-off-closeup");
            yield return new WaitForEndOfFrame();
            var unlit = ScreenCapture.CaptureScreenshotAsTexture();
            var a = lit.GetPixels32(); var b = unlit.GetPixels32(); long delta = 0;
            for (int i=0;i<a.Length;i++) delta += Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);
            Check((double)delta/a.Length > 2,"disabling real key light changes rendered floor and football shading");
            Destroy(lit); Destroy(unlit); key.enabled = true;
            camera.transform.position = position; camera.orthographicSize = size;
            foreach (var go in hidden) go.SetActive(true);
            app.UI.Root.style.visibility = Visibility.Visible;
            app.SetPaused(false);
        }

        private IEnumerator CaptureCharacterModels()
        {
            app.SetPaused(true);
            var camera = app.WorldCamera; var position = camera.transform.position; float size = camera.orthographicSize;
            camera.transform.position = new Vector3(0,.7f,0)-camera.transform.forward*10;
            camera.orthographicSize = 3.0f;
            bool debug = app.ShowDebug; if (debug) app.ToggleDebug();
            app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,false);
            app.UI.Root.style.visibility = Visibility.Hidden;
            yield return Capture("16-character-models-closeup");
            if (debug) app.ToggleDebug();
            camera.transform.position = position; camera.orthographicSize = size;
            app.UI.Root.style.visibility = Visibility.Visible;
            app.SetPaused(false);
        }

        private IEnumerator CaptureHudFeedback()
        {
            app.SetPaused(true); app.UI.ShowPause(false); app.ResetTraining(false); app.UI.RefreshMatch();
            var motion=app.UI.Motion;
            var commands=new PlayerCommand[2];
            int ready=motion.ReadyFeedbackCount,damage=motion.DamageFeedbackCount,resources=motion.ResourceFeedbackCount;
            Check(app.UI.Root.Q("hero-symbol").resolvedStyle.backgroundImage.texture.width>=1024 &&
                Resources.Load<Texture2D>("FrontEnd/skill-bomb-v2").width>=1024,
                "painted portraits and ability art import at high resolution");
            motion.Enter(app.UI.Root.Q("match-page"),true);
            app.UI.RefreshMatch();
            yield return null;
            yield return new WaitForEndOfFrame();
            Check(motion.TransitionRunning && app.UI.Root.Q("battle-dock").resolvedStyle.opacity<1,
                "match HUD enters with an actual fade and slide transition");
            bool debug=app.ShowDebug; if(debug) app.ToggleDebug();
            Directory.CreateDirectory(Path.Combine(output,"hud-motion-frames"));
            for(int frame=0;frame<48;frame++)
            {
                if(frame==7)
                {
                    commands[0]=new PlayerCommand {QPressed=true}; StepPresented(commands); commands[0]=default; app.UI.RefreshMatch();
                    Check(motion.HealthTrailPercent>app.Simulation.State.Players[0].Health && motion.ActiveParticles>0 &&
                        motion.CooldownFraction("q")>.9f && app.View.ActiveFieldParticles>0,
                        "real skill activation emits UI and arena particles, radial cooldown and buffered health loss");
                    Check(app.UI.Root.Q<ProgressBar>("health-progress").value==90,
                        "health number stays authoritative while the loss trail animates");
                }
                if(frame==14)
                {commands[0]=new PlayerCommand {QPressed=true}; StepPresented(commands); commands[0]=default; app.UI.RefreshMatch();}
                if(frame==22)
                {
                    Check(motion.HealthTrailPercent<91 && motion.ResourceFeedbackCount>resources,
                        "resource loss trail converges to current health after its delay");
                    for(int i=0;i<365;i++) StepPresented(commands); app.UI.RefreshMatch();
                    Check(motion.ReadyFeedbackCount>ready && motion.CooldownFraction("q")==0 && motion.ActiveParticles>0,
                        "cooldown completion produces a distinct ready gleam and spark burst");
                }
                if(frame==30)
                {
                    app.GiveTargetBall(); commands[1]=new PlayerCommand {Aim=new Vec2(-1,0),AttackHeld=true};
                    for(int i=0;i<35;i++) StepPresented(commands); commands[1]=default; app.UI.RefreshMatch();
                    Check(motion.DamageFeedbackCount>damage && app.View.ActiveFieldParticles>0,
                        "enemy hit triggers UI damage feedback and short arena impact sparks");
                }
                if(frame==37)
                {
                    commands[1]=new PlayerCommand {Aim=new Vec2(-1,0),AttackHeld=true};
                    for(int i=0;i<180 && app.Simulation.State.Players[0].Health>30;i++) StepPresented(commands);
                    commands[1]=default; app.UI.RefreshMatch();
                    Check(app.UI.Root.Q("battle-dock").ClassListContains("low-health"),
                        "low health enables restrained portrait warning state");
                }
                app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,false);
                yield return new WaitForSecondsRealtime(.055f);
                yield return new WaitForEndOfFrame();
                var screenshot=ScreenCapture.CaptureScreenshotAsTexture();
                int width=Mathf.Min(1040,Screen.width),height=Mathf.Min(365,Screen.height);
                var crop=new Texture2D(width,height,TextureFormat.RGB24,false);
                crop.SetPixels(screenshot.GetPixels((Screen.width-width)/2,0,width,height)); crop.Apply();
                File.WriteAllBytes(Path.Combine(output,"hud-motion-frames",frame.ToString("000")+".png"),crop.EncodeToPNG());
                Destroy(screenshot); Destroy(crop);
            }
            yield return Capture("17-polished-hud-feedback");
            app.View.SetFeedbackEnabled(false); motion.SetEnabled(false);
            Check(motion.ActiveParticles==0 && app.View.ActiveFieldParticles==0,
                "feedback preference clears both bounded UI and arena particle pools");
            app.View.SetFeedbackEnabled(true); motion.SetEnabled(true);
            if(debug) app.ToggleDebug(); app.ResetTraining(false); app.UI.RefreshMatch(); app.SetPaused(false);
        }

        private IEnumerator CaptureCooldownRevision()
        {
            app.SetPaused(true); app.UI.ShowPause(false); app.ResetTraining(true); app.UI.SetFullHud(true,false);
            bool debug=app.ShowDebug; if(debug) app.ToggleDebug();
            var commands=new PlayerCommand[2];
            commands[0]=new PlayerCommand {QPressed=true}; StepPresented(commands);
            commands[0]=default; for(int i=0;i<12;i++) StepPresented(commands);
            commands[0]=new PlayerCommand {EPressed=true}; StepPresented(commands);
            commands[0]=default; for(int i=0;i<12;i++) StepPresented(commands);
            commands[0]=new PlayerCommand {Aim=new Vec2(1,0),KickPressed=true,KickHeld=true}; StepPresented(commands);
            commands[0]=new PlayerCommand {Aim=new Vec2(1,0),KickReleased=true}; StepPresented(commands);
            commands[0]=default; for(int i=0;i<12;i++) StepPresented(commands);
            commands[0]=new PlayerCommand {Aim=new Vec2(0,1),TacklePressed=true}; StepPresented(commands);
            commands[0]=default; for(int i=0;i<60;i++) StepPresented(commands);
            app.UI.RefreshMatch(); app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,false);
            yield return null; yield return new WaitForEndOfFrame();
            var dock=app.UI.Root.Q("battle-dock").worldBound;
            Check(Math.Abs(dock.center.x-app.UI.Root.worldBound.center.x)<1,"portrait and all six actions form a centered full HUD");
            foreach(var id in new[]{"q","e","kick","tackle"})
            {
                var icon=app.UI.Root.Q(id+"-symbol").resolvedStyle.backgroundImage.texture;
                Check(icon!=null && icon.name.EndsWith("-gray") && app.UI.Motion.IconIsGrayscale(id) && app.UI.Motion.CooldownRingVisible(id),id+": real cooldown displays grayscale art and a visible circular track");
                Check(app.UI.Motion.CooldownProgress(id)>.1f && app.UI.Motion.CooldownProgress(id)<.5f && app.UI.Root.Q<Label>(id+"-cd").text.Contains("."),id+": ring advances clockwise with elapsed cooldown and remaining tenths");
            }
            foreach(var view in app.View.GetComponentsInChildren<PlayerView>())
                Check(view.ResourcesUI.Visible && PlayerResourceView.ResourceRows==2 && view.transform.Find("Attack Stock 0")==null && view.transform.Find("Charge Bar")==null,"field player has only the rounded health and energy pair");
            yield return Capture("45-centered-hud-cooling");
            float progress=app.UI.Motion.CooldownProgress("q"); string seconds=app.UI.Root.Q<Label>("q-cd").text;
            yield return new WaitForSecondsRealtime(.25f);
            Check(app.UI.Motion.CooldownProgress("q")==progress && app.UI.Root.Q<Label>("q-cd").text==seconds,"paused cooldown ring and remaining seconds stay frozen");
            for(int i=0;i<180;i++) StepPresented(commands); app.UI.RefreshMatch();
            Check(app.UI.Motion.CooldownProgress("q")>progress+.45f,"circular cooldown covers more of its single revolution as real time elapses");
            yield return Capture("46-cooldown-progress");
            for(int i=0;i<230;i++) StepPresented(commands); app.UI.RefreshMatch();
            Check(app.UI.Motion.CooldownProgress("e")==1 && app.UI.Motion.CooldownRingVisible("e") && !app.UI.Motion.IconIsGrayscale("e"),"ready ability completes exactly one revolution and restores its colour");
            yield return null; yield return new WaitForEndOfFrame();
            Check(app.UI.Root.Q("e-symbol").resolvedStyle.backgroundImage.texture.name=="skill-bomb-v2" && app.UI.Root.Q<Label>("e-cd").text=="","ready ability clears its timer and uses its original coloured artwork");
            yield return Capture("47-cooldown-ready");
            app.ResetTraining(false); if(debug) app.ToggleDebug(); app.SetPaused(false); app.UI.RefreshMatch();
        }

        private IEnumerator CaptureMatchPeriods()
        {
            app.SetPaused(true); app.UI.ShowPause(false);
            var state=app.Simulation.State;
            Check(state.Half==1 && app.UI.Root.Q<Label>("mode-label").text.Contains("上半场") && state.PeriodRemainingSeconds(app.Simulation.Rules)<=150,"match starts with a first-half label and period clock");
            typeof(MatchState).GetProperty(nameof(MatchState.RemainingSeconds)).SetValue(state,150.001f);
            var commands=new PlayerCommand[state.Players.Count]; StepPresented(commands); app.UI.RefreshMatch();
            yield return null; yield return new WaitForEndOfFrame();
            Check(state.Half==2 && state.Phase==MatchPhase.Countdown && app.UI.Root.Q<Label>("mode-label").text.Contains("下半场") && app.UI.Root.Q<Label>("clock-label").text=="02:30","half-time restarts with a second-half label and fresh period clock");
            yield return Capture("48-second-half-kickoff");
            for(int i=0;i<125 && state.Phase!=MatchPhase.Playing;i++) StepPresented(commands);
            Check(state.Phase==MatchPhase.Playing && !state.Overtime,"second-half kickoff finishes before play resumes");
            app.UI.RefreshMatch();
            app.SetPaused(false);
        }

        private IEnumerator CaptureHudModes()
        {
            app.SetPaused(true); app.UI.ShowPause(false); app.ResetTraining(false); app.UI.RefreshMatch();
            bool debug=app.ShowDebug; if(debug) app.ToggleDebug();
            yield return new WaitForSecondsRealtime(.55f);
            Check(app.UI.Root.Q("dock-backdrop").resolvedStyle.opacity>.5f && app.UI.Root.Q("dock-backdrop").resolvedStyle.opacity<.7f &&
                app.UI.Root.Q("hero-symbol").resolvedStyle.opacity==1 && app.UI.Root.Q("battle-dock").resolvedStyle.opacity>.99f,
                "only the full dock backdrop is translucent; portraits and content stay legible");
            app.GiveTargetBall();
            var commands=new PlayerCommand[2];
            commands[1]=new PlayerCommand {Aim=new Vec2(-1,0),AttackHeld=true};
            for(int i=0;i<35;i++) StepPresented(commands);
            commands[1]=default; app.UI.RefreshMatch();
            var p=app.Simulation.State.Players[0];
            Check(p.Health<100 && app.UI.Root.Q("health-delta")==null &&
                !app.UI.Root.Q<Label>("combat-toast").text.Contains("伤害"),
                "enemy damage changes health without a bottom damage number or toast");
            app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,false);
            yield return Capture("18-translucent-full-hud");
            Check(PicksButton("hud-mode-toggle"), "in-match HUD toggle receives pointer hit");
            yield return Click("hud-mode-toggle");
            yield return new WaitForSecondsRealtime(.55f);
            Check(!app.UI.FullHudEnabled && !GameUI.LoadFullHudPreference() &&
                app.UI.Root.Q("battle-dock").resolvedStyle.display==DisplayStyle.None &&
                app.UI.Root.Q("compact-player-hud").resolvedStyle.display!=DisplayStyle.None &&
                app.UI.Root.Q("combat-toast").resolvedStyle.display==DisplayStyle.None &&
                app.UI.Root.Q("feedback-label").resolvedStyle.display==DisplayStyle.None,
                "match toggle persists compact mode and removes the lower dock and helper overlays");
            commands[0]=new PlayerCommand {QPressed=true}; StepPresented(commands); commands[0]=default; app.UI.RefreshMatch();
            Check(app.UI.Root.Q<Label>("compact-health").text==$"生命 {p.Health:0} / 100" &&
                app.UI.Root.Q<Label>("compact-energy").text==$"能量 {p.Energy:0} / 100" &&
                app.UI.Root.Q("compact-hero-symbol").resolvedStyle.backgroundImage.texture.name=="portrait-marine-v2" &&
                app.UI.Motion.ActiveParticles==0,
                "compact HUD shows live health and energy beside matching portrait without hidden skill particles");
            app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,false);
            yield return Capture("19-compact-player-hud");
            yield return Click("hud-mode-toggle");
            yield return new WaitForSecondsRealtime(.55f);
            Check(app.UI.FullHudEnabled && GameUI.LoadFullHudPreference() &&
                app.UI.Root.Q("battle-dock").resolvedStyle.display!=DisplayStyle.None &&
                app.UI.Root.Q("compact-player-hud").resolvedStyle.display==DisplayStyle.None &&
                app.UI.Root.Q<ProgressBar>("health-progress").value==p.Health,
                "switching back restores full HUD with current health and cooldowns");
            if(debug) app.ToggleDebug(); app.ResetTraining(false); app.UI.RefreshMatch(); app.SetPaused(false);
        }

        private IEnumerator CaptureLowHealthWarning()
        {
            app.SetPaused(true); app.UI.ShowPause(false); app.ResetTraining(false); app.UI.RefreshMatch();
            bool debug=app.ShowDebug; if(debug) app.ToggleDebug();
            var p=app.Simulation.State.Players[0];
            var warning=app.UI.Root.Q("low-health-vignette");
            yield return new WaitForSecondsRealtime(.3f);
            yield return new WaitForEndOfFrame();
            var healthy=ScreenCapture.CaptureScreenshotAsTexture();
            Color healthyEdge=healthy.GetPixel(5,Screen.height/2),healthyCenter=healthy.GetPixel(Screen.width/2,Screen.height/2);
            Destroy(healthy);
            SetSmokeHealth(p,p.Stats.MaxHealth*.25f); app.UI.RefreshMatch();
            yield return new WaitForSecondsRealtime(.3f);
            Check(warning.resolvedStyle.opacity==0,"25 percent health does not trigger the low-health screen rim");
            SetSmokeHealth(p,p.Stats.MaxHealth*.24f); app.UI.RefreshMatch();
            yield return new WaitForSecondsRealtime(.3f);
            Check(warning.resolvedStyle.opacity>.4f && warning.resolvedStyle.opacity<.5f,
                "health below 25 percent fades in a persistent translucent red screen rim");
            yield return Capture("21-low-health-full-hud");
            yield return new WaitForEndOfFrame();
            var low=ScreenCapture.CaptureScreenshotAsTexture();
            Color lowEdge=low.GetPixel(5,Screen.height/2),lowCenter=low.GetPixel(Screen.width/2,Screen.height/2);
            Check(lowEdge.r-lowEdge.g>healthyEdge.r-healthyEdge.g+.02f && lowEdge.g<healthyEdge.g-.02f &&
                Mathf.Abs(lowCenter.r-healthyCenter.r)+Mathf.Abs(lowCenter.g-healthyCenter.g)+Mathf.Abs(lowCenter.b-healthyCenter.b)<.015f,
                "actual rendering tints the outside edge red while leaving the center clear");
            Destroy(low);
            app.UI.SetFullHud(false,false);
            app.UI.Motion.SetEnabled(false);
            yield return new WaitForSecondsRealtime(.55f);
            Check(warning.resolvedStyle.opacity>.4f && app.UI.Root.Q("battle-dock").resolvedStyle.display==DisplayStyle.None,
                "persistent low-health rim remains available in compact mode and with hit effects disabled");
            yield return Capture("22-low-health-compact-hud");
            app.UI.Motion.SetEnabled(true);
            SetSmokeHealth(p,p.Stats.MaxHealth*.25f); app.UI.RefreshMatch();
            yield return new WaitForSecondsRealtime(.3f);
            Check(warning.resolvedStyle.opacity==0,"healing to 25 percent fades out the low-health rim");
            app.GiveTargetBall(); p=app.Simulation.State.Players[0];
            SetSmokeHealth(p,p.Stats.MaxHealth*.24f); app.UI.RefreshMatch();
            yield return new WaitForSecondsRealtime(.3f);
            var commands=new PlayerCommand[2]; commands[1]=new PlayerCommand {Aim=new Vec2(-1,0),AttackHeld=true};
            for(int i=0;i<35 && !p.IsDead;i++) StepPresented(commands);
            app.UI.RefreshMatch();
            yield return new WaitForSecondsRealtime(.35f);
            Check(p.IsDead && warning.resolvedStyle.opacity==0,"knockout fades out the persistent rim during the respawn countdown");
            commands[1]=default;
            for(int i=0;i<310;i++) StepPresented(commands);
            app.UI.RefreshMatch();
            yield return new WaitForSecondsRealtime(.3f);
            Check(!p.IsDead && p.Health==p.Stats.MaxHealth && warning.resolvedStyle.opacity==0,
                "full-health revival leaves the persistent screen rim cleared");
            app.UI.SetFullHud(true,false);
            if(debug) app.ToggleDebug(); app.ResetTraining(false); app.UI.RefreshMatch(); app.SetPaused(false);
        }

        private IEnumerator CaptureDynamicStadium()
        {
            var stadium=app.View.GetComponent<PremiumArenaView>().Dynamics;
            Check(stadium.Turf.sharedMaterial.shader.name=="Soccer/Living Turf" && stadium.Turf.receiveShadows &&
                stadium.Turf.sharedMaterial.mainTexture!=null,
                "playing surface combines HD detail with procedural lit grass and wind geometry");
            Check(stadium.CrowdCount==48 && stadium.FlagCount==8 && stadium.DroneCount==2 &&
                stadium.GetComponentsInChildren<Collider>().Length==0,
                "tiered stands include 48 spectators, 8 cloth flags and 2 drones without gameplay colliders");
            Check(Resources.Load<Shader>("SceneSurface/LivingTurf").isSupported &&
                Resources.Load<Shader>("SceneSurface/ArenaWind").isSupported && Resources.Load<Shader>("SceneSurface/StadiumLED").isSupported,
                "turf, pinned wind cloth and scrolling LED shaders work in the actual player");
            app.ResetTraining(false); app.UI.RefreshMatch();
            app.SetPaused(true); app.UI.ShowPause(false);
            float clock=stadium.PresentationTime; Vector3 drone=stadium.FirstDrone.localPosition;
            yield return new WaitForSecondsRealtime(.20f);
            Check(stadium.PresentationTime==clock && stadium.FirstDrone.localPosition==drone,
                "pausing freezes stadium presentation time and hovering assets");
            bool debug=app.ShowDebug; if(debug) app.ToggleDebug();
            bool full=app.UI.FullHudEnabled; app.UI.SetFullHud(false,false);
            var camera=app.WorldCamera; Vector3 cameraPosition=camera.transform.position; float size=camera.orthographicSize;
            camera.transform.position=new Vector3(-app.Simulation.Rules.FieldWidth*.375f,.5f,app.Simulation.Rules.FieldHeight*.5f+1.4f)-camera.transform.forward*10; camera.orthographicSize=1.6f;
            app.UI.Root.style.visibility=Visibility.Hidden;
            yield return Capture("23-3d-stadium-closeup");
            camera.transform.position=cameraPosition; camera.orthographicSize=size;
            app.UI.Root.style.visibility=Visibility.Visible;
            app.SetPaused(false);
            Directory.CreateDirectory(Path.Combine(output,"stadium-motion-frames"));
            bool sawDeformedNet=false;
            for(int frame=0;frame<36;frame++)
            {
                if(frame==12)
                {
                    app.SetPaused(true); app.UI.ShowPause(false);
                    var ball=app.Simulation.State.Ball;
                    SetSmokeBall(nameof(BallState.OwnerId),-1); SetSmokeBall(nameof(BallState.LastKickerId),0);
                    SetSmokeBall(nameof(BallState.Position),new Vec2(app.Simulation.Rules.FieldWidth/2-.1f,0));
                    SetSmokeBall(nameof(BallState.PreviousPosition),ball.Position); SetSmokeBall(nameof(BallState.Velocity),new Vec2(18,0));
                    var commands=new PlayerCommand[2]; bool goal=false;
                    for(int i=0;i<12 && !goal;i++)
                    { StepPresented(commands); foreach(var e in app.Simulation.State.Events) if(e.Kind==MatchEventKind.Goal) goal=true; }
                    app.UI.RefreshMatch();
                    Check(goal && stadium.GoalReactionRemaining>2 && stadium.ConfettiCount>0 && stadium.ConfettiCount<=96,
                        "a real planar goal starts stadium cheers, net reaction, LED celebration and bounded confetti");
                    app.SetPaused(false);
                }
                yield return new WaitForSecondsRealtime(.10f);
                if(stadium.GoalReactionRemaining>0 && stadium.NetDeflection>.01f) sawDeformedNet=true;
                yield return new WaitForEndOfFrame();
                var screenshot=ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(output,"stadium-motion-frames",frame.ToString("000")+".png"),screenshot.EncodeToPNG());
                Destroy(screenshot);
            }
            Check(stadium.PresentationTime>clock+2 && Vector3.Distance(stadium.FirstDrone.localPosition,drone)>.05f &&
                stadium.Turf.sharedMaterial.GetFloat("_ArenaTime")>clock,
                "live match advances actual transforms and wind/turf/LED shader time");
            Check(sawDeformedNet,"goal reaction visibly deforms the 3D goal net");
            app.ResetTraining(false); app.UI.RefreshMatch();
            Check(stadium.GoalReactionRemaining==0 && stadium.ConfettiCount==0,
                "training reset clears stadium celebration and particle state");
            yield return Capture("24-living-stadium-wide");
            app.UI.SetFullHud(full,false); if(debug) app.ToggleDebug();
        }

        private void SetSmokeBall(string property,object value) => typeof(BallState).GetProperty(property).SetValue(app.Simulation.State.Ball,value);

        // 仅显式烟测准备边界状态，避免给正式 Core 增加任意改血量的入口。
        private static void SetSmokeHealth(PlayerState player,float health) =>
            typeof(PlayerState).GetProperty(nameof(PlayerState.Health)).SetValue(player,health);

        private void StepPresented(PlayerCommand[] commands)
        {
            app.Simulation.Step(commands);
            foreach (var e in app.Simulation.State.Events)
            { app.View.PlayEvent(e, app.Simulation.State); app.Audio.PlayEvent(e, app.Simulation.State); app.UI.PlayEvent(e); }
        }

        private IEnumerator CaptureKickoffPresentation()
        {
            yield return new WaitForSecondsRealtime(.14f);
            var overlay=app.UI.Root.Q("countdown-overlay");
            var number=app.UI.Root.Q<Label>("countdown-number");
            Check(app.Simulation.State.Phase==MatchPhase.Countdown && number.text=="3" && app.UI.Motion.CountdownVisible,
                "actual opening countdown displays three from Core time");
            Vector3 ballScreen=app.WorldCamera.WorldToScreenPoint(ArenaProjection.ToWorld(app.Simulation.State.Ball.Position));
            Vector2 ballPanel=RuntimePanelUtils.ScreenToPanel(app.UI.Root.panel,new Vector2(ballScreen.x,Screen.height-ballScreen.y));
            Check(Mathf.Abs(overlay.worldBound.center.x-ballPanel.x)<2,
                "countdown aligns horizontally with the protected football instead of the local player");
            Check(overlay.resolvedStyle.backgroundColor.a==0 && overlay.worldBound.width<Screen.width*.25f &&
                app.UI.Root.Q("phase-label").resolvedStyle.display==DisplayStyle.None,
                "kickoff uses a small transparent dial without the old opaque countdown card");
            yield return Capture("25-kickoff-three");
            app.SetPaused(true); app.UI.ShowPause(false);
            Check(app.View.OpeningShield.Visible && Resources.Load<Shader>("SceneSurface/OpeningShield").isSupported &&
                app.View.OpeningShield.GetComponentsInChildren<Collider>().Length==0,
                "opening football has a real transparent dome and Core owns its blocking");
            var player=app.Simulation.State.Players[0]; var origin=player.Position;
            var warmup=new PlayerCommand[app.Simulation.State.Players.Count];
            warmup[0]=new PlayerCommand {Move=new Vec2(1,0),Aim=new Vec2(0,1),QPressed=true,TacklePressed=true};
            for(int i=0;i<38;i++) {StepPresented(warmup); warmup[0]=warmup[0].WithoutEdges();}
            app.UI.RefreshMatch(); app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,false);
            Check(Vec2.Distance(origin,player.Position)>.25f &&
                Vec2.Distance(player.Position,app.Simulation.State.Ball.Position)>=app.Simulation.Rules.OpeningShieldRadius+player.Stats.Radius-.001f &&
                app.Simulation.State.Ball.OwnerId==-1 && player.Health==100 && player.Energy==100 && player.QCooldown==0 && player.TackleCooldown==0,
                "actual opening commands move and aim against the shield without picking up or spending resources");
            var center=GameObject.Find("Center Circle").GetComponent<LineRenderer>();
            Check(Mathf.Abs(ArenaProjection.ToGround(center.GetPosition(0)).Length-app.Simulation.Rules.OpeningShieldRadius)<.001f &&
                Mathf.Abs(app.View.OpeningShield.GetComponentInChildren<MeshRenderer>().transform.localScale.x-app.Simulation.Rules.CenterCircleRadius*2)<.001f,
                "center circle, visible shield diameter and blocking radius use exactly the same rule");
            app.ResetTraining(true); app.GiveTargetBall();
            Check(app.Simulation.State.IsOpeningWarmup && app.Simulation.State.Ball.OwnerId==-1,
                "training shortcuts cannot bypass the opening football protection");
            yield return Capture("27-warmup-shield");
            float remaining=app.Simulation.State.PhaseRemaining, fraction=app.UI.Motion.CountdownFraction;
            var scale=number.resolvedStyle.scale;
            yield return new WaitForSecondsRealtime(.22f);
            Check(app.Simulation.State.PhaseRemaining==remaining && app.UI.Motion.CountdownFraction==fraction &&
                number.resolvedStyle.scale.value==scale.value,"pause freezes both countdown time and number pulse");
            app.UI.SetFullHud(false,false);
            yield return null;
            Check(app.UI.Motion.CountdownVisible && number.text=="3" && PicksButton("hud-mode-toggle"),
                "transparent countdown remains visible in compact HUD and leaves controls clickable");
            app.UI.SetFullHud(true,false); app.SetPaused(false);
            string folder=Path.Combine(output,"kickoff-motion-frames"); Directory.CreateDirectory(folder);
            var seen=new HashSet<string>(); int frame=0;
            float deadline=Time.realtimeSinceStartup+9;
            while(Time.realtimeSinceStartup<deadline && (app.Simulation.State.Phase==MatchPhase.Countdown || app.UI.Motion.CountdownVisible))
            {
                seen.Add(number.text);
                yield return Capture("kickoff-motion-frames/"+(frame++).ToString("000"));
                yield return new WaitForSecondsRealtime(.085f);
            }
            Check(seen.Contains("3") && seen.Contains("2") && seen.Contains("1") && seen.Contains("开球！"),
                "native frames show each real countdown beat and kickoff transition");
            Check(app.Simulation.State.Phase==MatchPhase.Playing && !app.UI.Motion.CountdownVisible &&
                overlay.resolvedStyle.display==DisplayStyle.None,"kickoff effect automatically clears for live play");
            Check(!app.View.OpeningShield.Visible,"opening dome disappears when live football becomes available");
            var image=ScreenCapture.CaptureScreenshotAsTexture();
            float left=GrassLuminance(image,new Vec2(-8,-3)),right=GrassLuminance(image,new Vec2(8,-3));
            Check(Mathf.Max(left,right)<.75f && Mathf.Max(left,right)/Mathf.Max(.01f,Mathf.Min(left,right))<1.20f,
                "native grass pixels remain matte and evenly readable across former glare region");
            Destroy(image);
            yield return Capture("26-matte-field");
        }

        private IEnumerator CaptureLargeField()
        {
            app.ResetTraining(false); app.UI.SetFullHud(true,false); app.SetPaused(false);
            bool debug=app.ShowDebug; if(debug) app.ToggleDebug();
            yield return new WaitForSecondsRealtime(.3f);
            var camera=app.WorldCamera; var tracking=camera.GetComponent<ArenaCamera>(); var rules=app.Simulation.Rules;
            Check(rules.FieldWidth==56 && rules.FieldHeight==34 && tracking.VisibleGroundSize.x<rules.FieldWidth &&
                tracking.VisibleGroundSize.y<rules.FieldHeight,"actual larger field requires scrolling in both dimensions");
            Check(camera.rect==new Rect(0,0,1,1),"full HUD overlays a full-window playing field without a bottom strip");
            Vector3 floor=app.View.GetComponent<PremiumArenaView>().Floor.transform.localScale;
            Check(floor.x>=rules.FieldWidth+6 && floor.y>=rules.FieldHeight+6,
                "artwork ground extends past the enlarged pitch and camera edges");
            yield return Capture("30-large-field-center");
            app.UI.SetFullHud(false,false); yield return null;
            Check(camera.rect.height==1 && tracking.VisibleGroundSize.x<rules.FieldWidth,
                "compact HUD retains the same full-window local field view");
            var start=tracking.FocusGround;
            var commands=new PlayerCommand[app.Simulation.State.Players.Count];
            commands[0]=new PlayerCommand {Move=new Vec2(1,1),Aim=new Vec2(1,0)};
            string folder=Path.Combine(output,"camera-motion-frames"); Directory.CreateDirectory(folder);
            float largestFocusStep=0; Vector2 previous=start;
            for(int frame=0;frame<55;frame++)
            {
                for(int tick=0;tick<20;tick++) StepPresented(commands);
                app.UI.RefreshMatch();
                yield return new WaitForSecondsRealtime(.045f);
                largestFocusStep=Mathf.Max(largestFocusStep,Vector2.Distance(previous,tracking.FocusGround)); previous=tracking.FocusGround;
                yield return Capture("camera-motion-frames/"+frame.ToString("000"));
            }
            yield return new WaitForSecondsRealtime(.4f);
            var player=app.Simulation.State.Players[0];
            Check(player.Position.X>20 && player.Position.Y>12 && Vector2.Distance(start,tracking.FocusGround)>12,
                "real movement commands traverse the larger map and move the follow camera");
            Check(largestFocusStep<1.2f*rules.MovementSpeedScale,"camera movement stays smooth while traveling between map sections");
            var local=camera.WorldToViewportPoint(ArenaProjection.ToWorld(player.Position));
            Check(local.x>.05f && local.x<.95f && local.y>.05f && local.y<.95f,
                "boundary clamping keeps the local player inside the camera near the far corner");
            Vector2 extent=tracking.VisibleGroundSize*.5f;
            Check(Mathf.Abs(tracking.FocusGround.x)+extent.x<=rules.FieldWidth*.5f+3.001f &&
                Mathf.Abs(tracking.FocusGround.y)+extent.y<=rules.FieldHeight*.5f+3.001f,
                "camera clamp limits views to the pitch and dressed stadium margin");
            Check(app.UI.BallLocatorVisible && app.UI.Root.Q("offscreen-ball").pickingMode==PickingMode.Ignore,
                "football outside the local view gets a direction and distance cue that does not intercept aim");
            Check(player.Position.X>rules.FieldWidth*.5f-1 && player.Position.Y>rules.FieldHeight*.5f-1,
                "movement reaches the newly enlarged 56 by 34 far corner");
            var stadium=app.View.GetComponent<PremiumArenaView>().Dynamics;
            Check(stadium.FootprintCount>0 && stadium.FootprintCount<=16,"live grass records bounded fading footstep compression");
            yield return Capture("31-large-field-far-corner");
            app.SetPaused(true); app.UI.ShowPause(false); var frozen=tracking.FocusGround;
            var marks=stadium.Turf.sharedMaterial.GetVectorArray("_Footprints");
            yield return new WaitForSecondsRealtime(.25f);
            Check(tracking.FocusGround==frozen,"pausing freezes follow camera motion");
            Check(marks[0]==stadium.Turf.sharedMaterial.GetVectorArray("_Footprints")[0],"pausing freezes turf footprint fading");
            app.ResetTraining(false); app.UI.RefreshMatch(); yield return null;
            Check(Vector2.Distance(frozen,tracking.FocusGround)>12 && !app.UI.BallLocatorVisible,
                "training reset snaps the camera back and clears the offscreen football cue");
            Check(stadium.FootprintCount==0,"training reset clears decorative footstep marks");
            app.UI.SetFullHud(true,false); if(debug) app.ToggleDebug(); app.SetPaused(false);
        }

        private void CheckMiniMapLayout(string context)
        {
            var map=app.UI.Root.Q("minimap"); Rect bounds=map.worldBound, rootBounds=app.UI.Root.worldBound;
            Check(app.UI.MiniMap.Visible && bounds.width>150 && bounds.height>100 &&
                bounds.xMin>=rootBounds.xMin && bounds.xMax<=rootBounds.xMax &&
                bounds.yMin>=rootBounds.yMin && bounds.yMax<=rootBounds.yMax,
                context+": minimap remains visible and fits the bottom-left corner");
            if (app.UI.FullHudEnabled)
            {
                Check(!bounds.Overlaps(app.UI.Root.Q("battle-dock").worldBound),context+": minimap does not overlap the skill dock");
                Check(app.WorldCamera.rect==new Rect(0,0,1,1),context+": minimap overlays the full-window playing field");
            }
            bool interactive=map.pickingMode==PickingMode.Position;
            foreach (var item in map.Query<VisualElement>().ToList()) if(item!=map)interactive &= item.pickingMode==PickingMode.Ignore;
            var point=app.UI.Root.Q("minimap-player-0").worldBound.center;
            var picked=app.UI.Root.panel.Pick(point);bool hitsMap=false;
            while(picked!=null){hitsMap |= picked==map;picked=picked.parent;}
            Check(interactive && hitsMap && app.UI.MiniMap.Contains(point),context+": map captures observation clicks without interactive child portraits");
        }

        private void CheckMiniMapRoster(string context)
        {
            Check(app.UI.MiniMap.ActivePortraitCount==app.Simulation.State.Players.Count,context+": every player has an active portrait");
            Vector2 origin=app.UI.Root.Q("minimap-pitch").worldBound.position;
            foreach (var player in app.Simulation.State.Players)
            {
                var marker=app.UI.Root.Q("minimap-player-"+player.Id);
                var image=app.UI.Root.Q("minimap-portrait-"+player.Id).resolvedStyle.backgroundImage.texture;
                string expected=player.Stats.Id==CharacterId.Marine ? "portrait-marine-v2" : "portrait-thor-v2";
                // UI Toolkit 把布局对齐到实体像素；比较屏幕像素误差而非未经对齐的理论子像素。
                float pixelError=Vector2.Distance(marker.worldBound.center-origin,app.UI.MiniMap.WorldToMap(player.Position))*
                    Screen.width/app.UI.Root.worldBound.width;
                Debug.Log($"SOCCER_MINIMAP {context} id={player.Id} portrait={image?.name} expected={expected} pixelError={pixelError:0.000}");
                Check(marker.resolvedStyle.display==DisplayStyle.Flex && image!=null && image.name==expected &&
                    pixelError<=1 &&
                    marker.ClassListContains("minimap-ally")== (player.Team==app.Simulation.State.Players[0].Team),
                    context+": player "+player.Id+" uses its actual hero portrait, team color and current world position");
            }
            Check(app.UI.Root.Q("minimap-player-0").ClassListContains("minimap-local"),context+": local portrait has its own highlight");
        }

        [Serializable] private sealed class MiniMapBoundsEvidence
        {
            public string screenshot;
            public int screenWidth, screenHeight;
            public float[] boundsPixels;
            public int portraits;
        }
        private void WriteMiniMapBounds(string screenshot)
        {
            Rect bounds=app.UI.Root.Q("minimap").worldBound;
            float scale=Screen.width/app.UI.Root.worldBound.width;
            File.WriteAllText(Path.Combine(output,screenshot+"-bounds.json"),JsonUtility.ToJson(new MiniMapBoundsEvidence
            {
                screenshot=screenshot+".png",screenWidth=Screen.width,screenHeight=Screen.height,
                boundsPixels=new[]{bounds.x*scale,bounds.y*scale,bounds.width*scale,bounds.height*scale},
                portraits=app.UI.MiniMap.ActivePortraitCount
            },true));
        }

        private IEnumerator CaptureMiniMapTraining()
        {
            bool full=app.UI.FullHudEnabled;
            app.SetPaused(true); app.UI.ShowPause(false); app.UI.SetFullHud(true,false);
            app.ResetTraining(false); app.UI.RefreshMatch();
            float entryDeadline=Time.realtimeSinceStartup+1;
            while(app.UI.Motion.TransitionRunning && Time.realtimeSinceStartup<entryDeadline) yield return null;
            Check(!app.UI.Motion.TransitionRunning,"new match entry animation settles before testing paused map positions");
            yield return null; yield return new WaitForEndOfFrame();
            CheckMiniMapLayout("training full HUD"); CheckMiniMapRoster("training");
            var mini=app.UI.MiniMap;
            Vector2 start=app.UI.Root.Q("minimap-player-0").worldBound.center;
            Vector2 enemyStart=app.UI.Root.Q("minimap-player-1").worldBound.center;
            var commands=new PlayerCommand[2];
            commands[0]=new PlayerCommand {Move=new Vec2(-1,1),Aim=new Vec2(1,0)};
            commands[1]=new PlayerCommand {Move=new Vec2(1,-1),Aim=new Vec2(-1,0)};
            for (int i=0;i<180;i++) StepPresented(commands);
            app.WorldCamera.GetComponent<ArenaCamera>().ResetFraming(); app.UI.RefreshMatch();
            yield return null; yield return new WaitForEndOfFrame();
            Vector2 moved=app.UI.Root.Q("minimap-player-0").worldBound.center;
            Vector2 enemyMoved=app.UI.Root.Q("minimap-player-1").worldBound.center;
            Check(moved.x<start.x-10 && moved.y<start.y-10 && enemyMoved.x>enemyStart.x+10 && enemyMoved.y>enemyStart.y+10,
                "real opposing movement commands update both portraits with screen-aligned north and south");
            CheckMiniMapRoster("moving training");
            var tracking=app.WorldCamera.GetComponent<ArenaCamera>();
            Vector2 focus=mini.WorldToMap(new Vec2(tracking.FocusGround.x,tracking.FocusGround.y));
            Check(Vector2.Distance(mini.CameraBounds.center,focus)<.1f && mini.CameraBounds.width>0 && mini.CameraBounds.height>0,
                "minimap viewport rectangle uses the actual follow-camera ground footprint");
            long tick=app.Simulation.State.Tick; Rect cameraBounds=mini.CameraBounds;
            yield return new WaitForSecondsRealtime(.2f);
            var portraitAfter=app.UI.Root.Q("minimap-player-0").worldBound.center;
            Debug.Log($"SOCCER_PAUSE_MAP paused={app.Paused} tick={tick}/{app.Simulation.State.Tick} portraitBefore={moved} portraitAfter={portraitAfter} cameraBefore={cameraBounds} cameraAfter={mini.CameraBounds}");
            Check(app.Simulation.State.Tick==tick && Vector2.Distance(moved,portraitAfter)<.01f &&
                mini.CameraBounds==cameraBounds,
                "pause freezes portrait positions and camera footprint");
            yield return Capture("42-minimap-training-movement");
            app.UI.SetFullHud(false,false); yield return null;
            CheckMiniMapLayout("training compact HUD");
            yield return Capture("43-minimap-compact"); WriteMiniMapBounds("43-minimap-compact");
            app.ResetTraining(true); app.UI.RefreshMatch(); yield return null; yield return new WaitForEndOfFrame();
            Check(app.UI.Root.Q("minimap-possession-0").resolvedStyle.display==DisplayStyle.Flex &&
                Vector2.Distance(mini.BallPosition,mini.WorldToMap(app.Simulation.State.Ball.Position))<.01f,
                "training reset immediately shows the correct possession badge and football ground location");
            commands[0]=new PlayerCommand {Aim=new Vec2(1,0),KickPressed=true,KickHeld=true};commands[1]=default;
            StepPresented(commands); commands[0]=commands[0].WithoutEdges();
            for(int i=0;i<20;i++) StepPresented(commands);
            commands[0]=new PlayerCommand {Aim=new Vec2(1,0),KickReleased=true}; StepPresented(commands); commands[0]=default;
            for(int i=0;i<20;i++) StepPresented(commands);
            app.UI.RefreshMatch(); yield return null; yield return new WaitForEndOfFrame();
            Check(app.Simulation.State.Ball.OwnerId<0 && app.UI.Root.Q("minimap-possession-0").resolvedStyle.display==DisplayStyle.None &&
                Vector2.Distance(mini.BallPosition,mini.WorldToMap(app.Simulation.State.Ball.Position))<.01f,
                "a real kick clears possession and moves the free football marker using simulation coordinates");
            app.ResetTraining(false); app.UI.RefreshMatch(); app.UI.SetFullHud(full,false); app.SetPaused(false);
        }

        private IEnumerator CaptureMiniMapMatch()
        {
            bool full=app.UI.FullHudEnabled;
            app.SetPaused(true); app.UI.ShowPause(false); app.UI.SetFullHud(true,false);
            var state=app.Simulation.State;
            // 只在显式烟测中准备分散站位；随后两队的位置都由真实 Core 输入更新。
            Vec2[] positions={new Vec2(-20,-9),new Vec2(-8,8),new Vec2(-2,-11),new Vec2(7,11),new Vec2(19,-4),new Vec2(23,10)};
            for(int i=0;i<state.Players.Count;i++)
            {
                var player=state.Players[i];
                typeof(PlayerState).GetProperty(nameof(PlayerState.Position)).SetValue(player,positions[i]);
                typeof(PlayerState).GetProperty(nameof(PlayerState.PreviousPosition)).SetValue(player,positions[i]);
                typeof(PlayerState).GetProperty(nameof(PlayerState.Velocity)).SetValue(player,Vec2.Zero);
            }
            SetSmokeBall(nameof(BallState.OwnerId),-1); SetSmokeBall(nameof(BallState.Position),new Vec2(2,3));
            SetSmokeBall(nameof(BallState.PreviousPosition),state.Ball.Position); SetSmokeBall(nameof(BallState.Velocity),Vec2.Zero);
            app.WorldCamera.GetComponent<ArenaCamera>().ResetFraming(); app.UI.RefreshMatch();
            app.View.Render(state,app.Simulation.Rules,1,false,false);
            yield return null; yield return new WaitForEndOfFrame();
            CheckMiniMapLayout("3v3 full HUD"); CheckMiniMapRoster("3v3");
            Check(app.UI.Root.Q("minimap-player-1").ClassListContains("minimap-ally") &&
                app.UI.Root.Q("minimap-player-2").ClassListContains("minimap-ally") &&
                app.UI.Root.Q("minimap-player-3").ClassListContains("minimap-enemy") &&
                app.UI.Root.Q("minimap-player-4").ClassListContains("minimap-enemy") &&
                app.UI.Root.Q("minimap-player-5").ClassListContains("minimap-enemy"),
                "3v3 minimap clearly distinguishes both teammates and all three enemies");
            yield return Capture("40-minimap-3v3"); WriteMiniMapBounds("40-minimap-3v3");
            Vector2[] initial=new Vector2[6]; var commands=new PlayerCommand[6];
            for(int i=0;i<6;i++)
            {
                initial[i]=app.UI.Root.Q("minimap-player-"+i).worldBound.center;
                commands[i]=new PlayerCommand {Move=new Vec2(i<3 ? 1 : -1,i%2==0 ? 1 : -1),Aim=new Vec2(i<3 ? 1 : -1,0)};
            }
            Directory.CreateDirectory(Path.Combine(output,"minimap-motion-frames"));
            for(int frame=0;frame<12;frame++)
            {
                for(int tick=0;tick<8;tick++) StepPresented(commands);
                app.WorldCamera.GetComponent<ArenaCamera>().ResetFraming(); app.UI.RefreshMatch();
                app.View.Render(state,app.Simulation.Rules,1,false,false);
                yield return null; yield return Capture("minimap-motion-frames/"+frame.ToString("000"));
            }
            bool allMoved=true;
            for(int i=0;i<6;i++) allMoved &= Vector2.Distance(initial[i],app.UI.Root.Q("minimap-player-"+i).worldBound.center)>5;
            Check(allMoved,"all six portraits move live after real Core commands from both teams");
            CheckMiniMapRoster("moving 3v3");
            yield return Capture("41-minimap-3v3-moved");
            app.UI.SetFullHud(false,false); yield return null;
            CheckMiniMapLayout("3v3 compact HUD");
            // 把足球放到镜头左下之外，验证边缘提示避让地图。
            SetSmokeBall(nameof(BallState.Position),new Vec2(-27,-16)); SetSmokeBall(nameof(BallState.PreviousPosition),state.Ball.Position);
            yield return null; yield return new WaitForEndOfFrame();
            Check(app.UI.BallLocatorVisible && !app.UI.Root.Q("offscreen-ball").worldBound.Overlaps(app.UI.Root.Q("minimap").worldBound),
                "offscreen football direction cue avoids the bottom-left minimap in compact HUD");
            SetSmokeBall(nameof(BallState.Position),Vec2.Zero); SetSmokeBall(nameof(BallState.PreviousPosition),Vec2.Zero);
            app.UI.SetFullHud(full,false); app.SetPaused(false);
        }

        private IEnumerator CaptureCraftedArena()
        {
            app.SetPaused(true); app.UI.ShowPause(false);
            var arena=app.View.GetComponent<PremiumArenaView>(); var rules=app.Simulation.Rules;
            Check(rules.CenterCircleRadius==4.2f && arena.Details.CenterRadius==rules.CenterCircleRadius &&
                arena.Details.InlayCount>120 && arena.Details.GetComponentsInChildren<Collider>().Length==0,
                "larger kickoff circle has layered crest, embedded team trim and crafted collider-free architecture");
            var tile=arena.Dynamics.Turf.sharedMaterial.mainTexture;
            Check(tile.name=="turf-detail-v4" && tile.width>=1024 && tile.wrapMode==TextureWrapMode.Repeat &&
                tile.filterMode==FilterMode.Trilinear,
                "native turf uses the new HD repeated grass material with mipmapped trilinear filtering");
            Check(Resources.Load<Shader>("SceneSurface/ArenaInlay").isSupported,
                "brushed surface and recessed light shader works in the native player");
            MeshRenderer baseRing=null,lightRing=null;
            foreach(var renderer in arena.Details.GetComponentsInChildren<MeshRenderer>())
            {
                if(renderer.name=="Recessed center surround") baseRing=renderer;
                if(renderer.name=="Segmented center energy inlay") lightRing=renderer;
            }
            Check(baseRing!=null && lightRing!=null && lightRing.bounds.center.y>baseRing.bounds.center.y+.01f,
                "center trim uses separate surface layers to prevent z fighting");
            var camera=app.WorldCamera; var position=camera.transform.position; float size=camera.orthographicSize;
            camera.transform.position=-camera.transform.forward*25; camera.orthographicSize=6.4f;
            app.UI.Root.style.visibility=Visibility.Hidden;
            yield return Capture("32-crafted-midfield");
            camera.transform.position=new Vector3(rules.FieldWidth*.5f-3,0,0)-camera.transform.forward*25;
            camera.orthographicSize=5.7f;
            yield return Capture("33-crafted-goal-zone");
            camera.transform.position=position;camera.orthographicSize=size;app.UI.Root.style.visibility=Visibility.Visible;
            app.SetPaused(false);
            yield return new WaitForSecondsRealtime(.4f);
            int firstFrame=Time.frameCount; float began=Time.realtimeSinceStartup;
            while(Time.realtimeSinceStartup-began<.8f) yield return null;
            unobstructedSampleFps=(Time.frameCount-firstFrame)/(Time.realtimeSinceStartup-began);
        }

        private IEnumerator CaptureFinalGoal()
        {
            app.SetPaused(true);app.UI.ShowPause(false);
            var state=app.Simulation.State;
            typeof(MatchState).GetProperty(nameof(MatchState.Overtime)).SetValue(state,true);
            typeof(MatchState).GetProperty(nameof(MatchState.RemainingSeconds)).SetValue(state,90f);
            SetSmokeBall(nameof(BallState.OwnerId),-1);SetSmokeBall(nameof(BallState.LastKickerId),0);
            SetSmokeBall(nameof(BallState.Position),new Vec2(app.Simulation.Rules.FieldWidth*.5f+app.Simulation.Rules.BallRadius-.1f,0));
            SetSmokeBall(nameof(BallState.PreviousPosition),state.Ball.Position);SetSmokeBall(nameof(BallState.Velocity),new Vec2(18,0));
            app.Simulation.Step(new PlayerCommand[state.Players.Count]);
            bool goal=false;
            foreach(var e in state.Events) {app.HandleEvent(e);if(e.Kind==MatchEventKind.Goal) goal=true;}
            long finalTick=state.Tick;
            Check(goal && state.Phase==MatchPhase.Finished && app.CelebratingFinalGoal && app.Page==AppPage.Match,
                "actual golden goal finishes Core immediately but reserves its final celebration before results");
            app.UI.RefreshMatch();app.SetPaused(false);
            var point=new Vector3(app.Simulation.Rules.FieldWidth*.5f,1,0);
            float deadline=Time.realtimeSinceStartup+.9f;Vector3 screen=app.WorldCamera.WorldToViewportPoint(point);
            while(Time.realtimeSinceStartup<deadline)
            {screen=app.WorldCamera.WorldToViewportPoint(point);if(app.WorldCamera.GetComponent<ArenaCamera>().RevealingGoal && screen.x>.1f && screen.x<.9f) break;yield return null;}
            var flags=app.View.GetComponent<PremiumArenaView>().Dynamics.Celebration;
            Check(state.Tick==finalTick && app.Page==AppPage.Match && app.CelebratingFinalGoal && flags.Visible,
                "final goal renders its celebration while the finished match no longer advances simulation ticks");
            Check(screen.x>.08f && screen.x<.92f && app.WorldCamera.GetComponent<ArenaCamera>().RevealingGoal,
                "final golden goal also reveals the scored goal through the cinematic camera");
            app.SetPaused(true);app.UI.ShowPause(false);float remaining=app.FinalGoalRemaining,age=flags.Age;
            yield return new WaitForSecondsRealtime(.2f);
            Check(app.FinalGoalRemaining==remaining && flags.Age==age && state.Tick==finalTick,
                "pause freezes final goal celebration and its pending results transition");
            yield return Capture("39-final-golden-goal");
            app.SetPaused(false);deadline=Time.realtimeSinceStartup+3;
            while(app.Page==AppPage.Match && Time.realtimeSinceStartup<deadline) yield return null;
            Check(app.Page==AppPage.Results && app.FinalGoalRemaining==0 && state.Tick==finalTick,
                "completed final goal celebration transitions to real results without extra gameplay ticks");
            yield return Capture("40-golden-goal-results");
        }

        private IEnumerator CaptureFootballEffects()
        {
            bool savedFeedback=app.View.FeedbackEnabled,full=app.UI.FullHudEnabled,debug=app.ShowDebug;
            if(debug) app.ToggleDebug();app.UI.SetFullHud(false,false);app.View.SetFeedbackEnabled(true);
            app.SetPaused(true);app.UI.ShowPause(false);app.ResetTraining(true);
            var fx=app.View.KickEffects;var stadium=app.View.GetComponent<PremiumArenaView>().Dynamics;
            var flags=stadium.Celebration;var tracking=app.WorldCamera.GetComponent<ArenaCamera>();
            Check(fx.GetComponentsInChildren<Collider>().Length==0 && flags.GetComponentsInChildren<Collider>().Length==0 &&
                Resources.Load<Shader>("SceneSurface/CelebrationPaper").isSupported,
                "native kick feedback and lit celebration mesh have no gameplay colliders and a supported paper shader");
            var commands=new PlayerCommand[2];
            commands[0]=new PlayerCommand {Aim=new Vec2(1,.55f),KickPressed=true,KickHeld=true};StepPresented(commands);
            commands[0]=new PlayerCommand {Aim=new Vec2(1,.55f),KickReleased=true};StepPresented(commands);
            app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,false);
            Check(fx.ActiveBursts==1 && fx.LastKickStrength<.10f && app.Simulation.State.Players[0].KickCooldown>5.9f,
                "real tap release emits a small kick contact burst and preserves six second cooldown");
            var tapTint=fx.TrailTint;
            app.ResetTraining(true);
            commands[0]=new PlayerCommand {Aim=new Vec2(1,.55f),KickPressed=true,KickHeld=true};StepPresented(commands);
            commands[0]=new PlayerCommand {Aim=new Vec2(1,.55f),KickHeld=true};for(int i=0;i<45;i++) StepPresented(commands);
            app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,false);app.UI.RefreshMatch();
            Check(fx.ChargeFraction>.85f && fx.ChargeFraction<=1,"real held kick fills the world-space charge arc around the football");
            yield return Capture("34-football-charge");
            commands[0]=new PlayerCommand {Aim=new Vec2(1,.55f),Cancel=true};StepPresented(commands);
            app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,false);
            Check(fx.ChargeFraction==0 && fx.ActiveBursts==0 && app.Simulation.State.Ball.OwnerId==0,
                "cancelling charge clears its halo and produces no kick burst or release");
            app.ResetTraining(true);
            commands[0]=new PlayerCommand {Aim=new Vec2(1,.55f),KickPressed=true,KickHeld=true};StepPresented(commands);
            commands[0]=new PlayerCommand {Aim=new Vec2(1,.55f),KickHeld=true};for(int i=0;i<45;i++) StepPresented(commands);
            commands[0]=new PlayerCommand {Aim=new Vec2(1,.55f),KickReleased=true};StepPresented(commands);
            app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,false);app.UI.RefreshMatch();
            Check(fx.LastKickStrength>.85f && fx.ActiveBursts==1 && fx.TrailTint!=tapTint &&
                app.Simulation.State.Ball.FlightMode==BallFlightMode.Ground && app.Simulation.State.Ball.Height==0,
                "charged release changes burst strength and trail tint while ordinary shots remain planar");
            yield return Capture("35-football-kick-contact");
            commands[0]=default;
            Directory.CreateDirectory(Path.Combine(output,"kick-effects-frames"));
            for(int frame=0;frame<9;frame++)
            {
                for(int tick=0;tick<6;tick++)
                {
                    StepPresented(commands);app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,true);
                    yield return null;
                }
                yield return Capture("kick-effects-frames/"+frame.ToString("000"));
            }
            Check(fx.TrailSamples>=2 && fx.TrailSamples<=32,"actual moving football draws a bounded tapered history trail");
            float kickClock=fx.PresentationTime;int trailSamples=fx.TrailSamples;
            yield return new WaitForSecondsRealtime(.2f);
            Check(fx.PresentationTime==kickClock && fx.TrailSamples==trailSamples,"pause freezes kick burst and trail presentation time");
            app.View.SetFeedbackEnabled(false);
            Check(fx.ActiveBursts==0 && fx.TrailSamples==0 && fx.ChargeFraction==0,"feedback switch clears all kick effects immediately");
            app.View.SetFeedbackEnabled(true);app.ResetTraining(false);
            Check(fx.ActiveBursts==0 && fx.TrailSamples==0,"training reset clears kick contact and ball trail pools");
            for(int side=1;side>=-1;side-=2)
            {
                if(side<0) {app.UI.SetFullHud(true,false);yield return null;}
                app.ResetTraining(false);
                // 只在显式烟测中把球送到门线前，随后由真实扫掠判定和 Goal 事件触发。
                SetSmokeBall(nameof(BallState.OwnerId),-1);SetSmokeBall(nameof(BallState.LastKickerId),side>0 ? 0 : 1);
                SetSmokeBall(nameof(BallState.Position),new Vec2(side*(app.Simulation.Rules.FieldWidth*.5f-.1f),0));
                SetSmokeBall(nameof(BallState.PreviousPosition),app.Simulation.State.Ball.Position);
                SetSmokeBall(nameof(BallState.Velocity),new Vec2(side*18,0));
                if(side<0) app.View.SetFeedbackEnabled(false);
                bool goal=false;
                for(int i=0;i<12 && !goal;i++) {StepPresented(commands);foreach(var e in app.Simulation.State.Events) if(e.Kind==MatchEventKind.Goal) goal=true;}
                Check(goal && flags.GoalSide==side && flags.ActivePennantCount==11 && flags.ActivePaperCount==96 && flags.ActiveRibbonCount==12,
                    "real "+(side>0 ? "cyan" : "amber")+" goal unfolds pennants and emits bounded lit confetti and curled ribbons at the correct goal");
                app.UI.RefreshMatch();app.SetPaused(false);
                var goalPoint=new Vector3(side*app.Simulation.Rules.FieldWidth*.5f,1,0);
                Vector3 goalScreen=app.WorldCamera.WorldToViewportPoint(goalPoint);
                float revealDeadline=Time.realtimeSinceStartup+.9f;
                while(Time.realtimeSinceStartup<revealDeadline)
                {
                    goalScreen=app.WorldCamera.WorldToViewportPoint(goalPoint);
                    if(tracking.RevealingGoal && goalScreen.x>.10f && goalScreen.x<.90f) break;
                    yield return null;
                }
                Check(tracking.RevealingGoal && goalScreen.x>.08f && goalScreen.x<.92f && goalScreen.y>.1f && goalScreen.y<.9f,
                    "goal camera reveals the scored "+(side>0 ? "right" : "left")+" goal even when the local player remains at midfield");
                var extent=tracking.VisibleGroundSize*.5f;
                Check(Mathf.Abs(tracking.FocusGround.x)+extent.x<=app.Simulation.Rules.FieldWidth*.5f+3.001f &&
                    app.WorldCamera.rect==new Rect(0,0,1,1),
                    "goal reveal respects dressed scene bounds and the "+(side>0 ? "compact" : "full")+" HUD viewport");
                app.SetPaused(true);app.UI.ShowPause(false);float age=flags.Age;var focus=tracking.FocusGround;
                yield return new WaitForSecondsRealtime(.2f);
                Check(flags.Age==age && tracking.FocusGround==focus && flags.Visible,"pause freezes visible celebration cloth, ribbons and goal camera");
                yield return Capture(side>0 ? "36-goal-cyan" : "37-goal-amber");
                if(side>0)
                {
                    Directory.CreateDirectory(Path.Combine(output,"goal-effects-frames"));
                    for(int frame=0;frame<8;frame++)
                    {
                        for(int step=0;step<4;step++) {app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,true);yield return null;}
                        yield return Capture("goal-effects-frames/"+frame.ToString("000"));
                    }
                    Check(flags.Age>age && flags.GetComponent<MeshFilter>().sharedMesh.vertexCount<2400,
                        "celebration uses one animated paper mesh with bounded vertex count");
                    app.ResetTraining(false);
                    Check(!flags.Visible && flags.ActivePennantCount==0 && flags.ActivePaperCount==0 && flags.ActiveRibbonCount==0,
                        "training reset clears goal pennants, paper and ribbons together");
                }
            }
            for(int i=0;i<150;i++) StepPresented(commands);
            app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,false);
            Check(!flags.Visible && flags.ActivePaperCount==0,"new kickoff clears goal decorations before play resumes");
            app.SetPaused(false);yield return new WaitForSecondsRealtime(.3f);
            var player=app.Simulation.State.Players[0];
            Check(!tracking.RevealingGoal && Vector2.Distance(tracking.FocusGround,new Vector2(player.Position.X,player.Position.Y))<1,
                "kickoff restores local player camera framing after goal celebration");
            app.ResetTraining(false);app.View.SetFeedbackEnabled(savedFeedback);app.UI.SetFullHud(full,false);
            if(debug) app.ToggleDebug();app.SetPaused(false);app.UI.RefreshMatch();
        }

        private IEnumerator CaptureSkillHelp()
        {
            app.SetPaused(true); app.UI.ShowPause(false); app.ResetTraining(false); app.UI.SetFullHud(true,false); app.UI.RefreshMatch();
            yield return new WaitForSecondsRealtime(.6f);
            var dock=app.UI.Root.Q("battle-dock").worldBound;
            Check(dock.height/app.UI.Root.worldBound.height<.17f && dock.width/app.UI.Root.worldBound.width<.38f,
                "smaller full HUD occupies less than 17 percent height and 38 percent width");
            // 验证实际渲染底部的无遮挡区域，不仅检查相机 rect 配置。
            yield return new WaitForEndOfFrame();
            var fieldImage=ScreenCapture.CaptureScreenshotAsTexture(); bool fieldAtBottom=true;
            foreach(float x in new[]{.28f,.78f,.92f})
            {
                var color=fieldImage.GetPixel(Mathf.RoundToInt(x*(fieldImage.width-1)),15);
                fieldAtBottom &= color.grayscale>.08f && color.g>color.r && color.g>color.b;
            }
            Destroy(fieldImage);
            Check(app.WorldCamera.rect==new Rect(0,0,1,1) && fieldAtBottom,
                "native full HUD frame renders actual grass to the bottom edge outside overlays");
            foreach(string id in new[]{"q","e","g","kick","tackle","weapon"})
            {
                var tile=app.UI.Root.Q(id+"-tile");
                using(var evt=PointerEnterEvent.GetPooled()) {evt.target=tile; tile.SendEvent(evt);}
                yield return new WaitForSecondsRealtime(.16f);
                var panel=app.UI.Root.Q("skill-tooltip");
                Check(app.UI.SkillHelpVisible && app.UI.Root.Q<Label>("skill-tooltip-body").text.Length>12 &&
                    app.UI.Root.Q<Label>("skill-tooltip-meta").text.Length>5 && panel.worldBound.xMin>=0 &&
                    panel.worldBound.xMax<=app.UI.Root.worldBound.xMax && panel.worldBound.yMax<tile.worldBound.yMin,
                    "pointer hover shows bounded skill description, costs and cooldown above "+id);
                if(id=="q") yield return Capture("28-marine-skill-tooltip");
                using(var evt=PointerLeaveEvent.GetPooled()) {evt.target=tile; tile.SendEvent(evt);}
                yield return null;
                Check(!app.UI.SkillHelpVisible,"pointer leave dismisses skill description for "+id);
            }
            var q=app.UI.Root.Q("q-tile");
            using(var evt=PointerEnterEvent.GetPooled()) {evt.target=q; q.SendEvent(evt);}
            var commands=new PlayerCommand[2]; commands[0]=new PlayerCommand {QPressed=true}; StepPresented(commands); app.UI.RefreshMatch();
            yield return new WaitForSecondsRealtime(.16f);
            Check(app.UI.Root.Q<Label>("skill-tooltip-state").text.Contains("冷却"),"open skill description updates from live cooldown state");
            app.UI.SetFullHud(false,false); yield return null;
            Check(!app.UI.SkillHelpVisible && app.UI.Root.Q("skill-tooltip").resolvedStyle.display==DisplayStyle.None,
                "switching to compact HUD clears the floating skill description");
            app.UI.SetFullHud(true,false); app.ResetTraining(false); app.UI.RefreshMatch(); app.SetPaused(false);
        }
        private float GrassLuminance(Texture2D image,Vec2 position)
        {
            Vector3 screen=app.WorldCamera.WorldToScreenPoint(ArenaProjection.ToWorld(position));
            float sum=0;
            for(int x=-6;x<=6;x++) for(int y=-6;y<=6;y++)
            {
                Color c=image.GetPixel(Mathf.RoundToInt(screen.x)+x,Mathf.RoundToInt(screen.y)+y);
                sum+=c.r*.2126f+c.g*.7152f+c.b*.0722f;
            }
            return sum/169;
        }

        private IEnumerator CaptureMainMenuFlow()
        {
            var home = app.UI.Root.Q("home-page");
            app.UI.Root.Q<Button>("bots-button").Focus();
            using (var evt = KeyDownEvent.GetPooled('\0', KeyCode.DownArrow, EventModifiers.None)) home.SendEvent(evt);
            Check(app.UI.Root.panel.focusController.focusedElement == app.UI.Root.Q<Button>("normal-mode"), "arrow down selects multiplayer from primary entry");
            using (var evt = KeyDownEvent.GetPooled('\0', KeyCode.Return, EventModifiers.None)) home.SendEvent(evt);
            yield return null;
            Check(app.Page==AppPage.Home && app.UI.Chat.IsOpen,"Enter opens home chat while keeping the selected menu action inactive");
            app.UI.Chat.Close();yield return null;yield return Click("normal-mode");
            Check(app.Page==AppPage.NetworkMenu && app.Route==ConnectionRoute.LAN,"mouse opens offline multiplayer on LAN");
            yield return new WaitForSecondsRealtime(.35f);
            Check(!app.UI.Root.Q<Button>("internet-route").enabledSelf && PicksButton("route-login"), "offline multiplayer offers login beside disabled Internet tab");
            yield return Click("network-create-tab");
            Check(app.Page == AppPage.NetworkMenu && app.NetworkCreating, "main menu multiplayer creates room without a mode-information screen");
            yield return Click("network-join-tab");
            Check(app.Page == AppPage.NetworkMenu && !app.NetworkCreating && app.UI.JoinDialogOpen, "one join entry opens room-code input from the browser");
            yield return Click("network-join-cancel");app.BackFromNetwork();yield return null;
            yield return Click("bots-button");
            Check(app.Page == AppPage.PlayMenu && app.Simulation==null, "primary start entry chooses mode before character selection");
            yield return Click("play-ai");
            Check(app.InDraft && app.ActiveDraft.CurrentSlot==0, "AI mode first opens alternating character and position draft");
            yield return Click("select-back");
            Check(app.Page == AppPage.PlayMenu, "AI draft returns to mode selection");
            yield return Click("play-back");
            yield return Click("train-button");
            Check(app.Page == AppPage.CharacterSelect && app.SelectedMode == MatchMode.Training, "training entry opens training character selection directly");
            yield return Click("select-back");
            foreach (var size in new[] { new Vector2Int(1024,768), new Vector2Int(1280,720), new Vector2Int(1920,1080) })
            {
                Screen.SetResolution(size.x, size.y, false); yield return new WaitForSecondsRealtime(.55f);
                foreach (string name in new[] { "bots-button", "normal-mode", "train-button", "profile-button", "shop-button", "settings-button", "home-account" })
                {
                    var bounds = app.UI.Root.Q(name).worldBound;
                    Check(PicksButton(name) && app.UI.Root.worldBound.Contains(bounds.min) && app.UI.Root.worldBound.Contains(bounds.max),
                        size + " main menu control fits and receives pointer: " + name);
                }
                var marine = GameObject.Find("Marine menu model"); var thor = GameObject.Find("Thor menu model");
                Check(marine != null && thor != null && app.WorldCamera.WorldToViewportPoint(marine.transform.position + Vector3.up * 1.7f).x > .46f &&
                    app.WorldCamera.WorldToViewportPoint(thor.transform.position + Vector3.up * 1.9f).x > .46f,
                    size + " live character lineup stays on the display side of the menu");
                yield return Capture("menu-home-" + size.x + "x" + size.y);
                yield return Click("normal-mode");
                Check(PicksButton("network-create-tab") && PicksButton("network-join-tab") && PicksButton("network-back"), size + " multiplayer entrances receive pointer");
                yield return new WaitForSecondsRealtime(.35f);
                yield return Capture("menu-multiplayer-" + size.x + "x" + size.y);
                yield return Click("network-back");
            }
            app.UI.Root.Q<Button>("bots-button").Focus();
            using (var evt = KeyDownEvent.GetPooled('\0', KeyCode.UpArrow, EventModifiers.None)) home.SendEvent(evt);
            Check(app.UI.Root.panel.focusController.focusedElement == app.UI.Root.Q<Button>("settings-button"), "arrow up wraps main navigation to settings");
        }

        private IEnumerator Click(string name)
        {
            yield return null;
            var button = app.UI.Root.Q<Button>(name);
            if (button == null) { errors.Add("Missing button: " + name); yield break; }
            bool activated = false;
            Action observe = () => activated = true;
            button.clicked += observe;
            button.Focus();
            yield return null;
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target=button; button.SendEvent(evt); }
            yield return null;
            if (activated) { button.clicked -= observe; yield break; }
            var position = button.worldBound.center;
            using (var evt = MouseMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = position })) button.SendEvent(evt);
            yield return null;
            using (var evt = MouseDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = position })) button.SendEvent(evt);
            yield return null;
            using (var evt = MouseUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = position })) button.SendEvent(evt);
            yield return null;
            button.clicked -= observe;
            Check(activated, "button event: " + name);
        }
        private void Check(bool condition, string description)
        {
            checks.Add((condition ? "PASS " : "FAIL ") + description);
            if (!condition) { errors.Add(description); Debug.Log("SOCCER_CHECK_FAIL " + description + " page=" + app.Page + " mode=" + app.SelectedMode); }
        }
        private bool PicksButton(string name)
        {
            var button = app.UI.Root.Q<Button>(name);
            var picked = app.UI.Root.panel.Pick(button.worldBound.center);
            while (picked != null)
            {
                if (picked == button) return true;
                picked = picked.parent;
            }
            return false;
        }
        private void CaptureError(string message, string stack, LogType type)
        {
            if (message.StartsWith("Layout update") && !errors.Exists(e => e.StartsWith("Layout update")))
                Debug.Log("SOCCER_LAYOUT_CONTEXT page=" + app.Page + " frame=" + Time.frameCount + " classes=" + string.Join(",", app.UI.Root.GetClasses()));
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack);
        }
        private IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
            Destroy(image);
        }
    }
}
