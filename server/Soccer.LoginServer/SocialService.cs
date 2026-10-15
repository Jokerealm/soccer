using System.Text.Json;
using System.Security.Cryptography;
using Soccer.Core;
using Soccer.Core.Networking;

// Prototype account social data, persisted atomically independently of expiring room leases.
sealed class SocialService
{
    public sealed class ProfileState
    {
        public string DisplayName="";
        public HeroRecord[] Heroes=HeroRecord.Empty();
        public List<MatchRecord> Recent=new();
        public Dictionary<string,string> Uploaded=new();
    }
    public sealed class State
    {
        public int Version=1;
        public Dictionary<string,ProfileState> Profiles=new(){{"a",new()},{"b",new()}};
        public HashSet<string> Friends=new(),Requests=new();
    }
    internal Func<string,bool> KnownAccount=_=>true;
    private static readonly JsonSerializerOptions json=new(){IncludeFields=true};
    private readonly object gate=new();private readonly string path;private State state;
    internal SocialService(string path)
    {
        this.path=Path.GetFullPath(path);state=File.Exists(this.path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(this.path),json) ?? throw new InvalidDataException("社交存档无效") : new State();
        if(state.Version!=1 || state.Profiles==null || state.Friends==null || state.Requests==null)throw new InvalidDataException("社交存档无效");
        foreach(var profile in state.Profiles.Values){HeroRecord.Validate(profile.Heroes);foreach(var match in profile.Recent)MatchRecord.Validate(match);}
    }
    private static IResult Error(int status,string error)=>Results.Json(new{error},statusCode:status);
    private static string Pair(string a,string b)=>string.CompareOrdinal(a,b)<0 ? a+"|"+b : b+"|"+a;
    private SocialPerson Person(string account)=>new(){AccountId=account,DisplayName=state.Profiles[account].DisplayName.Length>0 ? state.Profiles[account].DisplayName : "帐号 "+account};
    internal bool AreFriends(string a,string b){lock(gate)return state.Friends.Contains(Pair(a,b));}
    internal void EnsureAccount(string id,string name)
    {
        lock(gate){if(state.Profiles.TryGetValue(id,out var found) && found.DisplayName==name)return;
            var result=Mutate(()=>{if(!state.Profiles.ContainsKey(id))state.Profiles[id]=new ProfileState();state.Profiles[id].DisplayName=name;return Results.Ok();});
            if(result is IStatusCodeHttpResult status && status.StatusCode>=400)throw new IOException("无法保存玩家资料");}
    }
    private FriendsData Friends(string account)=>new(){
        Friends=state.Profiles.Keys.Where(id=>id!=account && state.Friends.Contains(Pair(account,id))).Select(Person).ToArray(),
        Incoming=state.Profiles.Keys.Where(id=>state.Requests.Contains(id+">"+account)).Select(Person).ToArray(),
        Outgoing=state.Profiles.Keys.Where(id=>state.Requests.Contains(account+">"+id)).Select(Person).ToArray()};
    internal IResult Profile(string? account,string target)
    {
        lock(gate){if(account==null)return Error(401,"请重新登录");if(!KnownAccount(target) || !state.Profiles.TryGetValue(target,out var profile))return Error(404,"没有找到这个帐号");
            return Results.Ok(new PlayerProfileData {AccountId=target,DisplayName=Person(target).DisplayName,Heroes=profile.Heroes.Select(h=>h.Copy()).ToArray(),Recent=profile.Recent.ToArray()});}
    }
    internal IResult List(string? account){lock(gate)return account==null ? Error(401,"请重新登录") : Results.Ok(Friends(account));}
    private IResult Mutate(Func<IResult> mutation)
    {
        string previous=JsonSerializer.Serialize(state,json);
        try
        {
            var result=mutation();Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temporary,JsonSerializer.Serialize(state,json));File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
            return result;
        }
        catch(Exception e) when(e is IOException || e is UnauthorizedAccessException || e is OverflowException)
        {state=JsonSerializer.Deserialize<State>(previous,json)!;return Error(503,"暂时无法保存，请稍后重试");}
    }
    internal IResult AddMatch(string? account,MatchRecord match)
    {
        lock(gate)
        {
            if(account==null)return Error(401,"请重新登录");
            try{MatchRecord.Validate(match);}catch(ArgumentException){return Error(400,"对局记录无效");}
            if(match.DurationSeconds>86400 || match.FinishedUnixSeconds>DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds())return Error(400,"对局记录无效");
            var profile=state.Profiles[account];string hash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(match,json)));
            if(profile.Uploaded.TryGetValue(match.MatchId,out var saved))return saved==hash ? Results.Ok(new{saved=true}) : Error(409,"这场对局已经保存");
            if(profile.Uploaded.Count>=100000)return Error(429,"战绩存储已达上限");
            return Mutate(()=>{profile.Heroes.Single(h=>h.Character==match.Character).Add(match);profile.Uploaded.Add(match.MatchId,hash);
                profile.Recent.Insert(0,match);profile.Recent=profile.Recent.OrderByDescending(m=>m.FinishedUnixSeconds).Take(50).ToList();return Results.Ok(new{saved=true});});
        }
    }
    internal IResult Change(string? account,string target,string action)
    {
        lock(gate)
        {
            if(account==null)return Error(401,"请重新登录");if(!KnownAccount(target) || !state.Profiles.ContainsKey(target))return Error(404,"没有找到这个帐号");if(account==target)return Error(400,"不能添加自己");
            string pair=Pair(account,target),incoming=target+">"+account,outgoing=account+">"+target;
            if(action=="request" && state.Friends.Contains(pair))return Error(409,"你们已经是好友");
            if(action=="request" && state.Requests.Contains(incoming))return Error(409,"对方已发来申请，请在申请列表中接受");
            if((action=="accept" || action=="reject") && !state.Requests.Contains(incoming))return Error(404,"好友申请已失效");
            return Mutate(()=>{
                if(action=="request")state.Requests.Add(outgoing);
                else if(action=="accept"){state.Requests.Remove(incoming);state.Requests.Remove(outgoing);state.Friends.Add(pair);}
                else if(action=="reject")state.Requests.Remove(incoming);
                else if(action=="remove"){state.Friends.Remove(pair);state.Requests.Remove(incoming);state.Requests.Remove(outgoing);}
                return Results.Ok(Friends(account));});
        }
    }
}
