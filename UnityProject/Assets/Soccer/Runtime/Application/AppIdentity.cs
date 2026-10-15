using System;
using System.IO;
using UnityEngine;
using Soccer.Core;

namespace Soccer.Runtime
{
    public sealed partial class AppController
    {
        public string PlayerName {get;private set;}="";
        public string NicknameStatus {get;private set;}="";
        public bool FirstNickname {get;private set;}
        public bool HasPlayerName=>PlayerNickname.IsValid(PlayerName);
        private string nameDirectory,nameAccount="";
        private PlayerNameStore nameStore;
        private Action nicknameContinuation;
        private void InitializeIdentity()
        {
            nameDirectory=Path.Combine(Application.persistentDataPath,"names");
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--soccer-report-dir");
            if(at>=0 && at+1<args.Length && (Array.IndexOf(args,"--soccer-smoke")>=0 || Array.IndexOf(args,"--soccer-network-smoke")>=0 || Array.IndexOf(args,"--soccer-steam-smoke")>=0 || Array.IndexOf(args,"--soccer-tactics-smoke")>=0 || Array.IndexOf(args,"--soccer-community-smoke")>=0 || Array.IndexOf(args,"--soccer-room-ui-smoke")>=0))nameDirectory=Path.Combine(args[at+1],"names");
        }
        private void ReloadPlayerName(string key)
        {
            if(nameAccount==key && nameStore!=null)return;
            nameAccount=key;nameStore=new PlayerNameStore(nameDirectory,key);NicknameStatus="";
            try{PlayerName=nameStore.Load();}catch(Exception){PlayerName="";NicknameStatus="名字暂时无法读取，请重新设置";}
            if(key=="offline" && !HasPlayerName)PlayerName="离线球员";
        }
        public void OpenNickname(bool first=false)
        {
            if(EntryMode==GameEntryMode.NotEntered)return;
            FirstNickname=first;NicknameStatus="";nicknameContinuation=first ? (Action)GoHome : ()=>OpenProfile();
            Page=AppPage.Nickname;UI.ShowPage(Page);Audio.SetContext(Page,false);UI.BeginNickname();
        }
        public void SaveNickname(string value)
        {
            try{value=PlayerNickname.Normalize(value);nameStore.Save(value);PlayerName=value;NicknameStatus="";}
            catch(ArgumentException e){NicknameStatus=e.Message;UI.RefreshNickname();return;}
            catch(Exception){NicknameStatus="名字保存失败，请重试";UI.RefreshNickname();return;}
            if(SocialOnline)CommunityCommand(new Soccer.Core.CommunityAction{Action="rename",Name=value});
            onlineProfile=null;var next=nicknameContinuation;nicknameContinuation=null;UI.RefreshLogin();next?.Invoke();
        }
        public void CancelNickname(){if(!FirstNickname){nicknameContinuation=null;OpenProfile();}}
    }
}
