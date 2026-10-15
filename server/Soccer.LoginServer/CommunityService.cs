using System.Text.Json;
using Soccer.Core;
using Soccer.Core.Networking;

sealed class CommunityService : IDisposable
{
    private sealed class Party
    {internal string Id=Guid.NewGuid().ToString("N"),Leader="",Queue="";internal List<string> Members=new();internal HashSet<string> Ready=new();internal DateTimeOffset QueuedAt;}
    private sealed class Invitation
    {internal string Id=Guid.NewGuid().ToString("N"),From="",To="",Kind="",Code="";internal DateTimeOffset Expires=DateTimeOffset.UtcNow.AddMinutes(2);}
    private sealed class Room
    {internal string Code="",Owner="";internal string?[] Slots=new string?[6];internal HashSet<int> Bots=new();internal HashSet<string> Ready=new();internal BotDifficulty Difficulty=BotDifficulty.Player;internal DateTimeOffset? Countdown;}
    private readonly object gate=new();private readonly AccountService accounts;private readonly SocialService social;private readonly IConfiguration configuration;
    private readonly Dictionary<string,Party> parties=new(),partyOf=new();
    private readonly Dictionary<string,DateTimeOffset> presence=new(),lastChat=new();
    private readonly Dictionary<string,Invitation> invitations=new();private readonly Dictionary<string,Room> rooms=new(),roomOf=new();
    private readonly Dictionary<string,HostedMatch> matches=new(),matchOf=new();
    private readonly List<ChatMessage> messages=new();private long nextChat,revision;private readonly Dictionary<string,string> dismissed=new();
    private readonly string chatPath;private static readonly JsonSerializerOptions json=new(){IncludeFields=true};
    private readonly CancellationTokenSource stop=new();
    internal CommunityService(AccountService accounts,SocialService social,IConfiguration configuration)
    {this.accounts=accounts;this.social=social;this.configuration=configuration;
        chatPath=Path.GetFullPath(configuration["Chat:DataPath"] ?? Path.Combine(AppContext.BaseDirectory,"data","chat.json"));
        if(File.Exists(chatPath)){messages.AddRange(JsonSerializer.Deserialize<List<ChatMessage>>(File.ReadAllText(chatPath),json) ?? new());nextChat=messages.Select(m=>m.Id).DefaultIfEmpty().Max();}
        _=Tick();}
    private bool Online(string id)=>presence.TryGetValue(id,out var seen) && DateTimeOffset.UtcNow-seen<TimeSpan.FromSeconds(30);
    private PartyMember Person(string id)=>new(){AccountId=id,Name=accounts.Find(id)?.Name ?? id,Online=Online(id),Rating=accounts.Find(id)?.Rating ?? 1000};
    private Party GetParty(string id)
    {if(partyOf.TryGetValue(id,out var found))return found;var party=new Party{Leader=id,Members=new(){id}};parties[party.Id]=party;partyOf[id]=party;return party;}
    private void CancelQueue(Party party){party.Queue="";party.Ready.Clear();}
    private void Available(string id)
    {if(matchOf.TryGetValue(id,out var match) && !match.Finished)throw new ArgumentException("请先结束当前对局");if(roomOf.ContainsKey(id))throw new ArgumentException("请先离开当前房间");}
    private CommunityState Snapshot(string id)
    {
        var user=accounts.Find(id) ?? throw new ArgumentException("帐号不存在");var result=new CommunityState{Revision=++revision,AccountId=id,Name=user.Name,Rating=user.Rating,RankedGames=user.RankedGames};
        if(partyOf.TryGetValue(id,out var party))result.Party=new PartyData{Id=party.Id,LeaderId=party.Leader,Queue=party.Queue,QueuedAt=party.Queue.Length>0 ? party.QueuedAt.ToUnixTimeSeconds() : 0,
            Members=party.Members.Select(member=>{var person=Person(member);person.Ready=party.Ready.Contains(member);return person;}).ToArray()};
        if(roomOf.TryGetValue(id,out var room))result.Room=new CommunityRoom{Code=room.Code,OwnerId=room.Owner,Difficulty=room.Difficulty,Bots=room.Bots.Order().ToArray(),
            Countdown=room.Countdown.HasValue ? (float)Math.Max(0,3-(DateTimeOffset.UtcNow-room.Countdown.Value).TotalSeconds) : 0,
            Slots=room.Slots.Select(member=>{if(member==null)return null!;var person=Person(member);person.Ready=room.Ready.Contains(member);return person;}).ToArray()};
        if(matchOf.TryGetValue(id,out var match) && dismissed.GetValueOrDefault(id)!=match.Id)result.Match=new MatchAssignment{Id=match.Id,Playlist=match.Playlist,Phase=match.Phase,Slot=Array.IndexOf(match.Accounts,id),BotMask=match.BotMask,Names=match.Names,Rating=user.Rating,RatingChange=match.RatingChanges.GetValueOrDefault(id)};
        result.Invites=invitations.Values.Where(v=>v.To==id && v.Expires>DateTimeOffset.UtcNow).Select(v=>new CommunityInvite{Id=v.Id,FromId=v.From,FromName=accounts.Find(v.From)?.Name ?? v.From,Kind=v.Kind,Code=v.Code,Expires=v.Expires.ToUnixTimeSeconds()}).ToArray();
        result.QueuedPlayers=parties.Values.Where(p=>p.Queue.Length>0).Sum(p=>p.Members.Count);
        result.Inbox=messages.Where(m=>m.Channel=="private" && m.SenderId!=id && m.TargetId==PrivateKey(id,m.SenderId) && social.AreFriends(id,m.SenderId)).GroupBy(m=>m.SenderId).Select(g=>g.Last()).OrderByDescending(m=>m.Id).Take(30).ToArray();
        result.OnlineFriends=presence.Keys.Where(other=>other!=id && Online(other) && social.AreFriends(id,other)).ToArray();return result;
    }
    internal CommunityState State(string id)
    {lock(gate){presence[id]=DateTimeOffset.UtcNow;return Snapshot(id);}}
    internal CommunityState Action(string id,CommunityAction request)
    {
        lock(gate)
        {
            presence[id]=DateTimeOffset.UtcNow;
            switch(request.Action)
            {
                case "createParty":Available(id);GetParty(id);break;
                case "invite":
                {
                    Available(id);if(!social.AreFriends(id,request.Target))throw new ArgumentException("只能邀请游戏好友");
                    var party=GetParty(id);if(party.Leader!=id || party.Queue.Length>0)throw new ArgumentException("请由空闲队伍的队长邀请");
                    if(party.Members.Count>=3)throw new ArgumentException("队伍已满，最多三人");
                    if(invitations.Values.Any(v=>v.From==id && v.To==request.Target && v.Kind=="party" && v.Expires>DateTimeOffset.UtcNow))break;
                    if(invitations.Values.Count(v=>v.From==id)>=20)throw new ArgumentException("邀请太频繁，请稍后重试");
                    var invite=new Invitation{From=id,To=request.Target,Kind="party",Code=party.Id};invitations[invite.Id]=invite;break;
                }
                case "accept":
                {
                    Available(id);
                    if(!invitations.TryGetValue(request.Id,out var invite) || invite.To!=id || invite.Expires<=DateTimeOffset.UtcNow || !social.AreFriends(id,invite.From))throw new ArgumentException("邀请已失效");
                    if(invite.Kind=="room"){JoinRoom(id,invite.Code);invitations.Remove(invite.Id);break;}
                    if(!parties.TryGetValue(invite.Code,out var party) || party.Leader!=invite.From || party.Queue.Length>0 || party.Members.Count>=3 || party.Members.Any(roomOf.ContainsKey))throw new ArgumentException("队伍已满或不再可加入");
                    LeaveParty(id);party.Members.Add(id);partyOf[id]=party;party.Ready.Clear();invitations.Remove(invite.Id);break;
                }
                case "reject":if(invitations.TryGetValue(request.Id,out var rejected) && rejected.To==id)invitations.Remove(request.Id);break;
                case "leaveParty":Available(id);LeaveParty(id);break;
                case "kick":
                {
                    var party=GetParty(id);if(party.Leader!=id || request.Target==id || !party.Members.Contains(request.Target))throw new ArgumentException("只有队长可以移出队员");
                    Available(id);LeaveParty(request.Target);break;
                }
                case "ready":
                {
                    Available(id);var party=GetParty(id);if(party.Queue.Length>0)throw new ArgumentException("请先取消匹配");
                    if(request.Ready)party.Ready.Add(id);else party.Ready.Remove(id);break;
                }
                case "queue":
                {
                    Available(id);var party=GetParty(id);
                    if(party.Leader!=id)throw new ArgumentException("请由队长开始匹配");
                    if(request.Mode!="normal" && request.Mode!="ranked")throw new ArgumentException("匹配模式无效");
                    if(party.Members.Any(member=>!Online(member) || !party.Ready.Contains(member) || roomOf.ContainsKey(member) || matchOf.TryGetValue(member,out var active) && !active.Finished))throw new ArgumentException("请等待所有队员在线并准备");
                    if(party.Queue.Length==0){party.Queue=request.Mode;party.QueuedAt=DateTimeOffset.UtcNow;}break;
                }
                case "cancelQueue":{var party=GetParty(id);if(party.Leader!=id)throw new ArgumentException("请由队长取消匹配");CancelQueue(party);break;}
                case "createRoom":
                {
                    Available(id);var party=GetParty(id);if(party.Leader!=id)throw new ArgumentException("请由队长创建房间");
                    if(party.Members.Any(member=>roomOf.ContainsKey(member) || matchOf.TryGetValue(member,out var active) && !active.Finished))throw new ArgumentException("队员仍在房间或比赛中");
                    CancelQueue(party);var room=new Room{Owner=id,Code=RoomCode.Generate()};while(rooms.ContainsKey(room.Code))room.Code=RoomCode.Generate();
                    for(int i=0;i<party.Members.Count;i++){room.Slots[i]=party.Members[i];roomOf[party.Members[i]]=room;}rooms[room.Code]=room;break;
                }
                case "joinRoom":Available(id);JoinRoom(id,request.Code);break;
                case "inviteRoom":
                {
                    if(!roomOf.TryGetValue(id,out var room) || !social.AreFriends(id,request.Target))throw new ArgumentException("请先开房，并选择游戏好友");
                    if(room.Slots.Count(v=>v!=null)+room.Bots.Count>=6)throw new ArgumentException("房间已满");
                    if(invitations.Values.Count(v=>v.From==id)>=20)throw new ArgumentException("邀请太频繁");
                    var invite=new Invitation{From=id,To=request.Target,Kind="room",Code=room.Code};invitations[invite.Id]=invite;break;
                }
                case "leaveRoom":LeaveRoom(id);break;
                case "roomReady":
                {
                    if(!roomOf.TryGetValue(id,out var room))throw new ArgumentException("请先进入房间");
                    if(request.Ready)room.Ready.Add(id);else{room.Ready.Remove(id);room.Countdown=null;}break;
                }
                case "roomBots":
                {
                    if(!roomOf.TryGetValue(id,out var room) || room.Owner!=id)throw new ArgumentException("只有房主可以配置机器人");
                    if(!Enum.IsDefined(request.Difficulty))throw new ArgumentException("机器人难度无效");
                    room.Bots.Clear();if(request.Ready)for(int i=0;i<6;i++)if(room.Slots[i]==null)room.Bots.Add(i);
                    room.Difficulty=request.Difficulty;room.Ready.Clear();room.Countdown=null;break;
                }
                case "leaveMatch":
                    // The server continues simulation and settles ranked results independently of clients.
                    if(matchOf.TryGetValue(id,out var leaving)){if(leaving.Finished)matchOf.Remove(id);else dismissed[id]=leaving.Id;}break;
                case "rename":accounts.Rename(id,request.Name);social.EnsureAccount(id,request.Name);break;
                default:throw new ArgumentException("不支持的队伍操作");
            }
            return Snapshot(id);
        }
    }
    private void LeaveParty(string id)
    {
        if(!partyOf.Remove(id,out var party))return;CancelQueue(party);party.Members.Remove(id);
        if(party.Members.Count==0)parties.Remove(party.Id);else if(party.Leader==id)party.Leader=party.Members[0];
    }
    private void JoinRoom(string id,string code)
    {
        if(!RoomCode.TryNormalize(code,out var normalized) || !rooms.TryGetValue(normalized,out var room))throw new ArgumentException("房间不存在或已经开局");
        var joining=partyOf.TryGetValue(id,out var party) ? party.Members.ToArray() : new[]{id};
        if(party!=null && party.Leader!=id && joining.Length>1)throw new ArgumentException("请由队长带队加入好友房间");
        if(joining.Any(member=>roomOf.ContainsKey(member) || matchOf.TryGetValue(member,out var active) && !active.Finished))throw new ArgumentException("队员尚在其他房间或对局中");
        var cyan=Enumerable.Range(0,3).Where(i=>room.Slots[i]==null && !room.Bots.Contains(i)).ToArray();
        var amber=Enumerable.Range(3,3).Where(i=>room.Slots[i]==null && !room.Bots.Contains(i)).ToArray();
        var available=cyan.Length>=joining.Length && (amber.Length<joining.Length || cyan.Length>=amber.Length) ? cyan : amber;
        if(available.Length<joining.Length)throw new ArgumentException("房间没有足够的同队位置");
        if(party!=null)CancelQueue(party);
        for(int i=0;i<joining.Length;i++){room.Slots[available[i]]=joining[i];roomOf[joining[i]]=room;}
        room.Ready.Clear();room.Countdown=null;
    }
    private void LeaveRoom(string id)
    {
        if(!roomOf.Remove(id,out var room))return;int slot=Array.IndexOf(room.Slots,id);if(slot>=0)room.Slots[slot]=null;
        room.Ready.Clear();room.Countdown=null;
        if(room.Slots.All(member=>member==null))rooms.Remove(room.Code);else if(room.Owner==id)room.Owner=room.Slots.First(member=>member!=null)!;
    }
    internal ChatHistory Chat(string id,string channel,string target,long after)
    {
        lock(gate){var key=ChannelKey(id,channel,target);var history=messages.Where(m=>m.TargetId==key && m.Id>after).TakeLast(100).ToArray();return new ChatHistory{Messages=history,Cursor=history.LastOrDefault()?.Id ?? after};}
    }
    internal ChatMessage Send(string id,ChatRequest request)
    {
        lock(gate)
        {
            string key=ChannelKey(id,request.Channel,request.Target),text=ChatText.Normalize(request.Text);var now=DateTimeOffset.UtcNow;
            if(lastChat.TryGetValue(id,out var previous) && now-previous<TimeSpan.FromMilliseconds(700))throw new ArgumentException("发送太快，请稍后再试");
            lastChat[id]=now;var message=new ChatMessage{Id=++nextChat,SenderId=id,SenderName=accounts.Find(id)!.Name,Channel=request.Channel,TargetId=key,Text=text,SentUnixSeconds=now.ToUnixTimeSeconds()};
            messages.Add(message);
            if(request.Channel=="private")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(chatPath)!);string temporary=chatPath+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{File.WriteAllText(temporary,JsonSerializer.Serialize(messages.Where(m=>m.Channel=="private").TakeLast(5000),json));File.Move(temporary,chatPath,true);}
                catch{messages.Remove(message);throw;}finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
            if(messages.Count>10000)messages.RemoveRange(0,messages.Count-10000);return message;
        }
    }
    private static string PrivateKey(string a,string b)=>"private:"+(string.CompareOrdinal(a,b)<0 ? a+"|"+b : b+"|"+a);
    private string ChannelKey(string id,string channel,string target)
    {
        if(channel=="private")
        {if(!social.AreFriends(id,target))throw new ArgumentException("只能与游戏好友私聊");return PrivateKey(id,target);}
        if(channel=="party" && partyOf.TryGetValue(id,out var party))return "party:"+party.Id;
        if(channel=="room" && roomOf.TryGetValue(id,out var room))return "room:"+room.Code;
        throw new ArgumentException("聊天频道当前不可用");
    }
    internal HostedMatch? Match(string account,string id)
    {lock(gate)return matchOf.TryGetValue(account,out var match) && match.Id==id && dismissed.GetValueOrDefault(account)!=id ? match : null;}
    private MatchRules Rules(BotDifficulty difficulty=BotDifficulty.Player)=>new(){BotDifficulty=difficulty,MatchSeconds=Math.Clamp(configuration.GetValue("Gameplay:MatchSeconds",300f),1,600),OvertimeSeconds=Math.Clamp(configuration.GetValue("Gameplay:OvertimeSeconds",90f),1,180)};
    private void Launch(string?[] slots,bool ranked,BotDifficulty difficulty=BotDifficulty.Player,bool privateRoom=false)
    {
        if(matches.Count>=4096)throw new ArgumentException("对局服务繁忙");
        var match=new HostedMatch(slots,slots.Select((id,i)=>id==null ? BotDifficulties.Names[(int)difficulty]+" AI" : accounts.Find(id)!.Name).ToArray(),ranked,Rules(difficulty),Complete,privateRoom);
        matches[match.Id]=match;foreach(string id in slots.Where(id=>id!=null)!){matchOf[id]=match;dismissed.Remove(id);if(partyOf.TryGetValue(id,out var party))CancelQueue(party);}
    }
    private void Complete(HostedMatch match,int cyan,int amber)
    {
        lock(gate)if(match.Ranked)
        {try{match.RatingChanges=accounts.Settle(match.Id,match.Accounts,cyan,amber);}catch(IOException){_=Task.Run(async()=>{await Task.Delay(2000);Complete(match,cyan,amber);});}}
    }
    private async Task Tick()
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try{while(await timer.WaitForNextTickAsync(stop.Token))lock(gate)
        {
            var now=DateTimeOffset.UtcNow;
            foreach(var invite in invitations.Values.Where(v=>v.Expires<=now).ToArray())invitations.Remove(invite.Id);
            foreach(var party in parties.Values.Where(p=>p.Queue.Length>0 && p.Members.Any(id=>!Online(id))).ToArray())CancelQueue(party);
            foreach(var id in presence.Where(p=>now-p.Value>TimeSpan.FromMinutes(2)).Select(p=>p.Key).ToArray())
            {presence.Remove(id);LeaveRoom(id);LeaveParty(id);}
            foreach(var room in rooms.Values.ToArray())
            {
                var humans=room.Slots.Where(id=>id!=null).ToArray();bool ready=humans.Length+room.Bots.Count==6 && humans.All(id=>room.Ready.Contains(id!) && Online(id!));
                if(!ready){room.Countdown=null;continue;}room.Countdown??=now;
                if(now-room.Countdown<TimeSpan.FromSeconds(3) || matches.Count>=4096)continue;
                Launch(room.Slots.ToArray(),false,room.Difficulty,true);foreach(var id in humans)roomOf.Remove(id!);rooms.Remove(room.Code);
            }
            foreach(string mode in new[]{"normal","ranked"})TryMatch(mode);
            foreach(var match in matches.Values.Where(m=>m.Finished && now-m.Created>TimeSpan.FromMinutes(15)).ToArray())
            {matches.Remove(match.Id);foreach(var id in matchOf.Where(p=>p.Value==match).Select(p=>p.Key).ToArray())matchOf.Remove(id);match.Dispose();}
        }}catch(OperationCanceledException){}
    }
    private void TryMatch(string mode)
    {
        if(matches.Count>=4096)return;
        var waiting=parties.Values.Where(p=>p.Queue==mode).OrderBy(p=>p.QueuedAt).Take(24).ToArray();if(waiting.Sum(p=>p.Members.Count)<6)return;
        // Pack complete parties into two teams. No player is duplicated or split from their party.
        var left=new List<Party>();var right=new List<Party>();List<Party>? chosenLeft=null,chosenRight=null;
        int budget=16000;
        bool Search(int index,int a,int b)
        {
            if(--budget<=0)return false;
            if(a==3 && b==3)
            {
                double ar=left.SelectMany(p=>p.Members).Average(id=>accounts.Find(id)!.Rating),br=right.SelectMany(p=>p.Members).Average(id=>accounts.Find(id)!.Rating);
                double waited=(DateTimeOffset.UtcNow-left.Concat(right).Min(p=>p.QueuedAt)).TotalSeconds;
                if(mode=="ranked" && Math.Abs(ar-br)>Math.Min(1000,200+waited*5))return false;
                chosenLeft=left.ToList();chosenRight=right.ToList();return true;
            }
            if(index>=waiting.Length)return false;var party=waiting[index];int size=party.Members.Count;
            if(a+size<=3){left.Add(party);if(Search(index+1,a+size,b))return true;left.RemoveAt(left.Count-1);}
            if(b+size<=3){right.Add(party);if(Search(index+1,a,b+size))return true;right.RemoveAt(right.Count-1);}
            return Search(index+1,a,b);
        }
        if(!Search(0,0,0))return;
        var slots=chosenLeft!.SelectMany(p=>p.Members).Concat(chosenRight!.SelectMany(p=>p.Members)).Select(id=>(string?)id).ToArray();Launch(slots,mode=="ranked");
    }
    public void Dispose(){stop.Cancel();lock(gate)foreach(var match in matches.Values)match.Dispose();}
}
