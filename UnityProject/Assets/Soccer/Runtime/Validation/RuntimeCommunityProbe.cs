using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Soccer.Core;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed class RuntimeCommunityProbe : MonoBehaviour
    {
        [Serializable] private sealed class Auth {public string accessToken,accountId;}
        [Serializable] private sealed class Registration {public string Account,Name,Password;}
        [Serializable] private sealed class Report {public bool passed;public string[] checks,errors;}
        private AppController app;private string output,other,otherToken,body;private long responseCode;
        private readonly List<string> checks=new List<string>(),errors=new List<string>();
        private void Check(bool value,string name){if(value)checks.Add(name);else errors.Add(name);Debug.Log((value ? "PASS " : "FAIL ")+name);}
        private void Error(string message,string trace,LogType type){if(type==LogType.Exception || type==LogType.Error)errors.Add(message);}
        private IEnumerator Capture(string name){yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);}
        private IEnumerator Click(string name)
        {var button=app.UI.Root.Q<Button>(name);Check(button!=null && button.enabledInHierarchy,"button available: "+name);if(button!=null){button.Focus();yield return null;using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}yield return null;}
        private IEnumerator Api(string path,string token,string json=null)
        {
            using(var request=new UnityWebRequest(app.LoginServerAddress+path,json==null ? "GET" : "POST"){downloadHandler=new DownloadHandlerBuffer(),timeout=8})
            {if(token.Length>0)request.SetRequestHeader("Authorization","Bearer "+token);if(json!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));request.SetRequestHeader("Content-Type","application/json");}
                yield return request.SendWebRequest();responseCode=request.responseCode;body=request.downloadHandler.text;}
        }
        private IEnumerator Await(Func<bool> condition,float seconds=10)
        {float deadline=Time.realtimeSinceStartup+seconds;while(!condition() && Time.realtimeSinceStartup<deadline)yield return null;Check(condition(),"asynchronous operation completes");}
        private IEnumerator Start()
        {
            app=GetComponent<AppController>();var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--soccer-report-dir");output=index>=0 ? args[index+1] : Path.Combine(Application.persistentDataPath,"CommunityProbe");Directory.CreateDirectory(output);Application.logMessageReceived+=Error;
            yield return Await(()=>app.StartupReady);var root=app.UI.Root;string suffix=Guid.NewGuid().ToString("N").Substring(0,6),account="u"+suffix;other="v"+suffix;
            yield return Click("register-toggle");root.Q<TextField>("login-name").value=account;root.Q<TextField>("register-name").value="联机队长";root.Q<TextField>("login-password").value="Community-test-029";
            yield return Click("login-submit");yield return Await(()=>!app.LoginBusy);
            if(app.Page==AppPage.Nickname){root.Q<TextField>("nickname-value").value="联机队长";yield return Click("nickname-save");}
            Check(app.SocialOnline && app.Account?.Id==account,"real registration creates and authenticates an independent game account");
            if(!app.SocialOnline){errors.Add("registration: "+app.LoginStatus+" / "+app.Page);yield return Capture("registration-failure");File.WriteAllText(Path.Combine(output,"community-smoke.json"),JsonUtility.ToJson(new Report{passed=false,checks=checks.ToArray(),errors=errors.ToArray()},true));Application.Quit(1);yield break;}
            yield return Api("/api/register","",JsonUtility.ToJson(new Registration{Account=other,Name="联机队友",Password="Community-test-029"}));
            otherToken=JsonUtility.FromJson<Auth>(body).accessToken;Check(responseCode==200 && otherToken.Length==64,"second independent game account is registered through real service");
            app.OpenFriends();yield return Await(()=>!app.FriendsBusy);root.Q<TextField>("friend-account").value=other;yield return Click("friend-search");yield return Await(()=>!app.FriendsBusy);
            Check(app.SearchPerson?.AccountId==other,"friend search finds registered game ID");yield return Click("friend-add");yield return Await(()=>!app.FriendsBusy);
            yield return Api("/api/friends/"+account+"/accept",otherToken,"{}");Check(responseCode==200,"second player accepts real friend request");app.RefreshFriends();yield return Await(()=>!app.FriendsBusy);
            Check(app.Friends.Friends.Any(f=>f.AccountId==other),"game friends appear alongside a separate Steam tab");yield return Capture("01-game-friends");
            yield return Click("friends-steam-tab");Check(root.Q("friends-steam").resolvedStyle.display!=DisplayStyle.None && root.Q("friends-online").resolvedStyle.display==DisplayStyle.None,"Steam tab is distinct from game friend list");yield return Capture("02-steam-friends-tab");yield return Click("friends-game-tab");
            yield return Click("friend-chat-"+other);root.Q<TextField>("chat-input").value="<b>准备好了，一起踢球</b>";app.UI.Chat.Send();yield return new WaitForSecondsRealtime(.7f);
            yield return Api("/api/chat?channel=private&target="+account,otherToken);var history=JsonUtility.FromJson<ChatHistory>(body);
            Check(history.Messages.Any(m=>m.SenderId==account && m.Text.StartsWith("<b>")),"native private-chat UI sends to authenticated friend");
            yield return Api("/api/chat",otherToken,JsonUtility.ToJson(new ChatRequest{Channel="private",Target=account,Text="收到，给我传球"}));yield return new WaitForSecondsRealtime(1.4f);
            var chatLabels=root.Q<ScrollView>("chat-messages").Query<Label>().ToList();Check(chatLabels.Any(l=>l.text.Contains("给我传球")) && chatLabels.All(l=>!l.enableRichText),"private replies render as plain text without interpreting player markup");yield return Capture("03-private-chat");app.UI.Chat.Close();
            yield return Api("/api/community/action",otherToken,JsonUtility.ToJson(new CommunityAction{Action="invite",Target=account}));app.GoHome();yield return Await(()=>app.Community.Invites.Length>0);
            Check(root.Q<Button>("home-party").text.Contains("邀请"),"incoming party invitations are visible from the home page");
            app.CommunityCommand(new CommunityAction{Action="reject",Id=app.Community.Invites[0].Id});yield return Await(()=>!app.CommunityBusy);
            app.OpenParty();yield return Await(()=>app.Community.Party!=null && !app.CommunityBusy);
            app.OpenFriends();yield return new WaitForSecondsRealtime(.2f);yield return Click("friend-invite-"+other);yield return Await(()=>!app.CommunityBusy);
            yield return Api("/api/community/state",otherToken);var state=JsonUtility.FromJson<CommunityState>(body).Normalize();Check(state.Invites.Length>0,"game friend receives party invitation");
            yield return Api("/api/community/action",otherToken,JsonUtility.ToJson(new CommunityAction{Action="accept",Id=state.Invites[0].Id}));app.OpenParty();yield return Await(()=>app.Community.Party?.Members.Length==2);
            yield return Capture("04-two-player-party");
            yield return Click("party-chat");root.Q<TextField>("chat-input").value="我们一起开房";app.UI.Chat.Send();yield return new WaitForSecondsRealtime(.8f);
            yield return Api("/api/chat?channel=party",otherToken);Check(JsonUtility.FromJson<ChatHistory>(body).Messages.Any(m=>m.Text=="我们一起开房"),"party chat reaches the invited teammate");app.UI.Chat.Close();
            app.OpenSelection(MatchMode.Training);app.StartMatch();yield return new WaitForSecondsRealtime(3.4f);app.ResetTraining(true);
            var commands=new PlayerCommand[app.Simulation.State.Players.Count];commands[0]=new PlayerCommand{KickPressed=true,KickHeld=true,Aim=new Vec2(1,0)};app.Simulation.Step(commands);
            app.UI.Chat.OpenPrivate(other,"联机队友");yield return new WaitForSecondsRealtime(.1f);long before=app.Simulation.State.Tick;yield return new WaitForSecondsRealtime(1.5f);
            Check(app.UI.Chat.BlocksGameplay && app.Simulation.State.Tick>before+20,"typing chat blocks gameplay actions while match simulation continues");
            Check(!app.Simulation.State.Players[0].Charging && app.Simulation.State.Players[0].Kicks==0,"opening chat cancels prepared shot instead of auto-releasing it");yield return Capture("05-match-chat-input");app.UI.Chat.Close();app.GoHome();app.OpenParty();
            yield return Await(()=>!app.CommunityBusy);yield return Click("party-create-room");yield return Await(()=>app.Community.Room!=null && !app.CommunityBusy);
            Check(app.Community.Room.Slots.Count(m=>m!=null)==2,"leader opens one room for the whole party");
            root.Q<DropdownField>("party-room-difficulty").value=BotDifficulties.Names[3];yield return Await(()=>!app.CommunityBusy);yield return Click("party-room-bots");yield return Await(()=>!app.CommunityBusy);
            Check(app.Community.Room.Bots.Length==4 && app.Community.Room.Difficulty==BotDifficulty.Cheater,"private server room configures AI difficulty and four robot slots");yield return Capture("06-private-room");
            yield return Click("party-room-chat");root.Q<TextField>("chat-input").value="房间聊天正常";app.UI.Chat.Send();yield return new WaitForSecondsRealtime(.8f);
            yield return Api("/api/chat?channel=room",otherToken);Check(JsonUtility.FromJson<ChatHistory>(body).Messages.Any(m=>m.Text=="房间聊天正常"),"native private-room chat is delivered by membership");app.UI.Chat.Close();
            yield return Api("/api/community/action",otherToken,JsonUtility.ToJson(new CommunityAction{Action="roomReady",Ready=true}));yield return Click("party-room-ready");
            ServiceMatchSession teammate=null;float deadline=Time.realtimeSinceStartup+35;
            while(!app.IsServiceMatch && Time.realtimeSinceStartup<deadline)
            {
                if(teammate==null)
                {
                    yield return Api("/api/community/state",otherToken);state=JsonUtility.FromJson<CommunityState>(body).Normalize();
                    if(state.Match!=null){teammate=new ServiceMatchSession(app.LoginServerAddress,otherToken,state.Match);teammate.DraftChanged+=()=>{if(teammate.Draft?.CurrentSlot==teammate.Assignment.Slot){var pick=teammate.Draft.Picks[teammate.Assignment.Slot];teammate.Lock(pick.Character,pick.Position);}};}
                }
                teammate?.Pump();if(app.MyDraftTurn)app.ConfirmSelection();yield return null;
            }
            Check(app.IsServiceMatch && app.ServiceMatch.Assignment.Playlist=="custom","party room flows through real server draft into a native rendered match");
            if(app.IsServiceMatch)
            {
                Check(app.Simulation.State.Players[app.LocalPlayerId].Stats.MaxHealth==100 && app.Simulation.State.Players[2].Stats.MaxHealth==125,"native server match applies cheater bonus only to robots");
                var received=new List<ChatMessage>();teammate.Chat+=m=>received.Add(m);app.UI.Chat.Open("team");root.Q<TextField>("chat-input").value="队内沟通";app.UI.Chat.Send();
                deadline=Time.realtimeSinceStartup+5;while(!received.Any(m=>m.Text=="队内沟通") && Time.realtimeSinceStartup<deadline){teammate.Pump();yield return null;}
                Check(received.Any(m=>m.Text=="队内沟通"),"native match team chat reaches a second real WebSocket peer");
                app.SetPaused(true);before=app.Simulation.State.Tick;deadline=Time.realtimeSinceStartup+1;while(Time.realtimeSinceStartup<deadline){teammate.Pump();yield return null;}
                Check(app.Simulation.State.Tick>before+20 && root.Q<Label>("pause-title").text=="联机菜单","service match keeps authoritative simulation running in local menu");app.SetPaused(false);yield return Capture("07-authoritative-match-chat");app.UI.Chat.Close();app.LeaveServiceMatch();
            }
            teammate?.Dispose();
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {Screen.SetResolution(size.x,size.y,false);yield return new WaitForSecondsRealtime(.4f);var bounds=root.Q("party-page").worldBound;Check(root.worldBound.Contains(bounds.min) && root.worldBound.Contains(bounds.max),size+" party page fits viewport");app.UI.Chat.OpenPrivate(other,"联机队友");yield return null;bounds=root.Q("chat-panel").worldBound;Check(root.worldBound.Contains(bounds.min) && root.worldBound.Contains(bounds.max),size+" chat panel fits viewport");yield return Capture("party-chat-"+size.x+"x"+size.y);app.UI.Chat.Close();}
            Application.logMessageReceived-=Error;File.WriteAllText(Path.Combine(output,"community-smoke.json"),JsonUtility.ToJson(new Report{passed=errors.Count==0,checks=checks.ToArray(),errors=errors.ToArray()},true));Application.Quit(errors.Count==0 ? 0 : 1);
        }
    }
}
