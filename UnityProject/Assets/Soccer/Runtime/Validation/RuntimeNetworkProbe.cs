using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Soccer.Core;
using Soccer.Core.Networking;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    /// <summary>Explicit CLI-only two-process multiplayer test. Uses actual UI callbacks and sockets.</summary>
    public sealed class RuntimeNetworkProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        { public bool passed; public string role, route, platform; public int localPlayerId; public long tick; public string[] checks, errors; }
        private readonly List<string> checks = new List<string>(), errors = new List<string>();
        private AppController app;
        private string output, role, route;
        private int port;
        private const string TestPassword = "network-probe-017";
        private string Argument(string key, string fallback)
        { var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback; }
        private IEnumerator Start()
        {
            app = GetComponent<AppController>(); role = Argument("--soccer-network-role", "host"); route = Argument("--soccer-network-route", "lan");
            port = int.Parse(Argument("--soccer-network-port", "28560")); output = Argument("--soccer-report-dir", Path.Combine(Application.persistentDataPath, "NetworkSmoke")); Directory.CreateDirectory(output);
            Application.logMessageReceived += Error;
            float deadline = Time.realtimeSinceStartup + 15;
            while (!app.StartupReady && Time.realtimeSinceStartup < deadline) yield return null;
            Check(app.StartupReady, "real game startup completes");
            Check(app.Page == AppPage.Login, "startup requires login or explicit offline entry");
            var root = app.UI.Root;
            Check(root.Q<TextField>("login-password").isPasswordField, "account password uses a masked field");
            if (route == "internet")
            {
                string account = role == "host" ? "a" : "b";
                root.Q<TextField>("login-name").value = account;
                root.Q<TextField>("login-password").value = "incorrect"; yield return Click("login-submit");
                deadline = Time.realtimeSinceStartup + 10; while (app.LoginBusy && Time.realtimeSinceStartup < deadline) yield return null;
                Check(app.Account == null && app.LoginStatus.Contains("错误"), "real login server rejects incorrect credentials");
                root.Q<TextField>("login-password").value = account; yield return Click("login-submit");
                deadline = Time.realtimeSinceStartup + 10; while (app.LoginBusy && Time.realtimeSinceStartup < deadline) yield return null;
                Check(app.Account?.Id == account && app.EntryMode == GameEntryMode.Online && root.Q<TextField>("login-password").value.Length == 0,
                    "network login succeeds through service and clears password field");
                if (app.Account == null) { errors.Add(app.LoginStatus); Finish(); yield break; }
            }
            else
            {
                yield return Click("login-offline");
                Check(app.EntryMode == GameEntryMode.Offline && app.Account == null, "offline entry makes LAN available without account service");
            }
            app.OpenMode(GamePlaylist.Normal); app.SelectConnection(route == "lan" ? ConnectionRoute.LAN : ConnectionRoute.Internet); app.OpenNetwork(role == "host");
            yield return new WaitForSecondsRealtime(.4f);
            root.Q<TextField>("network-player-name").value = role == "host" ? "联机房主" : "联机客人";
            root.Q<DropdownField>("network-character").value = role == "host" ? "枪兵" : "雷神";
            root.Q<DropdownField>("network-team").value = role == "host" ? "青队" : "橙队";
            Check(root.Q<TextField>("network-room-password").isPasswordField && root.Q<TextField>("network-join-password").isPasswordField, "both room passwords use masked fields");
            if (role == "host")
            {
                root.Q<TextField>("network-room-name").value = "联机验证 · " + route;
                root.Q<IntegerField>("network-port").value = port;
                if (route == "internet")
                {
                    root.Q<Toggle>("network-password-enabled").value = true;
                    root.Q<TextField>("network-room-password").value = TestPassword;
                    root.Q<TextField>("network-public-address").value = "127.0.0.1:" + port;
                    Check(root.Q<TextField>("network-room-password").enabledSelf, "enabling room password enables password entry");
                }
                yield return Capture("00-room-setup");
                yield return Click("network-create");
                Check(app.Page == AppPage.RoomLobby && app.Room != null && app.Room.IsHost, "create button opens a real listening room");
                Check(root.Q<TextField>("network-room-password").value.Length == 0, "created room clears password field");
                yield return Capture("01-host-room");
                deadline = Time.realtimeSinceStartup + 20;
                while ((app.Room == null || app.Room.Lobby.Members.Length < 2 || !app.Room.CanStart) && Time.realtimeSinceStartup < deadline) yield return null;
                Check(app.Room != null && app.Room.Lobby.Members.Length == 2 && app.Room.CanStart, "second process joins and readiness reaches host");
                if (app.Room == null || app.Room.Lobby.Members.Length < 2 || !app.Room.CanStart) { Finish(); yield break; }
                if (route == "internet") Check(app.Room.Lobby.Members[0].AccountId == "a" && app.Room.Lobby.Members[1].AccountId == "b", "host verifies and displays both account identities");
                yield return Capture("02-host-ready-room"); yield return Click("lobby-start");
                Check(app.Page == AppPage.Match && app.IsNetworkMatch, "host start button starts authoritative six-player match");
                Check(app.Simulation.State.Players[3].Stats.Id == CharacterId.Thor, "guest selected character appears in opposing player slot");
                var remoteOrigin = app.Simulation.State.Players[3].Position;
                yield return new WaitForSecondsRealtime(4.8f);
                Check(Vec2.Distance(app.Simulation.State.Players[3].Position, remoteOrigin) > .4f, "remote movement changes only the guest-controlled authoritative player");
                // Give the guest a real held ball so its network kick can be verified independently of AI contention.
                foreach (var player in app.Simulation.State.Players)
                    if (player.Id != 3) Set(player, nameof(PlayerState.Position), new Vec2(-18 + player.Id * 5, -12));
                var receiver = app.Simulation.State.Players[3];
                Set(receiver, nameof(PlayerState.Health), 100f); Set(receiver, nameof(PlayerState.Stun), 0f); Set(receiver, nameof(PlayerState.PushRemaining), 0f); Set(receiver, nameof(PlayerState.KickCooldown), 0f);
                Set(app.Simulation.State.Ball, nameof(BallState.OwnerId), 3); Set(app.Simulation.State.Ball, nameof(BallState.LastTouchId), 3);
                Set(app.Simulation.State.Ball, nameof(BallState.PossessionContactRemaining), .25f);
                deadline = Time.realtimeSinceStartup + 10;
                while (receiver.Kicks < 1 && Time.realtimeSinceStartup < deadline) yield return null;
                Check(receiver.Kicks == 1 && receiver.KickCooldown > 0, "guest press/release executes exactly one real kick on host");
                yield return Capture("03-host-synchronized-match");
                deadline = Time.realtimeSinceStartup + 10;
                while (app.Room != null && app.Room.Lobby.Members.Length > 1 && Time.realtimeSinceStartup < deadline) yield return null;
                Check(app.Room != null && !app.Room.IsHumanSlot(3), "client exit releases remote slot for AI takeover");
            }
            else
            {
                if (route == "lan")
                {
                    deadline = Time.realtimeSinceStartup + 12;
                    DiscoveredRoom found = null;
                    while (found == null && Time.realtimeSinceStartup < deadline)
                    { if (app.Discovery != null) foreach (var room in app.Discovery.Rooms) if (room.Lobby.Port == port) found = room; yield return null; }
                    Check(found != null, "second game process discovers host over UDP");
                    root.Q<TextField>("network-address").value = found?.Address ?? "127.0.0.1:" + port;
                }
                else { root.Q<TextField>("network-address").value = "127.0.0.1:" + port; root.Q<TextField>("network-join-password").value = TestPassword; }
                yield return Click("network-join");
                deadline = Time.realtimeSinceStartup + 15;
                while (app.Page != AppPage.RoomLobby && Time.realtimeSinceStartup < deadline)
                {
                    // Simultaneous process startup may finish client loading before the host opens its listener.
                    if (app.Room == null)
                    {
                        if (route == "internet") root.Q<TextField>("network-join-password").value = TestPassword;
                        yield return new WaitForSecondsRealtime(.3f); yield return Click("network-join");
                    }
                    yield return null;
                }
                Check(app.Room != null && app.Room.Connected && app.LocalPlayerId == 3, "join button admits client to its chosen opposing team");
                if (app.Room == null || !app.Room.Connected) { errors.Add(app.NetworkStatus); Finish(); yield break; }
                Check(app.Room.Lobby.RequiresPassword == (route == "internet"), "room password setting arrives through live handshake");
                if (route == "internet") Check(app.Room.Lobby.Members[0].AccountId == "a" && app.Room.Lobby.Members[1].AccountId == "b", "verified account identities synchronize to guest lobby");
                Check(root.Q<TextField>("network-join-password").value.Length == 0, "joining clears submitted password field");
                yield return Capture("01-client-room"); yield return Click("lobby-ready");
                deadline = Time.realtimeSinceStartup + 15;
                while (app.Page != AppPage.Match && Time.realtimeSinceStartup < deadline) yield return null;
                Check(app.IsNetworkMatch && !app.Room.IsHost && app.LocalPlayerId == 3, "client receives start and binds its own player");
                if (!app.IsNetworkMatch) { Finish(); yield break; }
                Check(app.View.LocalPlayerId == 3 && app.Audio.LocalPlayerId == 3 && root.Q<Label>("local-name").text.Contains("雷神"), "model highlight, sound and HUD follow the local guest rather than host");
                var origin = app.Simulation.State.Players[3].Position;
                app.NetworkInputOverride = new PlayerCommand { Move = new Vec2(0, 1), Aim = new Vec2(0, 1) };
                yield return new WaitForSecondsRealtime(1.3f); app.NetworkInputOverride = default(PlayerCommand);
                Check(app.Simulation.State.Players[3].Position.Y > origin.Y + .4f, "client sees authoritative remote movement replicated back");
                app.SetPaused(true); long before = app.Simulation.State.Tick; yield return new WaitForSecondsRealtime(.8f);
                Check(app.Simulation.State.Tick > before + 20 && root.Q<Label>("pause-title").text == "联机菜单", "local menu keeps shared match and snapshots running");
                yield return Capture("02-client-local-menu"); app.SetPaused(false);
                deadline = Time.realtimeSinceStartup + 12;
                while (app.Simulation.State.Ball.OwnerId != 3 && Time.realtimeSinceStartup < deadline) yield return null;
                Check(app.Simulation.State.Ball.OwnerId == 3, "client receives host ball ownership before shooting");
                var help = (SkillHelp)typeof(GameUI).GetMethod("DescribeSkill", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(app.UI, new object[] { "kick" });
                Check(help.State == "准备就绪", "guest kick tooltip recognizes its own possession");
                app.NetworkInputOverride = new PlayerCommand { Aim = new Vec2(1, 0), KickPressed = true, KickHeld = true };
                yield return new WaitForSecondsRealtime(.04f);
                app.NetworkInputOverride = new PlayerCommand { Aim = new Vec2(1, 0), KickReleased = true };
                yield return new WaitForSecondsRealtime(.04f); app.NetworkInputOverride = default(PlayerCommand);
                deadline = Time.realtimeSinceStartup + 5;
                while (app.Simulation.State.Players[3].Kicks < 1 && Time.realtimeSinceStartup < deadline) yield return null;
                Check(app.Simulation.State.Players[3].Kicks == 1 && app.Simulation.State.Players[3].KickCooldown > 0 && app.Simulation.State.Ball.OwnerId != 3,
                    "client receives kick, cooldown and released football from host");
                yield return new WaitForSecondsRealtime(.3f); yield return Capture("03-client-synchronized-kick");
            }
            Finish();
        }
        private static void Set(object obj, string name, object value) => obj.GetType().GetProperty(name).SetValue(obj, value);
        private IEnumerator Click(string name)
        {
            var button = app.UI.Root.Q<Button>(name); button.Focus(); yield return null;
            using (var evt = NavigationSubmitEvent.GetPooled()) button.SendEvent(evt); yield return null;
        }
        private IEnumerator Capture(string name)
        { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png")); yield return null; yield return null; }
        private void Check(bool passed, string name)
        { checks.Add((passed ? "PASS " : "FAIL ") + name); if (!passed) errors.Add(name); }
        private void Error(string message, string stack, LogType type)
        { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors.Add(message); }
        private void Finish()
        {
            app.NetworkInputOverride = null;
            File.WriteAllText(Path.Combine(output, "network-smoke.json"), JsonUtility.ToJson(new Report { passed = errors.Count == 0, role = role, route = route, platform = Application.platform.ToString(),
                localPlayerId = app.LocalPlayerId, tick = app.Simulation?.State.Tick ?? 0, checks = checks.ToArray(), errors = errors.ToArray() }, true));
            Application.logMessageReceived -= Error; app.GoHome(); Application.Quit(errors.Count == 0 ? 0 : 1);
        }
    }
}
