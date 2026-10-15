using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading.Channels;
using Soccer.Core;
using Soccer.Core.Networking;

sealed class HostedMatch : IDisposable
{
    internal readonly string Id=Guid.NewGuid().ToString("N");
    internal readonly string?[] Accounts;
    internal readonly string[] Names;
    internal readonly bool Ranked;internal readonly string Playlist;
    internal readonly int BotMask;
    internal readonly DateTimeOffset Created=DateTimeOffset.UtcNow;
    internal volatile string Phase="选人";
    internal Dictionary<string,int> RatingChanges=new();
    private readonly object gate=new();
    private readonly Dictionary<string,MatchSocket> sockets=new();
    private readonly RemoteCommandBuffer[] inputs=Enumerable.Range(0,6).Select(_=>new RemoteCommandBuffer()).ToArray();
    private readonly BotCommandSource[] bots;
    private readonly double[] signalTimes={-10,-10,-10,-10,-10,-10},chatTimes={-10,-10,-10,-10,-10,-10};
    private readonly MatchRules rules;
    private readonly MatchDraft draft;
    private MatchSimulation? simulation;
    private NetworkStart? start;
    private readonly List<MatchEvent> events=new();
    private readonly Action<HostedMatch,int,int> completed;
    private readonly CancellationTokenSource stop=new();
    private float draftAge;
    private long chatId;
    private bool reported;
    private DateTimeOffset finishedAt;
    internal HostedMatch(string?[] accounts,string[] names,bool ranked,MatchRules rules,Action<HostedMatch,int,int> completed,bool privateRoom=false)
    {
        Accounts=accounts;Names=names;Ranked=ranked;Playlist=ranked ? "ranked" : privateRoom ? "custom" : "normal";this.rules=rules;this.completed=completed;
        BotMask=Enumerable.Range(0,6).Where(i=>accounts[i]==null).Aggregate(0,(mask,i)=>mask|(1<<i));
        if(ranked && BotMask!=0)throw new ArgumentException("排位不允许机器人补位");
        draft=new MatchDraft(rules,accounts.Select(id=>id!=null).ToArray());
        bots=Enumerable.Range(0,6).Select(_=>new BotCommandSource(rules)).ToArray();
        _=Run();
    }
    internal bool Finished=>Phase=="已结束";
    internal async Task Connect(string account,WebSocket socket,CancellationToken cancellation)
    {
        int slot=Array.IndexOf(Accounts,account);if(slot<0){await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation,"不是本场玩家",cancellation);return;}
        var peer=new MatchSocket(socket);
        lock(gate)
        {
            if(sockets.Remove(account,out var old))old.Dispose();sockets[account]=peer;inputs[slot].Reset();
            if(start!=null){peer.Send(PacketKind.Start,CommunityWire.Start(start));peer.Send(PacketKind.Snapshot,NetworkCodec.Snapshot(simulation!.State));}
            else peer.Send(PacketKind.Draft,CommunityWire.Draft(draft));
        }
        await peer.Run(cancellation);
        lock(gate){if(sockets.GetValueOrDefault(account)==peer){sockets.Remove(account);inputs[slot].Reset();}}
        peer.Dispose();
    }
    private async Task Run()
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(8));var watch=Stopwatch.StartNew();double last=0,accumulator=0;
        try
        {
            while(await timer.WaitForNextTickAsync(stop.Token))
            {
                double now=watch.Elapsed.TotalSeconds;accumulator+=Math.Min(.15,now-last);last=now;
                bool finish=false;int cyan=0,amber=0;
                lock(gate)
                {
                    foreach(var entry in sockets.ToArray())
                    {
                        int slot=Array.IndexOf(Accounts,entry.Key);
                        int budget=128;while(budget-->0 && entry.Value.TryRead(out var packet))
                        {try{Handle(slot,packet,now);}catch(Exception e) when(e is ArgumentException || e is IOException || e is OverflowException){entry.Value.Dispose();break;}}
                    }
                    while(accumulator>=MatchSimulation.TickSeconds)
                    {
                        accumulator-=MatchSimulation.TickSeconds;
                        if(simulation==null)
                        {
                            bool changed=draft.Tick(MatchSimulation.TickSeconds);draftAge+=MatchSimulation.TickSeconds;
                            if(changed || draftAge>=.25f){draftAge=0;Broadcast(PacketKind.Draft,CommunityWire.Draft(draft));}
                            if(draft.Complete)
                            {
                                start=new NetworkStart{Rules=rules,Lineup=draft.Lineup(),Positions=draft.Positions()};
                                simulation=new MatchSimulation(rules,StarStrikerLoadouts.CreateRoster(),start.Lineup[0],MatchMode.BotMatch,start.Lineup);
                                simulation.ApplyOpeningPositions(start.Positions);for(int i=0;i<6;i++)simulation.ConfigureBot(i,Accounts[i]==null);
                                Phase="比赛中";Broadcast(PacketKind.Start,CommunityWire.Start(start));
                            }
                            continue;
                        }
                        if(simulation.State.Phase==MatchPhase.Finished)break;
                        var commands=new PlayerCommand[6];
                        for(int i=0;i<6;i++)commands[i]=Accounts[i]!=null && sockets.TryGetValue(Accounts[i]!,out var peer) && peer.Connected
                            ? inputs[i].Read(now) : bots[i].ReadCommand(simulation.State,i);
                        simulation.Step(commands);events.AddRange(simulation.State.Events);
                        if(simulation.State.Tick%3==0 || simulation.State.Phase==MatchPhase.Finished)
                        {Broadcast(PacketKind.Snapshot,NetworkCodec.Snapshot(simulation.State,events));events.Clear();}
                        if(simulation.State.Phase==MatchPhase.Finished && !reported)
                        {reported=true;Phase="已结束";finishedAt=DateTimeOffset.UtcNow;finish=true;cyan=simulation.State.CyanScore;amber=simulation.State.AmberScore;}
                    }
                }
                if(finish)completed(this,cyan,amber);
                if(Finished && DateTimeOffset.UtcNow-finishedAt>TimeSpan.FromSeconds(30))break;
            }
        }
        catch(OperationCanceledException){}
        finally{lock(gate)foreach(var peer in sockets.Values)peer.Dispose();}
    }
    private void Handle(int slot,WirePacket packet,double now)
    {
        if(packet.Kind==PacketKind.DraftLock)
        {
            int epoch=0;CharacterId id=default;Vec2 point=default;
            NetworkCodec.Read(packet.Body,r=>{epoch=r.ReadInt32();id=(CharacterId)r.ReadInt32();point=new Vec2(r.ReadSingle(),r.ReadSingle());});
            if(simulation!=null || epoch!=draft.Epoch)return;
            if(draft.TryLock(slot,id,point,out _))Broadcast(PacketKind.Draft,CommunityWire.Draft(draft));return;
        }
        if(packet.Kind==PacketKind.Input)
        {
            NetworkCodec.Read(packet.Body,r=>{int epoch=r.ReadInt32();long seq=r.ReadInt64();var command=NetworkCodec.ReadFields<PlayerCommand>(r);
                if(epoch==1 && simulation!=null && !inputs[slot].Accept(seq,command,now))throw new InvalidDataException();});return;
        }
        if(packet.Kind==PacketKind.TeamSignal)
        {
            int epoch=0;TacticalSignal signal=null!;NetworkCodec.Read(packet.Body,r=>{epoch=r.ReadInt32();signal=NetworkCodec.ReadFields<TacticalSignal>(r);});
            if(epoch!=1 || simulation==null)return;
            if(signal.PlayerId!=slot || !signal.Valid(rules.FieldWidth,rules.FieldHeight))throw new InvalidDataException();
            if(now-signalTimes[slot]<1)return;signalTimes[slot]=now;Broadcast(PacketKind.TeamSignal,packet.Body,slot<3);return;
        }
        if(packet.Kind==PacketKind.Chat)
        {
            ChatRequest request=null!;NetworkCodec.Read(packet.Body,r=>request=NetworkCodec.ReadFields<ChatRequest>(r));
            if(request.Channel!="team" && request.Channel!="all")throw new InvalidDataException();
            string text=ChatText.Normalize(request.Text);if(now-chatTimes[slot]<1)return;chatTimes[slot]=now;
            var message=new ChatMessage{Id=++chatId,SenderId=Accounts[slot]!,SenderName=Names[slot],Channel=request.Channel,Text=text,SentUnixSeconds=DateTimeOffset.UtcNow.ToUnixTimeSeconds()};
            Broadcast(PacketKind.Chat,NetworkCodec.Pack(w=>NetworkCodec.WriteFields(w,message)),request.Channel=="team" ? slot<3 : null);return;
        }
        throw new InvalidDataException("无效的比赛消息");
    }
    private void Broadcast(PacketKind kind,byte[] body,bool? cyan=null)
    {foreach(var peer in sockets)if(!cyan.HasValue || (Array.IndexOf(Accounts,peer.Key)<3)==cyan.Value)peer.Value.Send(kind,body);}
    public void Dispose(){stop.Cancel();}

    private sealed class MatchSocket : IDisposable
    {
        private readonly WebSocket socket;private readonly Channel<byte[]> outgoing=Channel.CreateBounded<byte[]>(64);
        private readonly ConcurrentQueue<WirePacket> incoming=new();private readonly CancellationTokenSource stop=new();
        internal bool Connected=>!stop.IsCancellationRequested && socket.State==WebSocketState.Open;
        internal MatchSocket(WebSocket socket){this.socket=socket;}
        internal void Send(PacketKind kind,byte[] body){if(!outgoing.Writer.TryWrite(RoomFrame.Encode(kind,body)))Dispose();}
        internal bool TryRead(out WirePacket packet)=>incoming.TryDequeue(out packet!);
        internal async Task Run(CancellationToken cancellation)
        {
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation,stop.Token);
            var writer=Task.Run(async()=>{try{await foreach(var bytes in outgoing.Reader.ReadAllAsync(linked.Token))await socket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Binary,true,linked.Token);}catch(Exception e) when(e is OperationCanceledException || e is WebSocketException){}finally{stop.Cancel();}});
            try
            {
                var chunk=new byte[8192];
                while(Connected)
                {
                    using var message=new MemoryStream();WebSocketReceiveResult frame;
                    do{frame=await socket.ReceiveAsync(new ArraySegment<byte>(chunk),linked.Token);if(frame.MessageType==WebSocketMessageType.Close)return;
                        if(frame.MessageType!=WebSocketMessageType.Binary || message.Length+frame.Count>NetworkCodec.MaxFrameBytes+1)throw new InvalidDataException();message.Write(chunk,0,frame.Count);
                    }while(!frame.EndOfMessage);
                    if(incoming.Count>=128)throw new InvalidDataException();incoming.Enqueue(RoomFrame.Decode(message.ToArray()));
                }
            }
            catch(Exception e) when(e is WebSocketException || e is OperationCanceledException || e is IOException){}
            finally{Dispose();try{await writer;}catch(OperationCanceledException){}}
        }
        public void Dispose(){if(stop.IsCancellationRequested)return;stop.Cancel();outgoing.Writer.TryComplete();socket.Abort();socket.Dispose();}
    }
}
