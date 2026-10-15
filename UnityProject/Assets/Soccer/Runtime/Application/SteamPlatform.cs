using System;
using System.Collections.Generic;
using System.Linq;
using Soccer.Core;
using Soccer.Core.Networking;
using Steamworks;
using UnityEngine;

namespace Soccer.Runtime
{
    public sealed class SteamFriendInfo {public ulong Id;public string Name;public bool Online;}
    public sealed class SteamRoomInfo
    {public ulong LobbyId,Owner;public string Name,Code,RoomId,HostName;public int Occupied,Players;public bool Password,Busy;}
    /// <summary>Steam API callbacks, lobby metadata and P2P connections stay on Unity's main thread.</summary>
    public sealed class SteamPlatform : IDisposable
    {
        public const string GameKey="skill-soccer-jokerealm";
        public bool Initialized {get;private set;}
        public bool Ready=>Initialized && SteamUser.BLoggedOn();
        public uint AppId {get;private set;}=480;
        public ulong UserId {get;private set;}
        public string AccountId=>SteamAccount.FromId(UserId);
        public string Name {get;private set;}="";
        public string Status {get;private set;}="请启动并登录 Steam";
        public ulong LobbyId {get;private set;}
        public bool Busy {get;private set;}
        public SteamRoomInfo[] Rooms {get;private set;}=new SteamRoomInfo[0];
        public event Action Changed;
        public event Action<SteamRoomInfo> LobbyCreated,LobbyJoined;
        public event Action<ulong> InviteReceived;
        private CallResult<LobbyCreated_t> create;private CallResult<LobbyEnter_t> join;private CallResult<LobbyMatchList_t> list;
        private Callback<GameLobbyJoinRequested_t> invitation;private Callback<SteamNetConnectionStatusChangedCallback_t> connections;
        private HSteamListenSocket listener;private NetworkRoom hostRoom;private ulong expectedOwner;
        private Action<SteamSocketPeer> onConnected;private HSteamNetConnection pendingConnection;
        private readonly Dictionary<uint,SteamSocketPeer> peers=new Dictionary<uint,SteamSocketPeer>();
        private Callback<GetTicketForWebApiResponse_t> communityTicketCallback;
        private HAuthTicket communityTicket=HAuthTicket.Invalid;private Action<string> communityTicketReady;private float communityTicketDeadline;
        public void RequestCommunityTicket(Action<string> ready)
        {
            CancelCommunityTicket();if(!Ready){ready(null);return;}
            if(communityTicketCallback==null)communityTicketCallback=Callback<GetTicketForWebApiResponse_t>.Create(result=>{
                if(result.m_hAuthTicket!=communityTicket || communityTicketReady==null)return;
                var callback=communityTicketReady;communityTicketReady=null;
                callback(result.m_eResult==EResult.k_EResultOK && result.m_cubTicket>0 && result.m_cubTicket<=result.m_rgubTicket.Length
                    ? BitConverter.ToString(result.m_rgubTicket,0,result.m_cubTicket).Replace("-","") : null);
            });
            communityTicketReady=ready;communityTicketDeadline=Time.realtimeSinceStartup+10;communityTicket=SteamUser.GetAuthTicketForWebApi("skillsoccer-community");
        }
        public void CancelCommunityTicket(){if(communityTicket!=HAuthTicket.Invalid && Initialized)SteamUser.CancelAuthTicket(communityTicket);communityTicket=HAuthTicket.Invalid;communityTicketReady=null;}
        public bool InviteFriend(ulong id)=>Ready && LobbyId!=0 && SteamMatchmaking.InviteUserToLobby(new CSteamID(LobbyId),new CSteamID(id));
        private float deadline;private int operation;
        public bool Initialize()
        {
            if(Initialized){if(Ready){UserId=SteamUser.GetSteamID().m_SteamID;Name=SteamFriends.GetPersonaName();}Status=Ready ? "Steam 已连接" : "请在Steam客户端完成登录";return Ready;}
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--soccer-steam-app-id");
            if(at>=0 && at+1<args.Length && (!uint.TryParse(args[at+1],out var id) || id==0)){Status="Steam AppID 配置无效";return false;}
            if(at>=0 && at+1<args.Length)AppId=uint.Parse(args[at+1]);
            Environment.SetEnvironmentVariable("SteamAppId",AppId.ToString());Environment.SetEnvironmentVariable("SteamGameId",AppId.ToString());
            try
            {
                if(!Packsize.Test()) {Status="Steam接口平台配置不匹配";return false;}
                var result=SteamAPI.InitEx(out _);if(result!=ESteamAPIInitResult.k_ESteamAPIInitResult_OK){Status="请先启动并登录 Steam，然后重试";return false;}
                Initialized=true;AppId=SteamUtils.GetAppID().m_AppId;
                if(Ready){UserId=SteamUser.GetSteamID().m_SteamID;Name=SteamFriends.GetPersonaName();}
                create=CallResult<LobbyCreated_t>.Create(Created);join=CallResult<LobbyEnter_t>.Create(Joined);list=CallResult<LobbyMatchList_t>.Create(Listed);
                invitation=Callback<GameLobbyJoinRequested_t>.Create(v=>InviteReceived?.Invoke(v.m_steamIDLobby.m_SteamID));
                connections=Callback<SteamNetConnectionStatusChangedCallback_t>.Create(ConnectionChanged);
                SteamNetworkingUtils.InitRelayNetworkAccess();Status=Ready ? "Steam 已连接" : "请在Steam客户端完成登录";return Ready;
            }
            catch(Exception){Status="Steam接口暂不可用，请检查Steam客户端是否已启动";return false;}
        }
        private static string Data(CSteamID id,string key)=>SteamMatchmaking.GetLobbyData(id,key);
        public SteamRoomInfo Describe(ulong value,bool requireOwner=false)
        {
            var id=new CSteamID(value);if(!id.IsValid() || Data(id,"game")!=GameKey || Data(id,"protocol")!=NetworkCodec.Version)return null;
            string room=Data(id,"room"),code=Data(id,"code");if(!Guid.TryParseExact(room,"N",out _) || !RoomCode.IsValid(code) || !ulong.TryParse(Data(id,"owner"),out var owner) || !new CSteamID(owner).IsValid() || !int.TryParse(Data(id,"occupied"),out int count) || count<1 || count>6)return null;
            // GetLobbyOwner returns nil until this user has joined. Search metadata is only
            // a preview; verify its owner against Steam after joining and before connecting.
            if(requireOwner && owner!=SteamMatchmaking.GetLobbyOwner(id).m_SteamID)return null;
            string name=Data(id,"name");if(string.IsNullOrWhiteSpace(name) || name.Length>32)return null;
            string hostName=Data(id,"hostName");if(!PlayerNickname.IsValid(hostName) || !int.TryParse(Data(id,"players"),out int players) || players<1 || players>count)return null;
            return new SteamRoomInfo {LobbyId=value,Owner=owner,Name=name,Code=code,RoomId=room,HostName=hostName,Players=players,Occupied=count,Password=Data(id,"password")=="1",Busy=Data(id,"busy")=="1"};
        }
        public void CreateRoom(NetworkRoom room)
        {if(!Ready || Busy)throw new InvalidOperationException(Status);LeaveRoom();hostRoom=room;operation=1;Busy=true;deadline=Time.realtimeSinceStartup+15;Status="正在创建Steam房间…";create.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic,6));Changed?.Invoke();}
        private void Created(LobbyCreated_t value,bool io)
        {
            operation=0;Busy=false;if(io || value.m_eResult!=EResult.k_EResultOK || hostRoom==null){Status="Steam房间创建失败，请重试";Changed?.Invoke();return;}
            LobbyId=value.m_ulSteamIDLobby;
            listener=SteamNetworkingSockets.CreateListenSocketP2P(0,0,null);
            if(listener==HSteamListenSocket.Invalid){LeaveRoom();Status="Steam网络监听未能启动";Changed?.Invoke();return;}
            Publish(hostRoom);Status="Steam房间已创建";LobbyCreated?.Invoke(Describe(LobbyId,true));Changed?.Invoke();
        }
        public void FindRooms(string code=null)
        {
            if(!Ready || Busy)return;operation=3;Busy=true;deadline=Time.realtimeSinceStartup+15;Status="正在搜索Steam房间…";
            SteamMatchmaking.AddRequestLobbyListStringFilter("game",GameKey,ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter("protocol",NetworkCodec.Version,ELobbyComparison.k_ELobbyComparisonEqual);
            if(code!=null)SteamMatchmaking.AddRequestLobbyListStringFilter("code",code,ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
            list.Set(SteamMatchmaking.RequestLobbyList());Changed?.Invoke();
        }
        private void Listed(LobbyMatchList_t value,bool io)
        {
            operation=0;Busy=false;var found=new List<SteamRoomInfo>();if(!io)for(int i=0;i<value.m_nLobbiesMatching;i++){var room=Describe(SteamMatchmaking.GetLobbyByIndex(i).m_SteamID);if(room!=null)found.Add(room);}
            Rooms=found.ToArray();Status=io ? "Steam房间搜索失败" : Rooms.Length==0 ? "暂无可加入的Steam房间" : "";Changed?.Invoke();
            Debug.Log("STEAM_LOBBY_SEARCH raw="+value.m_nLobbiesMatching+" accepted="+Rooms.Length+" ioFailure="+io);
        }
        public void JoinRoom(ulong id)
        {if(!Ready || Busy)return;LeaveRoom();operation=2;Busy=true;deadline=Time.realtimeSinceStartup+15;Status="正在加入Steam房间…";join.Set(SteamMatchmaking.JoinLobby(new CSteamID(id)));Changed?.Invoke();}
        private void Joined(LobbyEnter_t value,bool io)
        {
            operation=0;Busy=false;if(io || value.m_EChatRoomEnterResponse!=(uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess){Status="Steam房间无法加入，可能已满或已关闭";Changed?.Invoke();return;}
            LobbyId=value.m_ulSteamIDLobby;var room=Describe(LobbyId,true);if(room==null || room.Busy || room.Occupied>=6 || room.Owner==UserId){LeaveRoom();Status="房间已关闭、已满或版本不同";Changed?.Invoke();return;}
            Status="正在连接Steam房主…";LobbyJoined?.Invoke(room);Changed?.Invoke();
        }
        public void Connect(ulong owner,Action<SteamSocketPeer> complete)
        {
            expectedOwner=owner;onConnected=complete;var identity=new SteamNetworkingIdentity();identity.SetSteamID64(owner);
            operation=4;pendingConnection=SteamNetworkingSockets.ConnectP2P(ref identity,0,0,null);Busy=true;deadline=Time.realtimeSinceStartup+15;
            if(pendingConnection==HSteamNetConnection.Invalid){Busy=false;Status="Steam连接无法创建";Changed?.Invoke();}
        }
        private bool Member(ulong id)
        {if(LobbyId==0)return false;var lobby=new CSteamID(LobbyId);for(int i=0;i<SteamMatchmaking.GetNumLobbyMembers(lobby);i++)if(SteamMatchmaking.GetLobbyMemberByIndex(lobby,i).m_SteamID==id)return true;return false;}
        private void ConnectionChanged(SteamNetConnectionStatusChangedCallback_t value)
        {
            var info=value.m_info;ulong remote=info.m_identityRemote.GetSteamID64();
            Debug.Log("STEAM_CONNECTION state="+info.m_eState+" remote="+remote+" endReason="+info.m_eEndReason+" detail="+info.m_szEndDebug);
            if(info.m_eState==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting && info.m_hListenSocket==listener && listener!=HSteamListenSocket.Invalid)
            {
                if(hostRoom==null || !Member(remote) || remote==UserId || hostRoom.Running || hostRoom.Lobby.Drafting || hostRoom.CountingDown || hostRoom.OccupiedSlots>=6){SteamNetworkingSockets.CloseConnection(value.m_hConn,0,"room unavailable",false);return;}
                SteamNetworkingSockets.AcceptConnection(value.m_hConn);return;
            }
            if(info.m_eState==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                // A user may leave while a connected callback is still queued; consult the live handle before constructing a peer.
                if(!SteamNetworkingSockets.GetConnectionInfo(value.m_hConn,out var current) || current.m_eState!=ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)return;
                info=current;remote=info.m_identityRemote.GetSteamID64();
                if(!info.m_identityRemote.GetSteamID().IsValid()){SteamNetworkingSockets.CloseConnection(value.m_hConn,0,"invalid identity",false);return;}
                var peer=new SteamSocketPeer(value.m_hConn,remote);peers[value.m_hConn.m_HSteamNetConnection]=peer;
                if(info.m_hListenSocket==listener && listener!=HSteamListenSocket.Invalid){if(!Member(remote) || hostRoom?.AcceptSteamPeer(peer)!=true)peer.Dispose();}
                else if(value.m_hConn==pendingConnection && remote==expectedOwner){pendingConnection=HSteamNetConnection.Invalid;operation=0;Busy=false;var complete=onConnected;onConnected=null;complete?.Invoke(peer);Changed?.Invoke();}
                else peer.Dispose();return;
            }
            if(info.m_eState==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer || info.m_eState==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                if(peers.TryGetValue(value.m_hConn.m_HSteamNetConnection,out var peer)){peer.Dispose();peers.Remove(value.m_hConn.m_HSteamNetConnection);}
                else SteamNetworkingSockets.CloseConnection(value.m_hConn,0,"connection ended",false);
                if(value.m_hConn==pendingConnection){LeaveRoom();Status="Steam房主连接失败，请重试";Changed?.Invoke();}
            }
        }
        public void Publish(NetworkRoom room)
        {
            if(!Ready || LobbyId==0 || !room.IsHost)return;var id=new CSteamID(LobbyId);
            var entries=new Dictionary<string,string>{{"game",GameKey},{"protocol",NetworkCodec.Version},{"owner",UserId.ToString()},{"hostName",room.Lobby.Members.Single(m=>m.Host).Name},{"players",room.Lobby.Members.Length.ToString()},{"room",room.Lobby.Id},{"code",room.Lobby.Code},{"name",room.Lobby.Name},{"occupied",room.OccupiedSlots.ToString()},{"password",room.Lobby.RequiresPassword ? "1" : "0"},{"busy",room.Running || room.Lobby.Drafting || room.CountingDown ? "1" : "0"}};
            foreach(var pair in entries)SteamMatchmaking.SetLobbyData(id,pair.Key,pair.Value);
            SteamMatchmaking.SetLobbyJoinable(id,!room.Running && !room.Lobby.Drafting && !room.CountingDown && room.OccupiedSlots<6);
            SteamFriends.SetRichPresence("connect","+connect_lobby "+LobbyId);
        }
        public void InviteFriends()
        {
            if(!Ready || LobbyId==0)return;
            if(SteamUtils.IsOverlayEnabled())SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(LobbyId));
            else {var room=Describe(LobbyId);if(room!=null){GUIUtility.systemCopyBuffer=room.Code;Status="房间号已复制，请在Steam中发给好友";Changed?.Invoke();}}
        }
        public void OpenFriends()
        {if(!Ready)return;if(SteamUtils.IsOverlayEnabled())SteamFriends.ActivateGameOverlay("Friends");else Application.OpenURL("steam://friends");}
        public void OpenFriendProfile(ulong id)
        {if(!Ready)return;if(SteamUtils.IsOverlayEnabled())SteamFriends.ActivateGameOverlayToUser("steamid",new CSteamID(id));else Application.OpenURL("https://steamcommunity.com/profiles/"+id);}
        public SteamFriendInfo[] GetFriends()
        {
            if(!Ready)return new SteamFriendInfo[0];var result=new List<SteamFriendInfo>();
            int count=SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for(int i=0;i<count;i++){var id=SteamFriends.GetFriendByIndex(i,EFriendFlags.k_EFriendFlagImmediate);result.Add(new SteamFriendInfo {Id=id.m_SteamID,Name=SteamFriends.GetFriendPersonaName(id),Online=SteamFriends.GetFriendPersonaState(id)!=EPersonaState.k_EPersonaStateOffline});}
            return result.OrderByDescending(f=>f.Online).ThenBy(f=>f.Name).ToArray();
        }
        public void Pump()
        {
            if(!Initialized)return;SteamAPI.RunCallbacks();
            if(communityTicketReady!=null && Time.realtimeSinceStartup>communityTicketDeadline){var callback=communityTicketReady;CancelCommunityTicket();callback(null);}
            foreach(var key in peers.Where(p=>!p.Value.Connected).Select(p=>p.Key).ToArray()){peers[key].Dispose();peers.Remove(key);}
            if(Busy && Time.realtimeSinceStartup>=deadline){if(operation==3){list.Cancel();Rooms=new SteamRoomInfo[0];Busy=false;operation=0;}else LeaveRoom();Status="Steam操作超时，请重试";Changed?.Invoke();}
        }
        public void LeaveRoom()
        {
            Busy=false;operation=0;create?.Cancel();join?.Cancel();list?.Cancel();onConnected=null;expectedOwner=0;
            if(!Initialized)return;
            if(pendingConnection!=HSteamNetConnection.Invalid)SteamNetworkingSockets.CloseConnection(pendingConnection,0,"room closed",false);pendingConnection=HSteamNetConnection.Invalid;
            foreach(var peer in peers.Values)peer.Dispose();peers.Clear();
            if(listener!=HSteamListenSocket.Invalid)SteamNetworkingSockets.CloseListenSocket(listener);listener=HSteamListenSocket.Invalid;
            if(LobbyId!=0)SteamMatchmaking.LeaveLobby(new CSteamID(LobbyId));LobbyId=0;hostRoom=null;SteamFriends.ClearRichPresence();
        }
        public void Dispose(){CancelCommunityTicket();communityTicketCallback?.Dispose();LeaveRoom();create?.Dispose();join?.Dispose();list?.Dispose();invitation?.Dispose();connections?.Dispose();if(Initialized)SteamAPI.Shutdown();Initialized=false;}
    }
}
