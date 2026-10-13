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
        private string codeFile;
        private const string TestPassword = "network-probe-017";
        private string Argument(string key, string fallback)
        { var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback; }
        private IEnumerator Start()
        {
            app = GetComponent<AppController>(); role = Argument("--soccer-network-role", "host"); route = Argument("--soccer-network-route", "lan");
            output = Argument("--soccer-report-dir", Path.Combine(Application.persistentDataPath, "NetworkSmoke")); Directory.CreateDirectory(output);
            codeFile = Argument("--soccer-network-code-file", Path.Combine(Path.GetDirectoryName(output), "room-code.txt"));
            Application.logMessageReceived += Error;
            float deadline = Time.realtimeSinceStartup + 15;
            while (!app.StartupReady && Time.realtimeSinceStartup < deadline) yield return null;
            Check(app.StartupReady, "real game startup completes");
            Check(app.Page == AppPage.Login, "startup requires login or explicit offline entry");
            var root = app.UI.Root;
            Check(root.Q("login-server") == null, "login service address is configured by deployment, not players");
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
            Check(!root.Q<Foldout>("network-profile").value, "player setup starts collapsed");
            Check(!PointerHits(root.Q("network-player-name")), "collapsed player setup hides its fields from interaction");
            Check(!root.Q<Foldout>("network-join-secret").value, "optional join password stays collapsed initially");
            yield return Capture("00-default-room-form");
            if (role == "host") yield return CheckRoomSizes("network-page", "network-create", "setup");
            yield return Expand("network-profile");
            Check(Visible(root.Q("network-player-name")) && !Visible(root.Q("network-character")) && Visible(root.Q("network-team")), "room setup exposes nickname and team while role selection waits for draft");
            root.Q<TextField>("network-player-name").value = role == "host" ? "联机房主" : "联机客人";
            root.Q<DropdownField>("network-team").value = role == "host" ? "青队" : "橙队";
            root.Q<Foldout>("network-profile").value = false;
            Check(root.Q<TextField>("network-room-password").isPasswordField && root.Q<TextField>("network-join-password").isPasswordField, "both room passwords use masked fields");
            if (role == "host")
            {
                root.Q<TextField>("network-room-name").value = "联机验证 · " + route;
                Check(!Visible(root.Q("network-room-password")), "create password field stays hidden until enabled");
                Check(root.Q("network-port") == null, "players create rooms without port fields");
                if (route == "internet")
                {
                    root.Q<Toggle>("network-password-enabled").value = true;
                    root.Q<TextField>("network-room-password").value = TestPassword;
                    yield return null;
                    Check(root.Q<TextField>("network-room-password").enabledSelf && Visible(root.Q("network-room-password")), "enabling room password reveals password entry");
                }
                yield return Capture("00-room-setup");
                yield return Click("network-create");
                Check(app.Page == AppPage.RoomLobby && app.Room != null && app.Room.IsHost, "create button opens a real listening room");
                Check(root.Q<TextField>("network-room-password").value.Length == 0, "created room clears password field");
                Check(app.Room.Port > 0, "program selects a usable listening port automatically");
                deadline = Time.realtimeSinceStartup + 18;
                while (!app.RoomCodeReady && Time.realtimeSinceStartup < deadline) yield return null;
                Check(app.RoomCodeReady && RoomCode.IsValid(app.Room.Lobby.Code), "room obtains a six-character uppercase alphanumeric code");
                Check(root.Q("network-public-address") == null, "players do not configure the Internet host endpoint");
                yield return new WaitForSecondsRealtime(.4f);
                var copyButton = root.Q<Button>("lobby-copy-address"); var lobbyBounds = root.Q("room-page").worldBound;
                Check(copyButton.worldBound.width > 0 && lobbyBounds.Contains(copyButton.worldBound.center) && copyButton.worldBound.xMax <= lobbyBounds.xMax,
                    "copy room-code button remains visible");
                yield return Click("lobby-copy-address");
                Check(GUIUtility.systemCopyBuffer == app.Room.Lobby.Code && RoomCode.IsValid(GUIUtility.systemCopyBuffer) && root.Q<TextField>("lobby-address").isReadOnly, "copy button supplies exactly the six-character room code");
                yield return CheckRoomSizes("room-page", "lobby-copy-address", "lobby");
                File.WriteAllText(codeFile, GUIUtility.systemCopyBuffer);
                yield return Capture("01-host-room");
                deadline = Time.realtimeSinceStartup + 20;
                while ((app.Room == null || app.Room.Lobby.Members.Length < 2 || !app.Room.CanStart) && Time.realtimeSinceStartup < deadline) yield return null;
                Check(app.Room != null && app.Room.Lobby.Members.Length == 2 && app.Room.CanStart, "second process joins and readiness reaches host");
                if (app.Room == null || app.Room.Lobby.Members.Length < 2 || !app.Room.CanStart) { Finish(); yield break; }
                if (route == "internet") Check(app.Room.Lobby.Members[0].AccountId == "a" && app.Room.Lobby.Members[1].AccountId == "b", "host verifies both account identities");
                yield return Capture("02-host-ready-room"); yield return Click("lobby-start");
                yield return ParticipateDraft();
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
                    { if (app.Discovery != null) foreach (var room in app.Discovery.Rooms) if (room.Lobby.Name == "联机验证 · " + route) found = room; yield return null; }
                    Check(found != null, "second game process discovers host over UDP");
                    if (Argument("--soccer-network-join-mode", "list") == "code")
                        root.Q<TextField>("network-address").value = File.ReadAllText(codeFile).ToLowerInvariant();
                    else if (Argument("--soccer-network-join-mode", "list") == "ip")
                        root.Q<TextField>("network-address").value = "127.0.0.1";
                    else if (found != null) yield return Click("network-discovered-" + found.Lobby.Id);
                }
                else { root.Q<TextField>("network-address").value = File.ReadAllText(codeFile).ToLowerInvariant(); root.Q<TextField>("network-join-password").value = TestPassword; }
                if (route == "internet")
                {
                    yield return Expand("network-join-secret");
                    Check(Visible(root.Q("network-join-password")), "optional join-password expander exposes masked entry");
                    string realCode = root.Q<TextField>("network-address").value;
                    root.Q<TextField>("network-address").value = "!BAD!"; yield return Click("network-join");
                    Check(app.Room == null && !app.NetworkLookupBusy && app.NetworkStatus.Contains("6 位"), "invalid room code is rejected before network lookup");
                    root.Q<TextField>("network-address").value = "ZZZZZZ"; yield return Click("network-join");
                    deadline = Time.realtimeSinceStartup + 8; while (app.NetworkLookupBusy && Time.realtimeSinceStartup < deadline) yield return null;
                    Check(app.Room == null && app.NetworkStatus.Contains("不存在"), "unknown room code reports a directory miss");
                    root.Q<TextField>("network-address").value = realCode;
                    root.Q<TextField>("network-join-password").value = "incorrect"; yield return Click("network-join");
                    deadline = Time.realtimeSinceStartup + 8;
                    while (!app.NetworkStatus.Contains("密码不正确") && Time.realtimeSinceStartup < deadline) yield return null;
                    Check(app.Room == null && app.NetworkStatus.Contains("密码不正确"), "code lookup still enforces the room password on the actual host");
                    root.Q<TextField>("network-join-password").value = TestPassword;
                }
                if (app.Room == null && !app.NetworkLookupBusy) yield return Click("network-join");
                deadline = Time.realtimeSinceStartup + 15;
                while (app.Page != AppPage.RoomLobby && Time.realtimeSinceStartup < deadline)
                {
                    // Simultaneous process startup may finish client loading before the host opens its listener.
                    if (app.Room == null && !app.NetworkLookupBusy)
                    {
                        if (route == "internet") root.Q<TextField>("network-join-password").value = TestPassword;
                        yield return new WaitForSecondsRealtime(.3f); yield return Click("network-join");
                    }
                    yield return null;
                }
                Check(app.Room != null && app.Room.Connected && app.LocalPlayerId == 3, "join button admits client to its chosen opposing team");
                if (app.Room == null || !app.Room.Connected) { errors.Add(app.NetworkStatus); Finish(); yield break; }
                Check(!root.Q<Foldout>("lobby-options").value && !Visible(root.Q("lobby-character")), "lobby role and team controls start collapsed");
                yield return Expand("lobby-options");
                Check(!Visible(root.Q("lobby-character")) && Visible(root.Q("lobby-team")), "lobby exposes team control and defers roles to the draft");
                root.Q<Foldout>("lobby-options").value = false;
                Check(app.Room.Lobby.Code == File.ReadAllText(codeFile), "room code synchronizes to guest lobby after lookup and admission");
                Check(app.Room.Lobby.RequiresPassword == (route == "internet"), "room password setting arrives through live handshake");
                if (route == "internet") Check(app.Room.Lobby.Members[0].AccountId == "a" && app.Room.Lobby.Members[1].AccountId == "b", "verified account identities synchronize internally");
                Check(root.Q<TextField>("network-join-password").value.Length == 0, "joining clears submitted password field");
                yield return Capture("01-client-room"); yield return Click("lobby-ready");
                yield return ParticipateDraft();
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
            if (role == "host" && route == "internet")
            {
                app.BackFromNetwork(); yield return new WaitForSecondsRealtime(.5f);
                Check(app.Room == null, "leaving an Internet room cancels heartbeat and removes publication");
            }
            Finish();
        }
        private IEnumerator ParticipateDraft()
        {
            float deadline=Time.realtimeSinceStartup+18;bool sawDraft=false,locked=false;
            Vec2 position=role=="host" ? new Vec2(-14,2) : new Vec2(14,-2);
            while(app.Page!=AppPage.Match && Time.realtimeSinceStartup<deadline)
            {
                if(app.InDraft)
                {
                    if(!sawDraft)
                    {
                        sawDraft=true;Check(app.Simulation==null && app.Room.Lobby.Drafting,"selection begins after readiness, before simulation");
                        yield return Capture("02-"+role+"-draft");
                    }
                    if(app.MyDraftTurn && !locked)
                    {
                        app.SelectCharacter(role=="host" ? CharacterId.Marine : CharacterId.Thor);app.SelectDraftPosition(position);
                        yield return Click("start-button");locked=true;
                    }
                }
                yield return null;
            }
            Check(sawDraft && locked && app.Page==AppPage.Match,"both peers select and lock their own role and position before kickoff");
            if(app.Simulation!=null)Check(Vec2.Distance(app.Simulation.State.Players[app.LocalPlayerId].Position,position)<.1f,"selected local opening position synchronizes through match start");
        }
        private static void Set(object obj, string name, object value) => obj.GetType().GetProperty(name).SetValue(obj, value);
        private static bool PointerHits(VisualElement element)
        {
            if (element == null || element.worldBound.width <= 0 || element.worldBound.height <= 0) return false;
            var hit = element.panel.Pick(element.worldBound.center);
            return hit == element || element.Contains(hit);
        }
        private static bool Visible(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent) if (current.resolvedStyle.display == DisplayStyle.None || current.style.display == new StyleEnum<DisplayStyle>(DisplayStyle.None)) return false;
            return element != null;
        }
        private IEnumerator CheckRoomSizes(string pageName, string actionName, string label)
        {
            int width = Screen.width, height = Screen.height; var mode = Screen.fullScreenMode;
            foreach (var size in new[] { new Vector2Int(1024, 768), new Vector2Int(1280, 720) })
            {
                Screen.SetResolution(size.x, size.y, false); yield return new WaitForSecondsRealtime(.6f);
                var root = app.UI.Root; var page = root.Q(pageName).worldBound; var bounds = root.worldBound;
                var button = root.Q<Button>(actionName); var hit = root.panel.Pick(button.worldBound.center);
                Check(page.xMin >= bounds.xMin && page.xMax <= bounds.xMax && page.yMin >= bounds.yMin && page.yMax <= bounds.yMax,
                    label + " panel fits " + size.x + "x" + size.y);
                Check(hit == button || button.Contains(hit), label + " primary control accepts pointer at " + size.x + "x" + size.y);
                yield return Capture("00-" + label + "-" + size.x + "x" + size.y);
            }
            Screen.SetResolution(width, height, mode); yield return new WaitForSecondsRealtime(.5f);
        }
        private IEnumerator Expand(string name)
        {
            var toggle = app.UI.Root.Q<Foldout>(name).Q<Toggle>(); toggle.Focus(); yield return null;
            using (var evt = NavigationSubmitEvent.GetPooled()) toggle.SendEvent(evt); yield return null;
        }
        private IEnumerator Click(string name)
        {
            var button = app.UI.Root.Q<Button>(name); button.Focus(); yield return null;
            using (var evt = NavigationSubmitEvent.GetPooled()) {evt.target=button;button.SendEvent(evt);} yield return null;
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
