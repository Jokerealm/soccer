using System;
using System.Linq;
using Soccer.Core.Networking;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed partial class AppController
    {
        public SteamPlatform Steam {get;}=new SteamPlatform();
        public bool SteamEntry=>EntryMode==GameEntryMode.Steam;
        public string PlayerAccountId=>SteamEntry ? Steam.AccountId : Account?.Id ?? "";
        private RoomOptions steamPending;private bool steamLookupByCode;private string steamRequestedCode="";private bool steamBound;private string steamPublicationSignature="";
        private static string SteamName(string value)
        {
            value=new string((value ?? "").Where(c=>!char.IsControl(c)).ToArray()).Trim();if(value.Length>24)value=value.Substring(0,char.IsHighSurrogate(value[23]) ? 23 : 24);return value.Length==0 ? "Steam球员" : value;
        }
        private void BindSteam()
        {
            if(steamBound)return;steamBound=true;
            Steam.Changed+=()=>
            {
                if(Room?.IsHost==true && Room.Lobby.Route==NetworkRoute.Steam && !Steam.Busy && Steam.LobbyId==0)
                {Room.Dispose();Room=null;NetworkStatus=Steam.Status;Page=AppPage.NetworkMenu;UI.ShowPage(Page);}
                if(steamLookupByCode && !Steam.Busy)
                {
                    steamLookupByCode=false;var matches=Steam.Rooms.Where(r=>r.Code==steamRequestedCode).ToArray();
                    if(matches.Length==1){Steam.JoinRoom(matches[0].LobbyId);return;}
                    steamPending=null;NetworkStatus=matches.Length==0 ? "Steam房间不存在或不可加入" : "房间号重复，请从列表选择房间";
                }
                else if(Route==ConnectionRoute.Steam && !string.IsNullOrEmpty(Steam.Status))NetworkStatus=Steam.Status;
                UI.RefreshLogin();UI.RefreshModeDetails();UI.RefreshNetwork();
            };
            Steam.LobbyCreated+=info=>
            {if(Room==null || info==null)return;LocalPlayerId=Room.LocalSlot;NetworkStatus="";Page=AppPage.RoomLobby;UI.ShowPage(Page);};
            Steam.LobbyJoined+=info=>
            {
                if(steamPending==null){Steam.LeaveRoom();return;}var options=steamPending;steamPending=null;
                if(info.Password && string.IsNullOrEmpty(options.Password))
                {Steam.LeaveRoom();UI.Root.Q<UnityEngine.UIElements.TextField>("network-address").SetValueWithoutNotify(info.Code);NetworkStatus="房间需要密码，请填写后加入";UI.RefreshNetwork();return;}
                Steam.Connect(info.Owner,peer=>
                {
                    try{Room=NetworkRoom.JoinSteam(peer,options,info.RoomId);AttachRoom();NetworkStatus="正在加入Steam房间…";UI.RefreshNetwork();}
                    catch(Exception){peer.Dispose();Steam.LeaveRoom();NetworkStatus="Steam房间连接失败";UI.RefreshNetwork();}
                });
            };
            Steam.InviteReceived+=id=>
            {
                if(Page==AppPage.Match){Feedback="收到Steam房间邀请，请在本局结束后加入";feedbackRemaining=5;return;}
                if(!EnterSteam(false))return;SelectedPlaylist=GamePlaylist.Normal;Route=ConnectionRoute.Steam;OpenNetwork(false,false);
                steamPending=SteamOptions(new RoomOptions());Steam.JoinRoom(id);
            };
        }
        public bool EnterSteam(bool goHome=true)
        {
            if(!StartupReady)return false;BindSteam();
            if(!Steam.Initialize()){LoginStatus=Steam.Status;NetworkStatus=Steam.Status;UI.RefreshLogin();UI.RefreshModeDetails();return false;}
            CloseNetworking();Account?.Dispose();Account=null;serviceAccessToken="";ResetSocial();EntryMode=GameEntryMode.Steam;Route=ConnectionRoute.Steam;LoginStatus="";ReloadWallet();
            if(goHome)GoHome();return true;
        }
        public void SelectSteamConnection()
        {if(!SteamEntry && !EnterSteam(false))return;Route=ConnectionRoute.Steam;UI.RefreshModeDetails();}
        private RoomOptions SteamOptions(RoomOptions options)
        {options.Route=NetworkRoute.Steam;options.Account=null;options.PlatformAccountId=Steam.AccountId;options.PlayerName=SteamName(Steam.Name);return options;}
        private void CreateSteamRoom(RoomOptions options)
        {
            try{Room=NetworkRoom.HostSteam(SteamOptions(options));AttachRoom();Steam.CreateRoom(Room);NetworkStatus=Steam.Status;UI.RefreshNetwork();}
            catch(Exception e){Steam.LeaveRoom();Room?.Dispose();Room=null;NetworkStatus=e is ArgumentException ? e.Message : "Steam房间创建失败";UI.RefreshNetwork();}
        }
        public void JoinSteamRoom(ulong id,RoomOptions options)
        {
            if(!SteamEntry || Steam.Busy || Room!=null)return;steamPending=SteamOptions(options);Steam.JoinRoom(id);UI.RefreshNetwork();
        }
        private void LookupSteamRoom(string code,RoomOptions options)
        {
            if(!RoomCode.TryNormalize(code,out var normalized)){NetworkStatus="请输入6位Steam房间号";UI.RefreshNetwork();return;}
            steamPending=SteamOptions(options);steamRequestedCode=normalized;steamLookupByCode=true;Steam.FindRooms(normalized);UI.RefreshNetwork();
        }
        private void PumpSteam()
        {
            Steam.Pump();
            if(Room?.IsHost==true && Room.Lobby.Route==NetworkRoute.Steam && Steam.LobbyId!=0)
            {
                string signature=Room.Lobby.Code+Room.OccupiedSlots+Room.Running+Room.Lobby.Drafting+Room.CountingDown;
                if(signature!=steamPublicationSignature){steamPublicationSignature=signature;Steam.Publish(Room);}
            }
        }
        public void InviteSteamFriends()=>Steam.InviteFriends();
        private void CloseSteamRoom(){steamPending=null;steamLookupByCode=false;steamPublicationSignature="";Steam.LeaveRoom();}
    }
}
