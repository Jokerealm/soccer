using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Soccer.Core;
using Soccer.Core.Networking;

namespace Soccer.Runtime
{
    /// <summary>Authenticated server match transport. All game callbacks run on Pump's caller.</summary>
    public sealed class ServiceMatchSession : IDisposable
    {
        public MatchAssignment Assignment {get;}
        public MatchDraft Draft {get;private set;}
        public bool Connected {get;private set;}
        public event Action DraftChanged;
        public event Action<NetworkStart> Started;
        public event Action<byte[]> Snapshot;
        public event Action<TacticalSignal> Signal;
        public event Action<ChatMessage> Chat;
        public event Action<string> Failed;
        private readonly ClientWebSocket socket=new ClientWebSocket();
        private readonly CancellationTokenSource stop=new CancellationTokenSource();
        private readonly ConcurrentQueue<WirePacket> incoming=new ConcurrentQueue<WirePacket>();
        private readonly ConcurrentQueue<byte[]> outgoing=new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim available=new SemaphoreSlim(0);
        private volatile bool opened;
        private string failure;
        private long sequence;
        private bool disposed;
        public ServiceMatchSession(string service,string token,MatchAssignment assignment)
        {
            Assignment=assignment;var address=new UriBuilder(service);address.Scheme=address.Scheme=="https" ? "wss" : "ws";
            address.Path=address.Path.TrimEnd('/')+"/api/matches/"+assignment.Id+"/socket";address.Query="";
            socket.Options.SetRequestHeader("Authorization","Bearer "+token);socket.Options.SetRequestHeader("X-SkillSoccer-Protocol",NetworkCodec.Version);socket.Options.KeepAliveInterval=TimeSpan.FromSeconds(15);
            _=Run(address.Uri);
        }
        private async Task Run(Uri uri)
        {
            Task writer=null;
            try
            {
                using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                {timeout.CancelAfter(TimeSpan.FromSeconds(10));await socket.ConnectAsync(uri,timeout.Token);}
                opened=true;
                writer=Task.Run(async()=>{
                    while(!stop.IsCancellationRequested)
                    {await available.WaitAsync(stop.Token);if(outgoing.TryDequeue(out var data))await socket.SendAsync(new ArraySegment<byte>(data),WebSocketMessageType.Binary,true,stop.Token);}
                });
                var chunk=new byte[8192];
                while(!stop.IsCancellationRequested)
                {
                    using(var message=new MemoryStream())
                    {
                        WebSocketReceiveResult frame;
                        do
                        {
                            frame=await socket.ReceiveAsync(new ArraySegment<byte>(chunk),stop.Token);
                            if(frame.MessageType==WebSocketMessageType.Close)throw new IOException();
                            if(frame.MessageType!=WebSocketMessageType.Binary || message.Length+frame.Count>NetworkCodec.MaxFrameBytes+1)throw new IOException();
                            message.Write(chunk,0,frame.Count);
                        }while(!frame.EndOfMessage);
                        if(incoming.Count>=256)throw new IOException();incoming.Enqueue(RoomFrame.Decode(message.ToArray()));
                    }
                }
            }
            catch(Exception e) when(e is WebSocketException || e is IOException || e is OperationCanceledException || e is InvalidOperationException)
            {if(!disposed)failure="对局连接已中断，可返回队伍重新连接。比赛由服务器继续结算。";}
            finally{opened=false;stop.Cancel();if(writer!=null)try{await writer;}catch(Exception){} }
        }
        public void Pump()
        {
            if(disposed && failure==null)return;Connected=opened;
            while(incoming.TryDequeue(out var packet))
            {
                try
                {
                    switch(packet.Kind)
                    {
                        case PacketKind.Draft:Draft=CommunityWire.ReadDraft(packet.Body);DraftChanged?.Invoke();break;
                        case PacketKind.Start:Draft=null;Started?.Invoke(CommunityWire.ReadStart(packet.Body));break;
                        case PacketKind.Snapshot:Snapshot?.Invoke(packet.Body);break;
                        case PacketKind.TeamSignal:
                            TacticalSignal signal=null;NetworkCodec.Read(packet.Body,r=>{if(r.ReadInt32()!=1)throw new InvalidDataException();signal=NetworkCodec.ReadFields<TacticalSignal>(r);});
                            if(signal==null || (signal.PlayerId<3)!=(Assignment.Slot<3))throw new InvalidDataException();Signal?.Invoke(signal);break;
                        case PacketKind.Chat:
                            ChatMessage chat=null;NetworkCodec.Read(packet.Body,r=>chat=NetworkCodec.ReadFields<ChatMessage>(r));chat.Text=ChatText.Normalize(chat.Text);Chat?.Invoke(chat);break;
                        default:throw new InvalidDataException();
                    }
                }
                catch(Exception e) when(e is IOException || e is ArgumentException){failure="对局消息无效，请更新到相同版本后重试。";Dispose();break;}
            }
            if(failure!=null){var reason=failure;failure=null;Failed?.Invoke(reason);}
        }
        private bool Send(PacketKind kind,byte[] body)
        {if(disposed || !opened)return false;if(outgoing.Count>=128){failure="对局网络发送拥塞，请检查连接。";Dispose();return false;}outgoing.Enqueue(RoomFrame.Encode(kind,body));available.Release();return true;}
        public void Lock(CharacterId id,Vec2 position)
        {if(Draft!=null)Send(PacketKind.DraftLock,NetworkCodec.Pack(w=>{w.Write(Draft.Epoch);w.Write((int)id);w.Write(position.X);w.Write(position.Y);}));}
        public void Input(PlayerCommand input)
        {if(NetworkCodec.ValidCommand(input))Send(PacketKind.Input,NetworkCodec.Pack(w=>{w.Write(1);w.Write(++sequence);NetworkCodec.WriteFields(w,input);}));}
        public bool SendSignal(SignalKind kind,Vec2 point,int target)
            =>Send(PacketKind.TeamSignal,NetworkCodec.Pack(w=>{w.Write(1);NetworkCodec.WriteFields(w,new TacticalSignal{PlayerId=Assignment.Slot,Kind=kind,Position=point,TargetId=target});}));
        public bool SendChat(string channel,string text)=>Send(PacketKind.Chat,NetworkCodec.Pack(w=>NetworkCodec.WriteFields(w,new ChatRequest{Channel=channel,Text=text})));
        public void Dispose(){if(disposed)return;disposed=true;Connected=false;stop.Cancel();socket.Abort();socket.Dispose();}
    }
}
