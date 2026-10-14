using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Soccer.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Soccer.Runtime
{
    public sealed partial class AppController
    {
        public FriendsData Friends {get;private set;}=new FriendsData();
        public PlayerProfileData SearchPerson {get;private set;}
        public bool FriendsBusy {get;private set;}
        public string SocialStatus {get;private set;}="";
        public string ProfileStatus {get;private set;}="";
        public string ProfileOwner {get;private set;}="";
        private PlayerProfileData remoteProfile,onlineProfile;private bool profileSyncing;
        private int socialGeneration;private float nextFriendsRefresh;
        private readonly HashSet<string> uploadedMatches=new HashSet<string>();
        public bool SocialOnline=>EntryMode==GameEntryMode.Online && Account!=null && serviceAccessToken.Length>0;
        public bool OwnProfile=>ProfileOwner==PlayerAccountId;
        public PlayerProfileData VisibleProfile
        {
            get
            {
                if(!OwnProfile)return remoteProfile;
                var local=new PlayerProfileData {AccountId=PlayerAccountId,DisplayName=WalletProfile,Heroes=History?.Heroes ?? HeroRecord.Empty(),Recent=History?.Matches.ToArray() ?? new MatchRecord[0]};
                return onlineProfile!=null && onlineProfile.Heroes.Sum(h=>h.Matches)>=local.Heroes.Sum(h=>h.Matches) ? onlineProfile : local;
            }
        }
        private void ResetSocial()
        {
            socialGeneration++;Friends=new FriendsData();SearchPerson=null;onlineProfile=remoteProfile=null;FriendsBusy=profileSyncing=false;
            SocialStatus=ProfileStatus="";uploadedMatches.Clear();nextFriendsRefresh=0;
        }
        public void OpenProfile(string account=null)
        {
            if(!EnsureEntered())return;ProfileOwner=account ?? PlayerAccountId;remoteProfile=null;ProfileStatus="";
            Page=AppPage.Profile;UI.ShowPage(Page);Audio.SetContext(Page,false);
            if(SocialOnline){if(OwnProfile)SyncSocialProfile();else StartCoroutine(FetchRemoteProfile(ProfileOwner,socialGeneration));}
        }
        public void BackFromProfile(){if(OwnProfile)GoHome();else OpenFriends();}
        public void OpenFriends()
        {
            if(!EnsureEntered())return;Page=AppPage.Friends;UI.ShowPage(Page);Audio.SetContext(Page,false);RefreshFriends();
        }
        public void RefreshFriends(){if(SocialOnline && !FriendsBusy)StartCoroutine(RequestFriends("/api/friends","GET"));}
        public void SearchFriend(string account)
        {
            if(!SocialOnline || FriendsBusy)return;string id=(account ?? "").Trim();
            if(id.Length<1 || id.Length>16){SocialStatus="请输入帐号";UI.RefreshSocial();return;}
            StartCoroutine(SearchSocialPerson(id,socialGeneration));
        }
        public void FriendAction(string account,string action)
        {
            if(!SocialOnline || FriendsBusy)return;
            string path="/api/friends/"+Uri.EscapeDataString(account)+(action=="remove" ? "" : "/"+action);
            StartCoroutine(RequestFriends(path,action=="remove" ? "DELETE" : "POST"));
        }
        private UnityWebRequest SocialRequest(string path,string method,string body=null)
        {
            var request=new UnityWebRequest(LoginServerAddress+path,method){downloadHandler=new DownloadHandlerBuffer(),timeout=7};
            request.SetRequestHeader("Authorization","Bearer "+serviceAccessToken);
            if(method=="POST"){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body ?? "{}"));request.SetRequestHeader("Content-Type","application/json");}
            return request;
        }
        [Serializable] private sealed class SocialError {public string error;}
        private static string SocialFailure(UnityWebRequest request)
        {
            try{var message=JsonUtility.FromJson<SocialError>(request.downloadHandler.text)?.error;if(!string.IsNullOrWhiteSpace(message) && message.Length<100)return message;}catch(Exception){}
            return request.responseCode==401 ? "登录已失效，请重新登录" : "暂时无法连接好友服务，请稍后重试";
        }
        private IEnumerator RequestFriends(string path,string method)
        {
            int generation=socialGeneration;FriendsBusy=true;SocialStatus="";UI.RefreshSocial();
            using(var request=SocialRequest(path,method))
            {
                yield return request.SendWebRequest();if(generation!=socialGeneration)yield break;
                if(request.result==UnityWebRequest.Result.Success)
                {try{var data=JsonUtility.FromJson<FriendsData>(request.downloadHandler.text);if(data?.Friends==null || data.Incoming==null || data.Outgoing==null)throw new ArgumentException();Friends=data;SocialStatus=method=="GET" ? "" : path.EndsWith("/request") ? "好友申请已发送" : path.EndsWith("/accept") ? "已成为好友" : "已更新";}catch(Exception){SocialStatus="好友信息暂时无法读取";}}
                else SocialStatus=SocialFailure(request);
            }
            FriendsBusy=false;nextFriendsRefresh=Time.realtimeSinceStartup+10;UI.RefreshSocial();
        }
        private IEnumerator SearchSocialPerson(string id,int generation)
        {
            FriendsBusy=true;SearchPerson=null;SocialStatus="";UI.RefreshSocial();
            using(var request=SocialRequest("/api/profiles/"+Uri.EscapeDataString(id),"GET"))
            {
                yield return request.SendWebRequest();if(generation!=socialGeneration)yield break;
                if(request.result==UnityWebRequest.Result.Success){try{SearchPerson=ParseProfile(request.downloadHandler.text);}catch(Exception){SocialStatus="玩家资料暂时无法读取";}}
                else SocialStatus=SocialFailure(request);
            }
            FriendsBusy=false;UI.RefreshSocial();
        }
        private static PlayerProfileData ParseProfile(string json)
        {
            var profile=JsonUtility.FromJson<PlayerProfileData>(json);if(profile==null || profile.Recent==null || profile.Recent.Length>50)throw new ArgumentException();
            HeroRecord.Validate(profile.Heroes);foreach(var match in profile.Recent)MatchRecord.Validate(match);return profile;
        }
        private IEnumerator FetchRemoteProfile(string id,int generation)
        {
            ProfileStatus="正在读取主页…";UI.RefreshSocial();
            using(var request=SocialRequest("/api/profiles/"+Uri.EscapeDataString(id),"GET"))
            {
                yield return request.SendWebRequest();if(generation!=socialGeneration || ProfileOwner!=id)yield break;
                ProfileStatus="";if(request.result==UnityWebRequest.Result.Success){try{remoteProfile=ParseProfile(request.downloadHandler.text);}catch(Exception){ProfileStatus="玩家资料暂时无法读取";}}
                else ProfileStatus=SocialFailure(request);UI.RefreshSocial();
            }
        }
        private void SyncSocialProfile(){if(SocialOnline && !profileSyncing && History!=null)StartCoroutine(UploadProfile(socialGeneration,History));}
        private IEnumerator UploadProfile(int generation,MatchHistory history)
        {
            profileSyncing=true;ProfileStatus="正在同步战绩…";
            for(int i=0;i<50;i++)
            {
                var match=history.Matches.LastOrDefault(m=>!uploadedMatches.Contains(m.MatchId));if(match==null)break;
                using(var request=SocialRequest("/api/profile/matches","POST",JsonUtility.ToJson(match)))
                {
                    yield return request.SendWebRequest();if(generation!=socialGeneration)yield break;
                    if(request.result!=UnityWebRequest.Result.Success){ProfileStatus="战绩已保存在本机，联网同步暂未完成";profileSyncing=false;UI.RefreshSocial();yield break;}
                    uploadedMatches.Add(match.MatchId);
                }
            }
            using(var request=SocialRequest("/api/profiles/"+Uri.EscapeDataString(Account.Id),"GET"))
            {
                yield return request.SendWebRequest();if(generation!=socialGeneration)yield break;
                if(request.result==UnityWebRequest.Result.Success){try{onlineProfile=ParseProfile(request.downloadHandler.text);ProfileStatus="";}catch(Exception){ProfileStatus="战绩已保存在本机";}}
                else ProfileStatus="战绩已保存在本机，联网同步暂未完成";
            }
            profileSyncing=false;UI.RefreshSocial();
        }
        private void TickSocial()
        {if(SocialOnline && Page==AppPage.Friends && Time.realtimeSinceStartup>=nextFriendsRefresh && !FriendsBusy)RefreshFriends();}
    }
}
