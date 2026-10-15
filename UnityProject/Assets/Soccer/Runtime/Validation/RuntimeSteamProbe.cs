using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Soccer.Core;
using Soccer.Core.Networking;
using Steamworks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    /// <summary>Real Steam API/lobby and native UDP loopback validation. Does not invite or message friends.</summary>
    public sealed class RuntimeSteamProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {public bool passed,steamInitialized,realLobby,nativeLoopback,crossAccountP2PVerified;public uint appId;public string[] checks,errors;public string platform,scope;}
        [Serializable] private sealed class JoinReport
        {public bool passed,found,connected;public uint appId;public ulong ownerBeforeJoin;public string code,status,scope;public string[] checks,errors;}
        private readonly List<string> checks=new List<string>(),errors=new List<string>();private AppController app;private string output;
        private void Check(bool pass,string name){checks.Add((pass ? "PASS " : "FAIL ")+name);if(!pass){errors.Add(name);Debug.Log("STEAM_CHECK_FAIL "+name);}}
        private IEnumerator Click(string name)
        {var button=app.UI.Root.Q<Button>(name);button.Focus();yield return null;using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}yield return null;}
        private IEnumerator Capture(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return new WaitForSecondsRealtime(.15f);}
        private IEnumerator Start()
        {
            app=GetComponent<AppController>();var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--soccer-report-dir");output=at>=0 ? args[at+1] : Path.Combine(Application.persistentDataPath,"SteamProbe");Directory.CreateDirectory(output);
            while(!app.StartupReady)yield return null;yield return Click("login-steam");
            if(app.Page==AppPage.Nickname)
            {
                Check(app.FirstNickname && !app.HasPlayerName,"first Steam login requests a game name before home");
                app.UI.Root.Q<TextField>("nickname-value").value="一二三四五六七";yield return Click("nickname-save");
                Check(app.Page==AppPage.Nickname && app.NicknameStatus.Contains("6"),"first-login name rejects more than six characters");
                app.UI.Root.Q<TextField>("nickname-value").value="蒸汽球员";yield return Click("nickname-save");
                Check(app.Page==AppPage.Home && app.PlayerName=="蒸汽球员","valid game name completes first login");
            }
            bool initialized=app.Steam.Ready;Check(initialized && app.SteamEntry,"actual Steam account initializes through the game login action");
            if(!initialized){Finish(false,false);yield break;}
            int idAt=Array.IndexOf(args,"--soccer-steam-app-id");uint expectedId=idAt>=0 && idAt+1<args.Length ? uint.Parse(args[idAt+1]) : 480;
            Check(app.Steam.AppId==expectedId,"Steam runtime reports the configured application id");
            Check(SteamAccount.IsValid(app.PlayerAccountId) && app.WalletProfile.Length>0,"Steam identity has a separate wallet and personal profile namespace");
            if(Array.IndexOf(args,"--soccer-community-link-smoke")>=0)
            {
                float until=Time.realtimeSinceStartup+15;while(app.FriendsBusy && Time.realtimeSinceStartup<until)yield return null;
                ulong steamUser=app.Steam.UserId;string gameId="s"+Guid.NewGuid().ToString("N").Substring(0,7);
                app.OpenFriends();yield return Click("friends-game-tab");yield return Click("friends-link-game");
                Check(app.UI.Root.Q("community-login-dialog").resolvedStyle.display!=DisplayStyle.None,"Steam player can open the independent game-account connection dialog");
                yield return Click("community-login-register");app.UI.Root.Q<TextField>("community-account").value=gameId;app.UI.Root.Q<TextField>("community-name").value="社交球员";app.UI.Root.Q<TextField>("community-password").value="Steam-social-test-029";
                yield return Click("community-login-submit");until=Time.realtimeSinceStartup+12;while(app.CommunityBusy && Time.realtimeSinceStartup<until)yield return null;
                Check(app.SocialOnline && app.SocialAccountId==gameId && app.SteamEntry && app.Steam.Ready && app.Steam.UserId==steamUser,"independent social registration preserves the authenticated Steam session");
                Check(app.UI.Root.Q("community-login-dialog").resolvedStyle.display==DisplayStyle.None && app.UI.Root.Q<TextField>("community-password").value.Length==0,"successful social connection closes the dialog and clears the password");
                yield return Capture("steam-game-friends-connected");app.GoHome();
            }
            app.OpenProfile();yield return Click("profile-rename");app.UI.Root.Q<TextField>("nickname-value").value="一二三四五六";yield return Click("nickname-save");
            Check(app.Page==AppPage.Profile && app.PlayerName=="一二三四五六" && app.VisibleProfile.DisplayName==app.PlayerName,"profile rename accepts six characters and immediately updates the game profile");
            yield return Click("profile-rename");app.UI.Root.Q<TextField>("nickname-value").value="未保存";yield return Click("nickname-cancel");
            Check(app.PlayerName=="一二三四五六","canceling a rename preserves the saved account name");
            app.OpenLogin();yield return Click("login-steam");Check(app.Page==AppPage.Home && app.PlayerName=="一二三四五六","returning Steam account reuses its saved game name without first-login prompt");
            int externalAt=Array.IndexOf(args,"--soccer-steam-join-code");
            if(externalAt>=0 && externalAt+1<args.Length){yield return JoinExternalRoom(args[externalAt+1],args);yield break;}
            app.OpenFriends();yield return Click("friends-steam-tab");
            Check(!app.UI.Root.Q("friends-steam").ClassListContains("hidden") && app.UI.Root.Q("friends-online").ClassListContains("hidden"),"Steam login presents native friends separately from HTTP debug friends");app.GoHome();
            app.OpenMode(GamePlaylist.Normal);app.SelectSteamConnection();yield return new WaitForSecondsRealtime(.3f);
            Check(app.Route==ConnectionRoute.Steam && app.UI.Root.Q("steam-route").ClassListContains("route-selected"),"Steam connection mode is selectable without HTTP debug login");
            app.OpenNetwork(true);app.UI.Root.Q<TextField>("network-room-name").value="技能足球测试房间";app.UI.Root.Q<Toggle>("network-password-enabled").value=true;app.UI.Root.Q<TextField>("network-room-password").value="smoke-password";yield return Click("network-create");
            float deadline=Time.realtimeSinceStartup+18;while(app.Page!=AppPage.RoomLobby && Time.realtimeSinceStartup<deadline)yield return null;
            bool lobby=app.Page==AppPage.RoomLobby && app.Steam.LobbyId!=0;Check(lobby,"actual Valve matchmaking lobby is created from game UI");
            if(!lobby){errors.Add(app.NetworkStatus);Finish(false,false);yield break;}
            var description=app.Steam.Describe(app.Steam.LobbyId);
            Check(description!=null && description.Code==app.Room.Lobby.Code && description.RoomId==app.Room.Lobby.Id && description.Owner==app.Steam.UserId,"lobby metadata is scoped to this game, protocol and authenticated owner");
            Check(description?.HostName==app.PlayerName && description.Players==1 && description.Password,"Steam search metadata publishes game host name, player count and password lock");
            var lobbyId=new CSteamID(app.Steam.LobbyId);ulong advertisedOther=app.Steam.UserId==76561197960265729UL ? 76561197960265730UL : 76561197960265729UL;
            SteamMatchmaking.SetLobbyData(lobbyId,"owner",advertisedOther.ToString());
            Check(app.Steam.Describe(app.Steam.LobbyId)!=null && app.Steam.Describe(app.Steam.LobbyId,true)==null,"joined lobby rejects advertised owner substitution before P2P connection");
            SteamMatchmaking.SetLobbyData(lobbyId,"owner",app.Steam.UserId.ToString());
            Check(app.Room.Lobby.Bots.Length==0 && !app.Room.Lobby.Members[0].Ready,"Steam lobby retains explicit empty slots and host readiness");
            Check(app.UI.Root.Q<Button>("lobby-steam-invite").enabledSelf && app.RoomCodeReady,"Steam friend invitation control and six-character join code are available");
            yield return Click("lobby-copy-address");Check(GUIUtility.systemCopyBuffer==app.Room.Lobby.Code,"Steam room copy preserves the simple room code");
            yield return new WaitForSecondsRealtime(.4f);yield return Capture("steam-real-lobby");
            bool found=false;for(int i=0;i<4 && !found;i++)
            {
                app.Steam.FindRooms(app.Room.Lobby.Code);deadline=Time.realtimeSinceStartup+16;while(app.Steam.Busy && Time.realtimeSinceStartup<deadline)yield return null;
                found=app.Steam.Rooms.Any(r=>r.LobbyId==app.Steam.LobbyId);if(!found)yield return new WaitForSecondsRealtime(.75f);
            }
            Check(found,"Valve lobby search finds this game's room through its six-character code");
            if(found)
            {
                var held=app.Room;typeof(AppController).GetProperty("Room").SetValue(app,null);typeof(AppController).GetProperty("NetworkCreating").SetValue(app,false);typeof(AppController).GetProperty("Page").SetValue(app,AppPage.NetworkMenu);app.UI.ShowPage(AppPage.NetworkMenu);
                Check(app.UI.Root.Q("steam-room-lock-"+app.Steam.LobbyId)!=null,"Steam room card displays a visible password lock");
                Screen.SetResolution(1280,720,false);yield return new WaitForSecondsRealtime(.4f);yield return Capture("steam-room-list");yield return Click("steam-room-"+app.Steam.LobbyId);
                Check(app.UI.JoinDialogOpen && app.UI.Root.Q<Foldout>("network-join-secret").value && app.UI.Root.Q<TextField>("network-join-password").isPasswordField,"locked list room opens a masked password dialog before joining");
                yield return Capture("steam-room-password");yield return Click("network-join-cancel");
                Check(!app.UI.JoinDialogOpen,"cancel closes room password entry without joining");
                typeof(AppController).GetProperty("Room").SetValue(app,held);typeof(AppController).GetProperty("Page").SetValue(app,AppPage.RoomLobby);app.UI.ShowPage(AppPage.RoomLobby);
            }
            app.GoHome();yield return null;
            ulong local=app.Steam.UserId,other=local==76561197960265729UL ? 76561197960265730UL : 76561197960265729UL;
            var first=new SteamNetworkingIdentity();first.SetSteamID64(local);var second=new SteamNetworkingIdentity();second.SetSteamID64(local);
            bool pair=SteamNetworkingSockets.CreateSocketPair(out var a,out var b,true,ref first,ref second);Check(pair,"real Steam Networking Sockets creates encrypted UDP loopback connections");
            if(!pair){Finish(lobby,false);yield break;}
            using(var nativeHostPeer=new SteamSocketPeer(a,local))using(var hostPeer=new IdentityFixturePeer(nativeHostPeer,SteamAccount.FromId(other)))using(var guestPeer=new SteamSocketPeer(b,local))
            using(var host=NetworkRoom.HostSteam(new RoomOptions {Route=NetworkRoute.Steam,Name="Steam回环测试",PlayerName="测试房主",PlatformAccountId=SteamAccount.FromId(local),UsePassword=true,Password="loopback-test"}))
            {
                Check(host.AcceptSteamPeer(hostPeer),"room protocol accepts the isolated logical identity fixture over native Steam packets");
                using(var guest=NetworkRoom.JoinSteam(guestPeer,new RoomOptions {Route=NetworkRoute.Steam,PlayerName="测试客人",PlatformAccountId=SteamAccount.FromId(other),Team=Team.Amber,Password="loopback-test"},host.Lobby.Id))
                {
                    deadline=Time.realtimeSinceStartup+7;while(!guest.Connected && Time.realtimeSinceStartup<deadline){host.Pump(Time.realtimeSinceStartup);guest.Pump(Time.realtimeSinceStartup);yield return null;}
                    Check(guest.Connected && guest.LocalSlot==3 && host.Lobby.Members.Length==2,"native Steam packets complete room admission and team assignment using logical identity fixtures");
                    if(guest.Connected)
                    {
                        host.FillBots();host.SetReady(true);guest.SetReady(true);
                        deadline=Time.realtimeSinceStartup+5;while(!host.CanStart && Time.realtimeSinceStartup<deadline){host.Pump(Time.realtimeSinceStartup);guest.Pump(Time.realtimeSinceStartup);yield return null;}
                        Check(host.CanStart && guest.Lobby.Bots.Length==4,"native Steam transport replicates explicit bots and every human's readiness");
                        if(host.CanStart)
                        {
                            host.BeginCountdown(app.RulesAsset.CreateRules());float began=Time.realtimeSinceStartup;
                            deadline=Time.realtimeSinceStartup+6;while(guest.Draft==null && Time.realtimeSinceStartup<deadline){host.Pump(Time.realtimeSinceStartup);guest.Pump(Time.realtimeSinceStartup);yield return null;}
                            Check(guest.Draft!=null && Time.realtimeSinceStartup-began>=2.9f,"actual Steam packets replicate three-second countdown before selection");
                            NetworkStart remote=null;guest.Started+=s=>remote=s;
                            deadline=Time.realtimeSinceStartup+8;
                            while(remote==null && Time.realtimeSinceStartup<deadline)
                            {
                                if(host.Draft?.CurrentSlot==0)host.LockDraft(CharacterId.Marine,host.Draft.Picks[0].Position);
                                if(guest.Draft?.CurrentSlot==3)guest.LockDraft(CharacterId.Thor,guest.Draft.Picks[3].Position);
                                host.Pump(Time.realtimeSinceStartup);guest.Pump(Time.realtimeSinceStartup);yield return null;
                            }
                            Check(remote!=null && remote.Lineup[3]==CharacterId.Thor && remote.Positions.Length==6,"native Steam draft locks deliver actual role and opening placement");
                            if(remote!=null)
                            {
                                var simulation=new MatchSimulation(remote.Rules,StarStrikerLoadouts.CreateRoster(),CharacterId.Marine,MatchMode.BotMatch,remote.Lineup);
                                var replica=new MatchSimulation(remote.Rules,StarStrikerLoadouts.CreateRoster(),CharacterId.Marine,MatchMode.BotMatch,remote.Lineup);guest.SnapshotReceived+=bytes=>NetworkCodec.ApplySnapshot(replica.State,bytes);
                                guest.SendInput(new PlayerCommand {Move=new Vec2(0,1)});deadline=Time.realtimeSinceStartup+3;
                                while(host.ReadInput(3).Move.Y!=1 && Time.realtimeSinceStartup<deadline){host.Pump(Time.realtimeSinceStartup);guest.Pump(Time.realtimeSinceStartup);yield return null;}
                                Check(host.ReadInput(3).Move.Y==1,"native Steam player command arrives at authoritative host");
                                typeof(MatchState).GetProperty("CyanScore").SetValue(simulation.State,2);typeof(MatchState).GetProperty("AmberScore").SetValue(simulation.State,1);simulation.Finish();host.Publish(simulation.State,true);
                                deadline=Time.realtimeSinceStartup+3;while(replica.State.Phase!=MatchPhase.Finished && Time.realtimeSinceStartup<deadline){host.Pump(Time.realtimeSinceStartup);guest.Pump(Time.realtimeSinceStartup);yield return null;}
                                Check(replica.State.Phase==MatchPhase.Finished && replica.State.CyanScore==2,"native Steam snapshot replicates actual final score");
                            }
                        }
                    }
                }
            }
            Finish(lobby,pair);
        }
        private IEnumerator JoinExternalRoom(string code,string[] args)
        {
            app.OpenMode(GamePlaylist.Normal);app.SelectSteamConnection();app.OpenNetwork(false);
            float deadline=Time.realtimeSinceStartup+22;while(app.Steam.Busy && Time.realtimeSinceStartup<deadline)yield return null;
            var preview=app.Steam.Rooms.SingleOrDefault(r=>r.Code==code);
            Check(preview!=null,"room list discovers the remote account's lobby before membership");
            ulong before=preview==null ? 0 : SteamMatchmaking.GetLobbyOwner(new CSteamID(preview.LobbyId)).m_SteamID;
            if(preview!=null)
            {
                Check(preview.Owner!=app.Steam.UserId,"remote lobby uses a different real Steam account");
                Check(before==0 && app.Steam.Describe(preview.LobbyId)!=null,"nonmember owner lookup is nil without discarding valid search metadata");
                int passwordAt=Array.IndexOf(args,"--soccer-steam-password-file");
                string password=passwordAt>=0 && passwordAt+1<args.Length ? File.ReadAllText(args[passwordAt+1]).TrimEnd('\r','\n') : "";
                app.UI.Root.Q<TextField>("network-address").value=code;
                app.JoinNetworkRoom(code,new RoomOptions {AutoAssignTeam=true,Password=password});password="";
                deadline=Time.realtimeSinceStartup+25;while(app.Page!=AppPage.RoomLobby && Time.realtimeSinceStartup<deadline)yield return null;
                Check(app.Room?.Connected==true && app.Page==AppPage.RoomLobby,"room code completes real cross-account Steam P2P and password admission");
                if(app.Room?.Connected==true)
                {
                    Check(app.Steam.Describe(app.Steam.LobbyId,true)?.Owner==preview.Owner,"joined owner is verified against Steam before authenticated room admission");
                    Check(app.Room.Lobby.Members.Any(m=>m.AccountId==app.PlayerAccountId) && app.Room.Lobby.Members.Any(m=>m.AccountId==SteamAccount.FromId(preview.Owner)),"both real Steam identities appear in the replicated room");
                    yield return new WaitForSecondsRealtime(2);
                    Check(app.Room?.Connected==true,"remote room remains connected after admission");
                    yield return Capture("steam-remote-room");
                }
            }
            var report=new JoinReport {passed=errors.Count==0,appId=app.Steam.AppId,code=code,found=preview!=null,ownerBeforeJoin=before,connected=app.Room?.Connected==true,status=app.NetworkStatus,checks=checks.ToArray(),errors=errors.ToArray(),scope="Real remote lobby discovery and cross-account Steam room admission; gameplay and public Internet/SDR routing are not asserted."};
            File.WriteAllText(Path.Combine(output,"steam-join.json"),JsonUtility.ToJson(report,true));
            app.GoHome();Application.Quit(errors.Count==0 ? 0 : 1);
        }
        // Only this validation adapter assigns a second logical room identity. Native sockets retain the real logged-in identity.
        private sealed class IdentityFixturePeer : IRoomPeer
        {
            private readonly IRoomPeer inner;public string AuthenticatedAccountId {get;}
            public IdentityFixturePeer(IRoomPeer peer,string identity){inner=peer;AuthenticatedAccountId=identity;}
            public bool Connected=>inner.Connected;public void Pump()=>inner.Pump();public bool Send(PacketKind kind,byte[] body)=>inner.Send(kind,body);public bool TryRead(out WirePacket packet)=>inner.TryRead(out packet);public void Dispose()=>inner.Dispose();
        }
        private void Finish(bool lobby,bool pair)
        {
            app.GoHome();var report=new Report {passed=errors.Count==0,steamInitialized=app.Steam.Ready,realLobby=lobby,nativeLoopback=pair,crossAccountP2PVerified=false,appId=app.Steam.AppId,checks=checks.ToArray(),errors=errors.ToArray(),platform=Application.platform.ToString(),scope="Real Valve lobby and Steam UDP loopback. Both native loopback sides use the signed-in account; the second room identity is an isolated protocol fixture; two-account Internet P2P and relay routing are not verified."};
            File.WriteAllText(Path.Combine(output,"steam-smoke.json"),JsonUtility.ToJson(report,true));Application.Quit(report.passed ? 0 : 1);
        }
    }
}
