using System;
using System.Collections.Generic;
using System.Linq;
using Soccer.Core;
using Soccer.Core.Networking;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    /// <summary>Room setup and lobby controls; passwords stay in masked, unsaved fields.</summary>
    public sealed class NetworkUI
    {
        private readonly AppController app;
        private readonly VisualElement root;
        private readonly List<string> characters = new List<string> { "枪兵", "雷神" };
        private readonly List<string> teams = new List<string> { "青队", "橙队" };
        private string discoverySignature = "", lobbySignature = "";
        private int countdownNumber;private string countdownRoom="";
        private TextField Field(string name) => root.Q<TextField>(name);
        private DropdownField Drop(string name) => root.Q<DropdownField>(name);
        private void Text(string name, string value) => root.Q<Label>(name).text = value;
        private void Show(string name, bool visible) => root.Q(name).EnableInClassList("hidden", !visible);
        public NetworkUI(AppController controller, VisualElement ui)
        {
            app = controller; root = ui;
            foreach (string name in new[] { "network-profile", "network-join-secret", "network-lan-help", "lobby-options" })
            { var foldout = root.Q<Foldout>(name); foldout.SetValueWithoutNotify(false); }

            foreach (string name in new[] { "network-character", "lobby-character" }) { Drop(name).choices = characters; Drop(name).SetValueWithoutNotify(characters[0]); }
            foreach (string name in new[] { "network-team", "lobby-team" }) { Drop(name).choices = teams; Drop(name).SetValueWithoutNotify(teams[0]); }
            Field("network-player-name").maxLength = 24; Field("network-room-name").maxLength = 32;
            Field("network-address").maxLength = 1600;
            Field("network-player-name").SetValueWithoutNotify(PlayerPrefs.GetString("soccer.playerName", "球员"));
            foreach (string name in new[] { "network-room-password", "network-join-password" }) { Field(name).isPasswordField = true; Field(name).maxLength = 64; }
            root.Q<Toggle>("network-password-enabled").RegisterValueChangedCallback(e => { Field("network-room-password").SetEnabled(e.newValue); Show("network-room-password", e.newValue); });
            Field("network-room-password").SetEnabled(false);
            Button("network-back", app.BackFromNetwork); Button("lobby-leave", app.BackFromNetwork);
            Button("network-create-tab", () => app.OpenNetwork(true)); Button("network-join-tab", () => app.OpenNetwork(false));
            Button("network-create", () =>
            {
                var options = Options(); options.Name = Field("network-room-name").value;
                options.UsePassword = app.Route == ConnectionRoute.Internet && root.Q<Toggle>("network-password-enabled").value;
                options.Password = Field("network-room-password").value;
                app.CreateNetworkRoom(options); Field("network-room-password").SetValueWithoutNotify("");
            });
            Button("network-join", () =>
            { var options = Options(); options.Password = Field("network-join-password").value; app.JoinNetworkRoom(Field("network-address").value, options); Field("network-join-password").SetValueWithoutNotify(""); });
            Button("network-refresh", () => { app.Discovery?.Pump(Time.realtimeSinceStartup, true); Refresh(); });
            Button("lobby-ready", () => { var member = app.Room?.Lobby.Members.FirstOrDefault(m => m.Slot == app.Room.LocalSlot); if (member != null) app.Room.SetReady(!member.Ready); });
            Button("lobby-fill-bots",()=>app.Room?.FillBots());Button("lobby-clear-bots",()=>app.Room?.ClearBots());
            Button("lobby-copy-address", () => { GUIUtility.systemCopyBuffer = Field("lobby-address").value; });
            Drop("lobby-character").RegisterValueChangedCallback(e => Choice()); Drop("lobby-team").RegisterValueChangedCallback(e => Choice());
        }
        private void Button(string name, Action action)
        { root.Q<Button>(name).clicked += () => { app.Audio.PlayClick(); action(); }; }
        private void Status(string name, string value)
        { Text(name, value); Show(name, !string.IsNullOrWhiteSpace(value)); }
        private RoomOptions Options()
        {
            string name = Field("network-player-name").value;
            if (!string.IsNullOrWhiteSpace(name)) PlayerPrefs.SetString("soccer.playerName", name);
            return new RoomOptions { PlayerName = name, Character = Drop("network-character").value == characters[1] ? CharacterId.Thor : CharacterId.Marine,
                Team = Drop("network-team").value == teams[1] ? Team.Amber : Team.Cyan };
        }
        private void Choice()
        {
            if (app.Room == null || !app.Room.Connected || app.Room.Running) return;
            app.Room.SetChoice(Drop("lobby-character").value == characters[1] ? CharacterId.Thor : CharacterId.Marine,
                Drop("lobby-team").value == teams[1] ? Team.Amber : Team.Cyan);
        }
        public void Refresh()
        {
            if (app.Page == AppPage.NetworkMenu)
            {
                bool lan = app.Route == ConnectionRoute.LAN; bool busy = app.Room != null || app.NetworkLookupBusy;
                bool canSetUp = lan || app.EntryMode == GameEntryMode.Online;
                Show("network-profile", canSetUp);
                Text("network-title", app.NetworkCreating ? "创建房间" : "加入房间");
                Text("network-route", lan ? "局域网" : "互联网");
                Show("network-create-form", canSetUp && app.NetworkCreating); Show("network-join-form", canSetUp && !app.NetworkCreating);
                Show("network-internet-options", !lan); Show("network-join-secret", !lan); Show("network-lan-discovery", lan);
                Field("network-address").label = lan ? "房间号或房主 IP" : "房间号";
                Field("network-address").maxLength = lan ? 1600 : 6;
                Text("network-address-help", "例如 AB12CD");
                Status("network-status", app.NetworkStatus);
                if (!lan && app.NetworkStatus.Contains("密码")) root.Q<Foldout>("network-join-secret").value = true;
                root.Q("network-create-tab").EnableInClassList("route-selected", app.NetworkCreating);
                root.Q("network-join-tab").EnableInClassList("route-selected", !app.NetworkCreating);
                foreach (string name in new[] { "network-create", "network-join" }) root.Q<Button>(name).SetEnabled(!busy && canSetUp);
                foreach (string name in new[] { "network-create-tab", "network-join-tab" }) root.Q<Button>(name).SetEnabled(!busy);
                Show("network-character",false);
                RefreshDiscovered();
            }
            if (app.Page == AppPage.RoomLobby && app.Room?.Lobby != null) RefreshLobby();
        }
        private void RefreshDiscovered()
        {
            var rooms = app.Discovery?.Rooms ?? new DiscoveredRoom[0];
            string signature = string.Join("|", rooms.Select(r => r.Address + r.Lobby.Code + r.Lobby.Name + r.Lobby.Members.Length + r.Lobby.Bots.Length + (r.Lobby.CountdownRemaining>0) + r.Lobby.Running + r.Lobby.Drafting)) + (app.Room != null);
            Show("network-room-list-empty", rooms.Count == 0);
            if (signature == discoverySignature) return; discoverySignature = signature;
            var list = root.Q("network-room-list"); list.Clear();
            foreach (var room in rooms)
            {
                var row = new VisualElement(); row.AddToClassList("row"); row.AddToClassList("discovery-room");
                NetworkRoom.ParseAddress(room.Address, out string host, out int _);
                row.Add(new Label(room.Lobby.Name + " · " + (room.Lobby.Members.Length+room.Lobby.Bots.Length) + "/6" + (room.Lobby.Bots.Length>0 ? " · "+room.Lobby.Bots.Length+" 机器人" : "") + (room.Lobby.Drafting ? " · 选人中" : room.Lobby.Running ? " · 比赛中" : room.Lobby.CountdownRemaining>0 ? " · 即将开始" : "")));
                string invite = NetworkRoom.CreateInvitation(host, room.Lobby);
                var join = new Button(() => { Field("network-address").SetValueWithoutNotify(invite); app.JoinNetworkRoom(invite, Options()); }) { text = "加入", name = "network-discovered-" + room.Lobby.Id };
                join.SetEnabled(!room.Lobby.Running && !room.Lobby.Drafting && room.Lobby.CountdownRemaining==0 && room.Lobby.Members.Length+room.Lobby.Bots.Length < 6 && app.Room == null && !app.NetworkLookupBusy); row.Add(join); list.Add(row);
            }
        }
        private void RefreshLobby()
        {
            var room = app.Room; var lobby = room.Lobby; var local = lobby.Members.FirstOrDefault(m => m.Slot == room.LocalSlot);
            Text("lobby-route", lobby.Route == NetworkRoute.LAN ? "局域网" : "互联网"); Text("lobby-title", lobby.Name);
            Text("lobby-summary", $"{lobby.Members.Length} 位玩家 · {lobby.Bots.Length} 位机器人" + (lobby.RequiresPassword ? " · 有密码" : ""));
            string status = app.NetworkStatus;
            if (status == room.Status || status.StartsWith("房间号 ")) status = room.IsHost && !room.CanStart ? "等待玩家准备" : "";
            if (status.StartsWith("正在生成")) status = "";
            if(room.OccupiedSlots<6)status="等待玩家加入，或由房主补齐机器人";
            Status("lobby-status", status);
            if (local != null)
            {
                Drop("lobby-character").SetValueWithoutNotify(local.Character == CharacterId.Thor ? characters[1] : characters[0]);
                Drop("lobby-team").SetValueWithoutNotify(local.Team == Team.Amber ? teams[1] : teams[0]);
                root.Q<Button>("lobby-ready").text = local.Ready ? "取消准备" : "准备";
            }
            Show("lobby-ready",true);Show("lobby-fill-bots",room.IsHost);Show("lobby-clear-bots",room.IsHost);
            root.Q<Button>("lobby-fill-bots").SetEnabled(room.OccupiedSlots<6 && !lobby.Drafting && !lobby.Running);root.Q<Button>("lobby-clear-bots").SetEnabled(lobby.Bots.Length>0 && !lobby.Drafting && !lobby.Running);
            root.Q<Button>("lobby-ready").SetEnabled(!lobby.Running);
            Show("lobby-countdown",room.CountingDown);Text("lobby-countdown-number",Mathf.CeilToInt(lobby.CountdownRemaining).ToString());
            if(countdownRoom!=lobby.Id){countdownRoom=lobby.Id;countdownNumber=0;}
            int number=room.CountingDown ? Mathf.CeilToInt(lobby.CountdownRemaining) : 0;if(number!=countdownNumber){countdownNumber=number;if(number>0)app.Audio.PlayLobbyCountdown();}
            Show("lobby-character",false);
            Drop("lobby-character").SetEnabled(false); Drop("lobby-team").SetEnabled(!lobby.Running);
            Field("lobby-address").SetValueWithoutNotify(app.RoomCodeReady ? lobby.Code : "生成中…");
            Field("lobby-address").isReadOnly = true;
            Text("lobby-address-note", lobby.Route == NetworkRoute.LAN ? "房主 IP：" + string.Join(" / ", NetworkRoom.LocalAddresses()) : "");
            Show("lobby-address-note", lobby.Route == NetworkRoute.LAN);
            root.Q<Button>("lobby-copy-address").SetEnabled(app.RoomCodeReady);
            string signature = string.Join("|", lobby.Members.Select(m => m.Slot + ":" + m.Name + m.AccountId + m.Character + m.Ready)) + room.LocalSlot+":"+string.Join(",",lobby.Bots);
            if (signature == lobbySignature) return; lobbySignature = signature;
            root.Q("lobby-cyan-members").Clear(); root.Q("lobby-amber-members").Clear();
            for (int slot = 0; slot < 6; slot++)
            {
                var member = lobby.Members.FirstOrDefault(m => m.Slot == slot); var row = new VisualElement(); row.AddToClassList("network-member");
                row.EnableInClassList("local-member", slot == room.LocalSlot);
                bool bot=lobby.Bots.Contains(slot);var name = new Label(member == null ? bot ? "机器人" : "空位" : member.Name + (slot == room.LocalSlot ? " · 你" : "")); name.AddToClassList("network-member-name");name.AddToClassList("grow"); row.Add(name);
                if (member != null)
                {
                    name.tooltip = member.Name;
                    var detail = new Label((member.Host ? "房主 · " : "")+(member.Ready ? "已准备" : "未准备"));
                    detail.AddToClassList("network-member-detail"); row.Add(detail);
                }
                row.EnableInClassList("empty-member", member == null);
                if(member==null && room.IsHost){int captured=slot;var change=new Button(()=>{app.Audio.PlayClick();app.Room?.SetBot(captured,!bot);}){text=bot ? "移除" : "+ 机器人",name="lobby-bot-"+slot};change.AddToClassList("lobby-bot-control");row.Add(change);}
                root.Q(slot < 3 ? "lobby-cyan-members" : "lobby-amber-members").Add(row);
            }
        }
    }
}
