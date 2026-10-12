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
        private NetworkRoom shownRoom;
        private string discoverySignature = "", lobbySignature = "";
        private TextField Field(string name) => root.Q<TextField>(name);
        private DropdownField Drop(string name) => root.Q<DropdownField>(name);
        private void Text(string name, string value) => root.Q<Label>(name).text = value;
        private void Show(string name, bool visible) => root.Q(name).EnableInClassList("hidden", !visible);
        public NetworkUI(AppController controller, VisualElement ui)
        {
            app = controller; root = ui;
            foreach (string name in new[] { "network-character", "lobby-character" }) { Drop(name).choices = characters; Drop(name).SetValueWithoutNotify(characters[0]); }
            foreach (string name in new[] { "network-team", "lobby-team" }) { Drop(name).choices = teams; Drop(name).SetValueWithoutNotify(teams[0]); }
            Field("network-player-name").maxLength = 24; Field("network-room-name").maxLength = 32;
            Field("network-player-name").SetValueWithoutNotify(PlayerPrefs.GetString("soccer.playerName", "球员"));
            foreach (string name in new[] { "network-room-password", "network-join-password" }) { Field(name).isPasswordField = true; Field(name).maxLength = 64; }
            root.Q<Toggle>("network-password-enabled").RegisterValueChangedCallback(e => Field("network-room-password").SetEnabled(e.newValue));
            Field("network-room-password").SetEnabled(false);
            Button("network-back", app.BackFromNetwork); Button("lobby-leave", app.BackFromNetwork);
            Button("network-create-tab", () => app.OpenNetwork(true)); Button("network-join-tab", () => app.OpenNetwork(false));
            Button("network-create", () =>
            {
                var options = Options(); options.Name = Field("network-room-name").value; options.Port = root.Q<IntegerField>("network-port").value;
                options.UsePassword = app.Route == ConnectionRoute.Internet && root.Q<Toggle>("network-password-enabled").value;
                options.Password = Field("network-room-password").value;
                app.CreateNetworkRoom(options); Field("network-room-password").SetValueWithoutNotify("");
            });
            Button("network-join", () =>
            { var options = Options(); options.Password = Field("network-join-password").value; app.JoinNetworkRoom(Field("network-address").value, options); Field("network-join-password").SetValueWithoutNotify(""); });
            Button("network-refresh", () => { app.Discovery?.Pump(Time.realtimeSinceStartup, true); Refresh(); });
            Button("lobby-ready", () => { var member = app.Room?.Lobby.Members.FirstOrDefault(m => m.Slot == app.Room.LocalSlot); if (member != null) app.Room.SetReady(!member.Ready); });
            Button("lobby-start", () => { if (app.Room?.CanStart == true) app.Room.StartMatch(app.RulesAsset.CreateRules()); });
            Button("lobby-copy-address", () => { GUIUtility.systemCopyBuffer = Field("lobby-address").value; });
            Drop("lobby-character").RegisterValueChangedCallback(e => Choice()); Drop("lobby-team").RegisterValueChangedCallback(e => Choice());
        }
        private void Button(string name, Action action)
        { root.Q<Button>(name).clicked += () => { app.Audio.PlayClick(); action(); }; }
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
                bool lan = app.Route == ConnectionRoute.LAN; bool busy = app.Room != null;
                bool canSetUp = lan || app.EntryMode == GameEntryMode.Online;
                Show("network-profile", canSetUp);
                Text("network-title", lan ? "局域网房间" : "互联网房间");
                Text("network-description", lan ? "和同一网络的朋友联机，最多 6 人；空位由 AI 补齐。" : "通过房主公网 IP 或域名加入；房主需开放所选端口或配置端口映射。");
                Show("network-create-form", canSetUp && app.NetworkCreating); Show("network-join-form", canSetUp && !app.NetworkCreating);
                Show("network-internet-options", !lan); Show("network-join-password", !lan); Show("network-lan-discovery", lan);
                Text("network-address-help", lan ? "例如 192.168.1.10:27860；找不到房间时可直接输入房主地址。" : "输入朋友提供的公网 IP 或域名:端口，例如 play.example.com:27860。");
                Text("network-status", app.NetworkStatus);
                if (lan && app.NetworkStatus.Length == 0) Text("network-status", app.Discovery?.Status ?? "");
                root.Q("network-create-tab").EnableInClassList("route-selected", app.NetworkCreating);
                root.Q("network-join-tab").EnableInClassList("route-selected", !app.NetworkCreating);
                foreach (string name in new[] { "network-create", "network-join" }) root.Q<Button>(name).SetEnabled(!busy && canSetUp);
                foreach (string name in new[] { "network-create-tab", "network-join-tab" }) root.Q<Button>(name).SetEnabled(!busy);
                RefreshDiscovered();
            }
            if (app.Page == AppPage.RoomLobby && app.Room?.Lobby != null) RefreshLobby();
        }
        private void RefreshDiscovered()
        {
            var rooms = app.Discovery?.Rooms ?? new DiscoveredRoom[0];
            string signature = string.Join("|", rooms.Select(r => r.Address + r.Lobby.Name + r.Lobby.Members.Length + r.Lobby.Running)) + (app.Room != null);
            Show("network-room-list-empty", rooms.Count == 0);
            if (signature == discoverySignature) return; discoverySignature = signature;
            var list = root.Q("network-room-list"); list.Clear();
            foreach (var room in rooms)
            {
                var row = new VisualElement(); row.AddToClassList("row"); row.AddToClassList("discovery-room");
                row.Add(new Label(room.Lobby.Name + " · " + room.Lobby.Members.Length + "/6\n" + room.Address + (room.Lobby.Running ? " · 比赛中" : "")));
                var join = new Button(() => { Field("network-address").SetValueWithoutNotify(room.Address); app.JoinNetworkRoom(room.Address, Options()); }) { text = "加入" };
                join.SetEnabled(!room.Lobby.Running && room.Lobby.Members.Length < 6 && app.Room == null); row.Add(join); list.Add(row);
            }
        }
        private void RefreshLobby()
        {
            var room = app.Room; var lobby = room.Lobby; var local = lobby.Members.FirstOrDefault(m => m.Slot == room.LocalSlot);
            Text("lobby-route", lobby.Route == NetworkRoute.LAN ? "LAN · 局域网" : "INTERNET · 互联网"); Text("lobby-title", lobby.Name);
            Text("lobby-summary", $"{lobby.Members.Length}/6 人 · 3v3 · 空位由 AI 补齐" + (lobby.RequiresPassword ? " · 密码房间" : " · 无密码房间"));
            Text("lobby-status", app.NetworkStatus);
            if (local != null)
            {
                Drop("lobby-character").SetValueWithoutNotify(local.Character == CharacterId.Thor ? characters[1] : characters[0]);
                Drop("lobby-team").SetValueWithoutNotify(local.Team == Team.Amber ? teams[1] : teams[0]);
                root.Q<Button>("lobby-ready").text = local.Ready ? "取消准备" : "准备";
            }
            Show("lobby-ready", !room.IsHost); Show("lobby-start", room.IsHost);
            root.Q<Button>("lobby-start").SetEnabled(room.CanStart);
            root.Q<Button>("lobby-ready").SetEnabled(!lobby.Running);
            Drop("lobby-character").SetEnabled(!lobby.Running); Drop("lobby-team").SetEnabled(!lobby.Running);
            if (shownRoom != room)
            {
                shownRoom = room; lobbySignature = "";
                string address = room.IsHost ? lobby.Route == NetworkRoute.LAN
                    ? (NetworkRoom.LocalAddresses().FirstOrDefault() ?? "127.0.0.1") + ":" + room.Port
                    : Field("network-public-address").value.Trim() : Field("network-address").value;
                if (room.IsHost && address.Length > 0 && !address.Contains(":")) address += ":" + room.Port;
                Field("lobby-address").SetValueWithoutNotify(address);
                Field("lobby-address").isReadOnly = !room.IsHost;
            }
            Text("lobby-address-note", lobby.Route == NetworkRoute.LAN ? "房主本机地址：" + string.Join(" / ", NetworkRoom.LocalAddresses())
                : room.IsHost ? "填写可访问的公网邀请地址后复制给朋友；加入密码只在入房时验证。" : "当前为公网直连；离开后可用此地址重新加入。比赛进行中暂不接收新球员。");
            root.Q<Button>("lobby-copy-address").SetEnabled(!string.IsNullOrWhiteSpace(Field("lobby-address").value));
            string signature = string.Join("|", lobby.Members.Select(m => m.Slot + ":" + m.Name + m.AccountId + m.Character + m.Ready)) + room.LocalSlot;
            if (signature == lobbySignature) return; lobbySignature = signature;
            root.Q("lobby-cyan-members").Clear(); root.Q("lobby-amber-members").Clear();
            for (int slot = 0; slot < 6; slot++)
            {
                var member = lobby.Members.FirstOrDefault(m => m.Slot == slot); var row = new VisualElement(); row.AddToClassList("network-member");
                row.EnableInClassList("local-member", slot == room.LocalSlot);
                var name = new Label(member == null ? "AI 空位" : member.Name + (member.Host ? " · 房主" : "") + (slot == room.LocalSlot ? " · 你" : "")); name.AddToClassList("network-member-name"); row.Add(name);
                var detail = new Label(member == null ? "开始时自动补齐" : (member.AccountId.Length > 0 ? "帐号 " + member.AccountId + " · " : "") + (member.Character == CharacterId.Thor ? "雷神" : "枪兵") + " · " + (member.Ready ? "已准备" : "未准备")); detail.AddToClassList("network-member-detail"); row.Add(detail);
                root.Q(slot < 3 ? "lobby-cyan-members" : "lobby-amber-members").Add(row);
            }
        }
    }
}
