using System;
using System.IO;
using System.Linq;

namespace Soccer.Core.Networking
{
    public sealed partial class NetworkRoom
    {
        public event Action<ChatMessage> ChatReceived;
        private long nextChatId;
        private readonly double[] lastChats={-10,-10,-10,-10,-10,-10};
        public bool SendChat(string channel,string text)
        {
            if(!Connected)return false;
            var request=new ChatRequest{Channel=channel};try{request.Text=ChatText.Normalize(text);}catch(ArgumentException){return false;}
            if(!ValidChatChannel(channel))return false;
            if(IsHost)return RelayChat(LocalSlot,request);
            return server.Send(PacketKind.Chat,NetworkCodec.Pack(w=>NetworkCodec.WriteFields(w,request)));
        }
        private bool ValidChatChannel(string channel)=>channel=="room" || Running && (channel=="team" || channel=="all");
        private bool RelayChat(int slot,ChatRequest request)
        {
            if(!ValidChatChannel(request.Channel))return false;
            string text=ChatText.Normalize(request.Text);if(clock-lastChats[slot]<.7)return false;lastChats[slot]=clock;
            var member=Lobby.Members.FirstOrDefault(m=>m.Slot==slot);if(member==null)return false;
            var message=new ChatMessage{Id=++nextChatId,SenderId=slot.ToString(),SenderName=member.Name,Channel=request.Channel,Text=text,SentUnixSeconds=DateTimeOffset.UtcNow.ToUnixTimeSeconds()};
            var bytes=NetworkCodec.Pack(w=>NetworkCodec.WriteFields(w,message));
            foreach(var guest in guests.Where(g=>g.Joined && (request.Channel!="team" || (g.Member.Slot<3)==(slot<3))))guest.Peer.Send(PacketKind.Chat,bytes);
            if(request.Channel!="team" || (LocalSlot<3)==(slot<3))ChatReceived?.Invoke(message);return true;
        }
        private void ReceiveChat(WirePacket packet,int slot=-1)
        {
            if(IsHost){ChatRequest request=null;NetworkCodec.Read(packet.Body,r=>request=NetworkCodec.ReadFields<ChatRequest>(r));RelayChat(slot,request);}
            else
            {
                ChatMessage message=null;NetworkCodec.Read(packet.Body,r=>message=NetworkCodec.ReadFields<ChatMessage>(r));
                if(!ValidChatChannel(message.Channel) || message.Id<1 || !int.TryParse(message.SenderId,out int sender) || sender<0 || sender>=6 ||
                    message.Channel=="team" && (sender<3)!=(LocalSlot<3))throw new InvalidDataException("聊天消息无效");
                message.Text=ChatText.Normalize(message.Text);ChatReceived?.Invoke(message);
            }
        }
    }
}
