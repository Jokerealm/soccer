using System;
using System.Collections;
using System.Linq;
using Soccer.Core;
using Soccer.Core.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace Soccer.Runtime
{
    public sealed partial class AppController
    {
        public CommunityState Community {get;private set;}=new CommunityState();
        public string CommunityStatus {get;private set;}="";
        public bool CommunityBusy {get;private set;}
        public ServiceMatchSession ServiceMatch {get;private set;}
        public bool IsServiceMatch=>ServiceMatch!=null && Simulation!=null && (Page==AppPage.Match || Page==AppPage.Results);
        public string SocialAccountId=>SteamEntry ? steamCommunityAccount : Account?.Id ?? "";
        private string steamCommunityAccount="";
        private bool communityPolling;
        private float nextCommunityPoll,serviceSnapshotAt;
        private string communityMatchClosed="";

        public void OpenParty()
        {
            if(!EnsureEntered())return;
            Page=AppPage.Party;UI.ShowPage(Page);Audio.SetContext(Page,false);
            if(SteamEntry && !SocialOnline)ConnectSteamCommunity();
            if(SocialOnline)CommunityCommand(new CommunityAction{Action="createParty"});
            else if(!SteamEntry)CommunityStatus="请先登录游戏帐号，添加游戏好友并组队。";
            UI.RefreshCommunity();
        }
        public void CommunityCommand(CommunityAction action)
        {
            if(!SocialOnline || CommunityBusy)return;
            StartCoroutine(CommunityRequest(action,socialGeneration));
        }
        private IEnumerator CommunityRequest(CommunityAction action,int generation)
        {
            CommunityBusy=true;CommunityStatus="";UI.RefreshCommunity();
            using(var request=SocialRequest("/api/community/action","POST",JsonUtility.ToJson(action)))
            {
                yield return request.SendWebRequest();if(generation!=socialGeneration)yield break;
                if(request.result==UnityWebRequest.Result.Success)ApplyCommunity(request.downloadHandler.text);
                else CommunityStatus=SocialFailure(request);
            }
            CommunityBusy=false;nextCommunityPoll=Time.realtimeSinceStartup+.5f;UI.RefreshCommunity();UI.RefreshSocial();
        }
        private void TickCommunity()
        {
            ServiceMatch?.Pump();
            if(SocialOnline && !communityPolling && Time.realtimeSinceStartup>=nextCommunityPoll)StartCoroutine(PollCommunity(socialGeneration));
        }
        private IEnumerator PollCommunity(int generation)
        {
            communityPolling=true;
            using(var request=SocialRequest("/api/community/state","GET"))
            {
                yield return request.SendWebRequest();if(generation!=socialGeneration)yield break;
                if(request.result==UnityWebRequest.Result.Success){ApplyCommunity(request.downloadHandler.text);if(CommunityStatus.StartsWith("暂时无法"))CommunityStatus="";}
                else if(Page==AppPage.Party)CommunityStatus=SocialFailure(request);
            }
            communityPolling=false;nextCommunityPoll=Time.realtimeSinceStartup+1.5f;UI.RefreshCommunity();if(Page==AppPage.Friends)UI.RefreshSocial();UI.Chat?.RefreshInbox();
        }
        private void ApplyCommunity(string json)
        {
            try
            {
                var state=JsonUtility.FromJson<CommunityState>(json)?.Normalize();
                if(state==null || state.AccountId!=SocialAccountId || state.Invites==null || state.OnlineFriends==null || state.Inbox==null)throw new ArgumentException();
                if(state.Revision<Community.Revision)return;Community=state;
                if(state.Match!=null && state.Match.Phase!="已结束" && state.Match.Id!=communityMatchClosed && ServiceMatch==null && (Page==AppPage.Party || Page==AppPage.Friends || Page==AppPage.Home))ConnectServiceMatch(state.Match);
            }
            catch(ArgumentException){CommunityStatus="队伍信息暂时无法读取";}
        }
        public void ReconnectServiceMatch()
        {communityMatchClosed="";if(Community.Match!=null)ConnectServiceMatch(Community.Match);}
        private void ConnectServiceMatch(MatchAssignment assignment)
        {
            CloseNetworking();ServiceMatch?.Dispose();Simulation=null;View.Clear();Paused=false;LocalPlayerId=assignment.Slot;
            ServiceMatch=new ServiceMatchSession(LoginServerAddress,serviceAccessToken,assignment);
            ServiceMatch.DraftChanged+=()=>
            {
                if(ServiceMatch?.Draft==null)return;
                if(Page!=AppPage.CharacterSelect){Selected=ServiceMatch.Draft.Picks[LocalPlayerId].Character;DraftPosition=ServiceMatch.Draft.Picks[LocalPlayerId].Position;}
                Page=AppPage.CharacterSelect;SelectedMode=MatchMode.BotMatch;UI.ShowPage(Page);UI.RefreshSelection();Audio.SetContext(Page,false);
            };
            ServiceMatch.Started+=BeginServiceMatch;
            ServiceMatch.Snapshot+=bytes=>
            {
                if(Simulation==null)return;long previous=Simulation.State.Tick;NetworkCodec.ApplySnapshot(Simulation.State,bytes);serviceSnapshotAt=Time.realtimeSinceStartup;
                if(Simulation.State.Tick>previous)foreach(var e in Simulation.State.Events)HandleEvent(e);SettleMatchReward();
            };
            ServiceMatch.Signal+=signal=>UI.Tactics.Receive(signal);ServiceMatch.Chat+=message=>UI.Chat.Receive(message,"match:"+assignment.Id);
            ServiceMatch.Failed+=reason=>
            {
                if(Simulation?.State.Phase==MatchPhase.Finished)return;
                CommunityStatus=reason;communityMatchClosed=assignment.Id;ServiceMatch?.Dispose();ServiceMatch=null;Simulation=null;View.Clear();Page=AppPage.Party;UI.ShowPage(Page);
            };
            CommunityStatus="已找到对局，正在连接服务器…";UI.RefreshCommunity();
        }
        private void BeginServiceMatch(NetworkStart start)
        {
            Selected=start.Lineup[LocalPlayerId];SelectedMode=MatchMode.BotMatch;
            Simulation=new MatchSimulation(start.Rules,Array.ConvertAll(Characters,c=>c.CreateStats()),Selected,MatchMode.BotMatch,start.Lineup);
            if(start.Positions!=null)Simulation.ApplyOpeningPositions(start.Positions);
            for(int i=0;i<6;i++)Simulation.ConfigureBot(i,(ServiceMatch.Assignment.BotMask&(1<<i))!=0);
            localInput=new KeyboardMouseInput(WorldCamera,Keys);BeginRewardSession("server:"+ServiceMatch.Assignment.Id);
            View.LocalPlayerId=Audio.LocalPlayerId=LocalPlayerId;View.Bind(Simulation.State,Characters);
            Paused=false;FinalGoalRemaining=0;accumulator=0;serviceSnapshotAt=Time.realtimeSinceStartup;Page=AppPage.Match;UI.ShowPage(Page);
            WorldCamera.GetComponent<ArenaCamera>().ResetFraming();Audio.ResetMatch();Audio.SetContext(Page,false);
        }
        private void UpdateServiceMatch()
        {
            var state=Simulation.State;
            if(state.Phase==MatchPhase.Finished)
            {SettleMatchReward();FinalGoalRemaining=Mathf.Max(0,FinalGoalRemaining-Time.unscaledDeltaTime);View.Render(state,Simulation.Rules,1,ShowDebug,!Paused);UI.RefreshMatch();if(FinalGoalRemaining<=0)ShowResults();return;}
            bool active=!Paused && Application.isFocused && (state.Phase==MatchPhase.Playing || state.IsOpeningWarmup || state.Phase==MatchPhase.SetPiece);
            if(active){if(UI.Tactics.BlocksGameplay || UI.Chat.BlocksGameplay)localInput.SuppressActions();else localInput.Sample(state,Simulation.Rules,UI.IsPointerOverControl(),state.IsOpeningWarmup,LocalPlayerId);}else localInput.Reset();
            accumulator+=Mathf.Min(Time.unscaledDeltaTime,.1f);
            while(accumulator>=MatchSimulation.TickSeconds){ServiceMatch.Input(localInput.ReadCommand(state,LocalPlayerId));accumulator-=MatchSimulation.TickSeconds;}
            Audio.Observe(state,Simulation.Rules);View.Render(state,Simulation.Rules,Mathf.Clamp01((Time.realtimeSinceStartup-serviceSnapshotAt)/.05f),ShowDebug,!Paused,localInput.PreviewAim,localInput.SelectingSkillTarget);UI.RefreshMatch();
        }
        public void LeaveServiceMatch()
        {
            if(ServiceMatch!=null)communityMatchClosed=ServiceMatch.Assignment.Id;
            ServiceMatch?.Dispose();ServiceMatch=null;Simulation=null;View.Clear();Paused=false;LocalPlayerId=0;
            CommunityCommand(new CommunityAction{Action="leaveMatch"});Page=AppPage.Party;UI.ShowPage(Page);Audio.SetContext(Page,false);
        }
        private void ResetCommunity()
        {
            ServiceMatch?.Dispose();ServiceMatch=null;Community=new CommunityState();CommunityStatus="";CommunityBusy=communityPolling=false;
            nextCommunityPoll=0;communityMatchClosed="";UI?.Chat?.Reset();
        }
        private IEnumerator RegisterGameAccount(string account,string name,string password)
        {
            LoginBusy=true;LoginStatus="正在创建帐号…";UI.RefreshLogin();
            using(var request=new UnityWebRequest(LoginServerAddress+"/api/register","POST"){uploadHandler=new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Registration{Account=account,Name=name,Password=password}))),downloadHandler=new DownloadHandlerBuffer(),timeout=8})
            {
                request.SetRequestHeader("Content-Type","application/json");yield return request.SendWebRequest();LoginBusy=false;
                if(request.result==UnityWebRequest.Result.Success)
                {
                    var response=JsonUtility.FromJson<LoginResponse>(request.downloadHandler.text);
                    if(response!=null && response.accountId==account && ValidServiceToken(response.accessToken))
                    {Account?.Dispose();Account=DebugAccounts.FromService(account);serviceAccessToken=response.accessToken;EntryMode=GameEntryMode.Online;Route=ConnectionRoute.Internet;ResetSocial();ReloadWallet();GoHome();if(!HasPlayerName)OpenNickname(true);RefreshFriends();}
                    else LoginStatus="帐号服务返回无效信息";
                }
                else LoginStatus=SocialFailure(request);
                UI.RefreshLogin();
            }
        }
        [Serializable] private sealed class Registration {public string Account,Name,Password;}
        public void RegisterAccount(string account,string name,string password)
        {
            if(LoginBusy || Page!=AppPage.Login)return;
            if(string.IsNullOrEmpty(LoginServerAddress)){LoginStatus="游戏帐号服务暂未开放，请先使用 Steam 或离线进入。";UI.RefreshLogin();return;}
            StartCoroutine(RegisterGameAccount((account??"").Trim().ToLowerInvariant(),name,password));
        }
        public void ConnectSteamCommunity()
        {
            if(!SteamEntry || SocialOnline || FriendsBusy)return;
            if(string.IsNullOrEmpty(LoginServerAddress)){SocialStatus=CommunityStatus="游戏好友服务暂未开放；Steam 好友仍可邀请开房。";UI.RefreshSocial();UI.RefreshCommunity();return;}
            FriendsBusy=true;Steam.RequestCommunityTicket(ticket=>{if(ticket==null){FriendsBusy=false;SocialStatus=CommunityStatus="Steam 身份票据暂不可用";UI.RefreshSocial();return;}StartCoroutine(AuthenticateSteamCommunity(ticket,socialGeneration));});
        }
        public void ConnectGameSocial(string account,string name,string password,bool register)
        {
            if(!SteamEntry || CommunityBusy)return;
            if(string.IsNullOrEmpty(LoginServerAddress)){CommunityStatus="游戏好友服务暂未配置";UI.RefreshCommunity();return;}
            StartCoroutine(ConnectGameSocialRequest((account??"").Trim().ToLowerInvariant(),name,password,register,socialGeneration));
        }
        private IEnumerator ConnectGameSocialRequest(string account,string name,string password,bool register,int generation)
        {
            CommunityBusy=true;CommunityStatus="正在连接游戏帐号…";UI.RefreshCommunity();
            string path=register ? "/api/register" : "/api/login";
            string data=register ? JsonUtility.ToJson(new Registration{Account=account,Name=name,Password=password}) : JsonUtility.ToJson(new LoginRequest{account=account,password=password});
            using(var request=SocialRequest(path,"POST",data))
            {
                yield return request.SendWebRequest();if(generation!=socialGeneration)yield break;CommunityBusy=false;
                if(request.result==UnityWebRequest.Result.Success)
                {
                    var response=JsonUtility.FromJson<LoginResponse>(request.downloadHandler.text);
                    if(response!=null && response.accountId==account && ValidServiceToken(response.accessToken))
                    {ResetSocial();steamCommunityAccount=account;serviceAccessToken=response.accessToken;CommunityStatus=SocialStatus="";RefreshFriends();UI.HideCommunityLogin();}
                    else CommunityStatus="游戏帐号返回无效信息";
                }
                else CommunityStatus=SocialFailure(request);
                UI.RefreshCommunity();UI.RefreshSocial();
            }
        }
        [Serializable] private sealed class SteamLogin {public string Ticket,Name;}
        private IEnumerator AuthenticateSteamCommunity(string ticket,int generation)
        {
            using(var request=SocialRequest("/api/auth/steam","POST",JsonUtility.ToJson(new SteamLogin{Ticket=ticket,Name=PlayerName})))
            {
                yield return request.SendWebRequest();if(generation!=socialGeneration)yield break;FriendsBusy=false;
                if(request.result==UnityWebRequest.Result.Success)
                {
                    var response=JsonUtility.FromJson<LoginResponse>(request.downloadHandler.text);
                    if(response!=null && response.accountId=="S"+Steam.UserId && ValidServiceToken(response.accessToken))
                    {steamCommunityAccount=response.accountId;serviceAccessToken=response.accessToken;SocialStatus=CommunityStatus="";RefreshFriends();nextCommunityPoll=0;}
                    else SocialStatus=CommunityStatus="Steam 身份验证返回无效信息";
                }
                else SocialStatus=CommunityStatus=SocialFailure(request);
                UI.RefreshSocial();UI.RefreshCommunity();
            }
            Steam.CancelCommunityTicket();
        }
    }
}
