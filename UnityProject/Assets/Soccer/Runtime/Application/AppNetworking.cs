using System;
using Soccer.Core;
using Soccer.Core.Networking;
using UnityEngine;

namespace Soccer.Runtime
{
    public sealed partial class AppController
    {
        public NetworkRoom Room { get; private set; }
        public DebugAccountSession Account { get; private set; }
        public LanDiscovery Discovery { get; private set; }
        public int LocalPlayerId { get; private set; }
        public bool NetworkCreating { get; private set; }
        public string NetworkStatus { get; private set; } = "";
        public bool IsNetworkMatch => Room != null && Room.Connected && Room.Running && Simulation != null;
        private float nextNetworkUi, lastSnapshotAt;
        internal PlayerCommand? NetworkInputOverride;
        public void OpenNetwork(bool create)
        {
            if (SelectedPlaylist != GamePlaylist.Normal || !EnsureEntered()) return;
            if (Route == ConnectionRoute.Internet && EntryMode != GameEntryMode.Online) { OpenLogin(); return; }
            CloseNetworking(); NetworkCreating = create; NetworkStatus = "";
            if (Route == ConnectionRoute.LAN)
                try { Discovery = new LanDiscovery(); } catch (Exception) { NetworkStatus = "自动发现不可用，可以使用房主地址加入"; }
            Page = AppPage.NetworkMenu; UI.ShowPage(Page); Audio.SetContext(Page, false);
        }
        public void BackFromNetwork()
        { CloseNetworking(); OpenMode(SelectedPlaylist); }
        private void CloseNetworking()
        { Room?.Dispose(); Room = null; Discovery?.Dispose(); Discovery = null; }
        public void CreateNetworkRoom(RoomOptions options)
        {
            try
            {
                Room?.Dispose(); options.Route = (NetworkRoute)Route; options.Account = Account;
                Room = NetworkRoom.Host(options); AttachRoom(); LocalPlayerId = Room.LocalSlot;
                NetworkStatus = Room.Status; Page = AppPage.RoomLobby; UI.ShowPage(Page);
            }
            catch (Exception e) { Room?.Dispose(); Room = null; NetworkStatus = e is ArgumentException ? e.Message : "建房失败：端口可能已被占用，或防火墙禁止监听"; UI.RefreshNetwork(); }
        }
        public void JoinNetworkRoom(string address, RoomOptions options)
        {
            try
            {
                Room?.Dispose(); options.Route = (NetworkRoute)Route; options.Account = Account;
                Room = NetworkRoom.Join(address, options); AttachRoom(); NetworkStatus = "正在连接房主…"; UI.RefreshNetwork();
            }
            catch (Exception e) { Room?.Dispose(); Room = null; NetworkStatus = e.Message; UI.RefreshNetwork(); }
        }
        private void AttachRoom()
        {
            Room.Changed += () =>
            {
                if (Room == null) return; LocalPlayerId = Room.LocalSlot; NetworkStatus = Room.Status;
                if (Room.Connected && Page == AppPage.NetworkMenu) { Page = AppPage.RoomLobby; UI.ShowPage(Page); }
                UI.RefreshNetwork();
            };
            Room.Started += BeginNetworkMatch;
            Room.ReturnedToLobby += () =>
            {
                Simulation = null; View.Clear(); localInput?.Reset(); Paused = false; FinalGoalRemaining = 0;
                Page = AppPage.RoomLobby; UI.ShowPage(Page); Audio.SetContext(Page, false);
            };
            Room.PlayerLeft += slot => { Simulation?.CancelPlayerInputs(slot); if (Simulation != null && inputSources != null) inputSources[slot] = new BotCommandSource(Simulation.Rules); Feedback = "球员离开，空位由 AI 接管"; feedbackRemaining = 3; };
            Room.SnapshotReceived += bytes =>
            {
                if (Simulation == null) return;
                long tick = Simulation.State.Tick; NetworkCodec.ApplySnapshot(Simulation.State, bytes); lastSnapshotAt = Time.realtimeSinceStartup;
                if (Simulation.State.Tick > tick) foreach (var e in Simulation.State.Events) HandleEvent(e);
            };
            Room.Failed += reason =>
            {
                Room?.Dispose(); Room = null; NetworkStatus = reason; Simulation = null; Paused = false; LocalPlayerId = 0;
                localInput?.Reset(); View.Clear(); Page = AppPage.NetworkMenu; UI.ShowPage(Page); Audio.SetContext(Page, false);
            };
        }
        public void BeginNetworkMatch(NetworkStart start)
        {
            LocalPlayerId = Room.LocalSlot; Selected = start.Lineup[LocalPlayerId]; SelectedMode = MatchMode.BotMatch;
            Simulation = new MatchSimulation(start.Rules, Array.ConvertAll(Characters, c => c.CreateStats()), start.Lineup[0], MatchMode.BotMatch, start.Lineup);
            localInput = new KeyboardMouseInput(WorldCamera); inputSources = new ICommandSource[6]; commands = new PlayerCommand[6];
            for (int i = 0; i < 6; i++) inputSources[i] = i == LocalPlayerId ? (ICommandSource)localInput : new BotCommandSource(Simulation.Rules);
            View.LocalPlayerId = Audio.LocalPlayerId = LocalPlayerId; View.Bind(Simulation.State, Characters);
            accumulator = 0; lastSnapshotAt = Time.realtimeSinceStartup; FinalGoalRemaining = 0; Paused = false;
            Feedback = "联机 3v3 · 接触足球夺球，传球配合进攻"; feedbackRemaining = 4;
            Page = AppPage.Match; UI.ShowPage(Page); WorldCamera.GetComponent<ArenaCamera>().ResetFraming(); Audio.ResetMatch(); Audio.SetContext(Page, false);
            if (Room.IsHost) Room.Publish(Simulation.State, true);
        }
        private void PumpNetworking()
        {
            Room?.Pump(Time.realtimeSinceStartup);
            if (Page == AppPage.NetworkMenu) Discovery?.Pump(Time.realtimeSinceStartup);
            if ((Page == AppPage.NetworkMenu || Page == AppPage.RoomLobby) && Time.realtimeSinceStartup >= nextNetworkUi)
            { nextNetworkUi = Time.realtimeSinceStartup + .25f; UI.RefreshNetwork(); }
        }
        private void UpdateNetworkMatch()
        {
            var state = Simulation.State;
            if (state.Phase == MatchPhase.Finished)
            {
                if (CelebratingFinalGoal) FinalGoalRemaining = Mathf.Max(0, FinalGoalRemaining - Mathf.Min(Time.unscaledDeltaTime, .1f));
                View.Render(state, Simulation.Rules, 1, ShowDebug, !Paused); UI.RefreshMatch();
                if (FinalGoalRemaining <= 0) ShowResults(); return;
            }
            bool inputActive = !Paused && Application.isFocused && (state.Phase == MatchPhase.Playing || state.IsOpeningWarmup || state.Phase == MatchPhase.SetPiece);
            if (inputActive) localInput.Sample(state, Simulation.Rules, UI.IsPointerOverControl(), state.IsOpeningWarmup, LocalPlayerId);
            else localInput.Reset();
            accumulator += Mathf.Min(Time.unscaledDeltaTime, .1f);
            while (accumulator >= MatchSimulation.TickSeconds)
            {
                if (Room.IsHost)
                {
                    for (int i = 0; i < 6; i++) commands[i] = i == LocalPlayerId ? !Paused && NetworkInputOverride.HasValue ? NetworkInputOverride.Value : localInput.ReadCommand(state, i) : Room.IsHumanSlot(i) ? Room.ReadInput(i) : inputSources[i].ReadCommand(state, i);
                    Simulation.Step(commands); Room.Publish(state);
                    foreach (var e in state.Events) HandleEvent(e);
                }
                else Room.SendInput(!Paused && NetworkInputOverride.HasValue ? NetworkInputOverride.Value : localInput.ReadCommand(state, LocalPlayerId));
                accumulator -= MatchSimulation.TickSeconds;
                if (state.Phase == MatchPhase.Finished) break;
            }
            float alpha = Room.IsHost ? accumulator / MatchSimulation.TickSeconds : Mathf.Clamp01((Time.realtimeSinceStartup - lastSnapshotAt) / .05f);
            Audio.Observe(state, Simulation.Rules); View.Render(state, Simulation.Rules, alpha, ShowDebug, !Paused, localInput.PreviewAim, localInput.SelectingSkillTarget); UI.RefreshMatch();
        }
        private void OnDestroy() { CloseNetworking(); Account?.Dispose(); Account = null; }
    }
}
