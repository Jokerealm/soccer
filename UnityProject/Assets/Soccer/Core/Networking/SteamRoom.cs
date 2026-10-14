using System;
using System.Globalization;

namespace Soccer.Core.Networking
{
    public static class SteamAccount
    {
        public static string FromId(ulong id)=>"steam:"+id.ToString(CultureInfo.InvariantCulture);
        public static bool IsValid(string value)=>value!=null && value.StartsWith("steam:",StringComparison.Ordinal) && value.Length<=32 && ulong.TryParse(value.Substring(6),NumberStyles.None,CultureInfo.InvariantCulture,out var id) && id>0 && value==FromId(id);
    }
    public sealed partial class NetworkRoom
    {
        public static NetworkRoom HostSteam(RoomOptions value)
        {
            if(value.Route!=NetworkRoute.Steam)throw new ArgumentException("请选择Steam联机");value.Validate();
            var room=new NetworkRoom {IsHost=true,options=value,LocalSlot=value.Team==Team.Cyan ? 0 : 3,Connected=true,Status="Steam房间已创建"};
            room.salt=RoomPassword.Random(16);room.passwordKey=value.UsePassword ? RoomPassword.Derive(value.Password,room.salt) : null;value.Password="";
            room.Lobby=new RoomLobby {Id=Guid.NewGuid().ToString("N"),Code=RoomCode.Generate(),Name=value.Name,Route=NetworkRoute.Steam,Port=1,RequiresPassword=value.UsePassword,
                Members=new[]{new RoomMember {Slot=room.LocalSlot,Name=value.PlayerName,AccountId=value.PlatformAccountId,Character=value.Character,Host=true}}};
            return room;
        }
        public bool AcceptSteamPeer(IRoomPeer peer)
        {
            if(!IsHost || !Connected || Lobby.Route!=NetworkRoute.Steam || peer==null || !SteamAccount.IsValid(peer.AuthenticatedAccountId))return false;
            if(guests.Count>=8){peer.Dispose();return false;}guests.Add(new Guest {Peer=peer,Began=-1,LastSeen=-1});return true;
        }
        public static NetworkRoom JoinSteam(IRoomPeer peer,RoomOptions value,string expectedRoomId)
        {
            if(peer==null || value.Route!=NetworkRoute.Steam || !SteamAccount.IsValid(peer.AuthenticatedAccountId))throw new ArgumentException("Steam连接无效");
            string password=value.Password ?? "";if(password.Length>64)throw new ArgumentException("密码过长");value.UsePassword=false;value.Validate();value.Password=password;
            var room=new NetworkRoom {server=peer,options=value,expectedRoomId=expectedRoomId,lastServer=-1};room.SendHello();return room;
        }
        private string OwnAccountId=>options.Route==NetworkRoute.Steam ? options.PlatformAccountId : options.Account?.Id ?? "";
        private void SendHello()=>server.Send(PacketKind.Hello,NetworkCodec.Pack(w=>{NetworkCodec.Text(w,NetworkCodec.Version);NetworkCodec.Text(w,options.PlayerName,24);w.Write((int)options.Character);w.Write((int)options.Team);w.Write((int)options.Route);NetworkCodec.Text(w,OwnAccountId,32);}));
    }
}
