using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Soccer.Core;
using Soccer.Core.Networking;

string service=args.Length>0 ? args[0] : "http://127.0.0.1:18929";string output=args.Length>1 ? args[1] : "reports/community-029-service";
var json=new JsonSerializerOptions{IncludeFields=true,PropertyNameCaseInsensitive=true};var checks=new List<string>();var errors=new List<string>();
var tokens=new Dictionary<string,string>();using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(10)};var peers=new List<Peer>();
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
async Task<string> Call(string id,string path,object body=null,int expected=200,string method=null)
{
 using var request=new HttpRequestMessage(new HttpMethod(method??(body==null ? "GET" : "POST")),service+path);
 if(id!=null && tokens.TryGetValue(id,out var token))request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
 if(body!=null)request.Content=new StringContent(JsonSerializer.Serialize(body,json),Encoding.UTF8,"application/json");
 using var response=await http.SendAsync(request);string text=await response.Content.ReadAsStringAsync();Check((int)response.StatusCode==expected,path+" returns "+expected);if((int)response.StatusCode!=expected)throw new Exception(path+" "+text);return text;
}
async Task<CommunityState> State(string id)=>JsonSerializer.Deserialize<CommunityState>(await Call(id,"/api/community/state"),json);
async Task Action(string id,string action,string target="",string invitation="",bool ready=false,string mode="",string code="")=>await Call(id,"/api/community/action",new CommunityAction{Action=action,Target=target,Id=invitation,Ready=ready,Mode=mode,Code=code});
async Task Friend(string a,string b){await Call(a,"/api/friends/"+b+"/request",new{});await Call(b,"/api/friends/"+a+"/accept",new{});}
async Task Wait(Func<Task<bool>> done,int seconds=15){var deadline=DateTime.UtcNow.AddSeconds(seconds);while(DateTime.UtcNow<deadline){if(await done())return;await Task.Delay(80);}throw new Exception("asynchronous operation timed out");}
try
{
 await Call(null,"/api/community/state",expected:401);await Call(null,"/api/auth/steam",new{Ticket="1234567890ABCDEF",Name="测试"},503);
 string suffix=Guid.NewGuid().ToString("N").Substring(0,6);var users=Enumerable.Range(0,6).Select(i=>"t"+suffix+i).ToArray();
 await Call(null,"/api/register",new{Account="bad_account",Name="注册",Password="short"},400);
 foreach(var id in users){var response=JsonDocument.Parse(await Call(null,"/api/register",new{Account=id,Name="球员"+Array.IndexOf(users,id),Password="Test-password-029"}));tokens[id]=response.RootElement.GetProperty("accessToken").GetString();Check(!response.RootElement.TryGetProperty("Hash",out _),"registration never exposes password hashes");}
 await Call(null,"/api/login",new{Account=users[0],Password="wrong-password"},401);
 await Call(null,"/api/register",new{Account=users[0],Name="重名",Password="Test-password-029"},400);
 await Call(users[0],"/api/friends/"+users[0]+"/request",new{},400);
 await Friend(users[0],users[1]);await Friend(users[0],users[2]);await Friend(users[3],users[4]);await Friend(users[3],users[5]);await Friend(users[0],users[3]);
 var friends=JsonSerializer.Deserialize<FriendsData>(await Call(users[0],"/api/friends"),json);Check(friends.Friends.Length==3 && friends.Friends.Any(p=>p.DisplayName=="球员1"),"registered game friends expose their game names");
 await Call(users[0],"/api/chat",new ChatRequest{Channel="private",Target=users[5],Text="不应发送"},400);
 var message=JsonSerializer.Deserialize<ChatMessage>(await Call(users[0],"/api/chat",new{Channel="private",Target=users[1],Text="<b>你好，传给我</b>",SenderId=users[5]}),json);
 Check(message.SenderId==users[0] && message.Text=="<b>你好，传给我</b>","private chat binds authenticated sender and preserves literal markup");
 await Call(users[0],"/api/chat",new ChatRequest{Channel="private",Target=users[1],Text="频率"},400);
 var history=JsonSerializer.Deserialize<ChatHistory>(await Call(users[1],"/api/chat?channel=private&target="+users[0]),json);Check(history.Messages.Length==1,"friend receives private history");
 await Call(users[5],"/api/chat?channel=private&target="+users[0],expected:400);
 Check((await State(users[1])).Inbox.Any(m=>m.SenderId==users[0]),"offline-capable inbox exposes only recipient messages");
 foreach(int leader in new[]{0,3})
 {
  await Action(users[leader],"createParty");foreach(int member in new[]{leader+1,leader+2}){await Action(users[leader],"invite",users[member]);var invite=(await State(users[member])).Invites.Single();await Action(users[member],"accept",invitation:invite.Id);}
 }
 Check((await State(users[0])).Party.Members.Length==3,"three friends form a party");
}
catch(Exception e){errors.Add(e.ToString());}
// Continue the main queue scenario after verifying full-party rejection explicitly.
try
{
 var users=tokens.Keys.ToArray();if(users.Length!=6)throw new Exception("registration setup failed");
 await Call(users[0],"/api/community/action",new CommunityAction{Action="invite",Target=users[3]},400);
 await Call(users[1],"/api/community/action",new CommunityAction{Action="queue",Mode="ranked"},400);
 await Call(users[0],"/api/community/action",new CommunityAction{Action="queue",Mode="ranked"},400);
 foreach(var id in users){await State(id);await Action(id,"ready",ready:true);}
 await Action(users[0],"queue",mode:"ranked");await Action(users[3],"queue",mode:"ranked");
 CommunityState[] states=null;await Wait(async()=>{states=await Task.WhenAll(users.Select(State));return states.All(s=>s.Match!=null);});
 Check(states.Select(s=>s.Match.Id).Distinct().Count()==1 && states.All(s=>s.Match.BotMask==0),"ranked pairs six real authenticated players into one match");
 Check(states.Take(3).Select(s=>s.Match.Slot<3).Distinct().Count()==1 && states.Skip(3).Select(s=>s.Match.Slot<3).Distinct().Count()==1 && (states[0].Match.Slot<3)!=(states[3].Match.Slot<3),"matchmaking keeps both three-player parties intact on opposite teams");
 foreach(var state in states){var peer=new Peer(service,tokens[state.AccountId],state.Match);await peer.Connect();peers.Add(peer);}
 await Wait(()=>Task.FromResult(peers.All(p=>p.Replica!=null)),30);Check(peers.All(p=>p.Started),"six WebSocket clients complete real alternating draft and receive server kickoff");
 await peers[0].Send(PacketKind.Chat,NetworkCodec.Pack(w=>NetworkCodec.WriteFields(w,new ChatRequest{Channel="team",Text="队内测试"})));
 await Wait(()=>Task.FromResult(peers.Where(p=>(p.Slot<3)==(peers[0].Slot<3)).All(p=>p.Chat.Any(m=>m.Text=="队内测试"))));
 Check(peers.Where(p=>(p.Slot<3)!=(peers[0].Slot<3)).All(p=>!p.Chat.Any(m=>m.Text=="队内测试")),"server never delivers team chat to opponents");
 await peers[1].Send(PacketKind.Chat,NetworkCodec.Pack(w=>NetworkCodec.WriteFields(w,new ChatRequest{Channel="all",Text="全场测试"})));
 await Wait(()=>Task.FromResult(peers.All(p=>p.Chat.Any(m=>m.Text=="全场测试"))));Check(true,"all-match chat reaches both teams");
 await peers[0].Send(PacketKind.TeamSignal,NetworkCodec.Pack(w=>{w.Write(1);NetworkCodec.WriteFields(w,new TacticalSignal{PlayerId=peers[0].Slot,Kind=SignalKind.Attack,Position=Vec2.Zero});}));
 await Wait(()=>Task.FromResult(peers.Where(p=>(p.Slot<3)==(peers[0].Slot<3)).All(p=>p.Signals>0)));
 Check(peers.Where(p=>(p.Slot<3)!=(peers[0].Slot<3)).All(p=>p.Signals==0),"existing tactical signals remain team-only on the service transport");
 var striker=peers[0];var deadline=DateTime.UtcNow.AddSeconds(45);while(DateTime.UtcNow<deadline && !peers.All(p=>p.Finished))
 {
  var command=striker.StrikerCommand();await striker.SendInput(command);await Task.Delay(16);
 }
 Check(peers.All(p=>p.Finished),"server advances and finishes a real authoritative ranked match");
 await Wait(async()=>{states=await Task.WhenAll(users.Select(State));return states.All(s=>s.RankedGames==1);});
 Check(states.Any(s=>s.Match.RatingChange!=0),"a real scored goal produces a ranked win/loss rating change");
 Check(states.Sum(s=>s.Match.RatingChange)==0 && states.All(s=>s.Rating==1000+s.Match.RatingChange),"ranked settlement is server-owned, balanced and persisted once");
 Check(peers.All(p=>p.Errors.Count==0),"all six clients decode valid authoritative snapshots");
 await Task.Delay(300);Check((await State(users[0])).RankedGames==1,"repeated final snapshots cannot double-settle ratings");
 foreach(var id in users)await Action(id,"leaveMatch");foreach(var peer in peers)peer.Dispose();peers.Clear();
 var ratings=states.Select(s=>s.Rating).ToArray();
 foreach(var id in users){await State(id);await Action(id,"ready",ready:true);}
 await Action(users[0],"queue",mode:"normal");await Action(users[3],"queue",mode:"normal");
 await Wait(async()=>{states=await Task.WhenAll(users.Select(State));return states.All(s=>s.Match?.Playlist=="normal");});
 Check(states.Select(s=>s.Match.Id).Distinct().Count()==1 && states.All(s=>s.Match.BotMask==0),"ordinary matchmaking also requires six real players");
 foreach(var state in states){var peer=new Peer(service,tokens[state.AccountId],state.Match);await peer.Connect();peers.Add(peer);}
 await Wait(()=>Task.FromResult(peers.All(p=>p.Replica!=null)),30);
 await Action(users[0],"leaveMatch");await Call(users[0],"/api/community/action",new CommunityAction{Action="queue",Mode="normal"},400);
 Check((await State(users[0])).Match==null,"deliberately leaving hides the match while reserving membership until completion");
 await Wait(()=>Task.FromResult(peers.All(p=>p.Finished)),45);
 states=await Task.WhenAll(users.Select(State));Check(states.Select(s=>s.Rating).SequenceEqual(ratings) && states.All(s=>s.RankedGames==1),"ordinary match completion cannot change ranked ratings or ranked game counts");
 foreach(var id in users)await Action(id,"leaveMatch");foreach(var peer in peers)peer.Dispose();peers.Clear();
 await Action(users[0],"createRoom");var room=(await State(users[0])).Room;Check(room.Slots.Count(p=>p!=null)==3 && (await State(users[2])).Room.Code==room.Code,"party opens a shared private room together");
 await Action(users[0],"roomBots",ready:true);Check((await State(users[0])).Room.Bots.Length==3,"custom room can explicitly fill remaining slots with AI");
 await Call(users[3],"/api/chat?channel=room",expected:400);
 await Action(users[0],"roomBots",ready:false);await Action(users[0],"inviteRoom",users[3]);var invitation=(await State(users[3])).Invites.Single(v=>v.Kind=="room");await Action(users[3],"accept",invitation:invitation.Id);
 Check((await State(users[3])).Room.Code==room.Code && (await State(users[5])).Room.Code==room.Code && (await State(users[0])).Room.Slots.Count(p=>p!=null)==6,"game friend accepts room invitation with all three party members");
 await Call(users[0],"/api/chat",new ChatRequest{Channel="room",Text="房间见"});Check(JsonSerializer.Deserialize<ChatHistory>(await Call(users[3],"/api/chat?channel=room"),json).Messages.Any(m=>m.Text=="房间见"),"private room chat is visible to invited members");
}
catch(Exception e){errors.Add(e.ToString());Console.WriteLine("FAIL "+e.Message);foreach(var peer in peers)Console.WriteLine("CLIENT "+peer.Slot+" errors="+string.Join("|",peer.Errors)+" messages="+string.Join("|",peer.Chat.Select(m=>m.Channel+":"+m.Text)));}
finally{foreach(var peer in peers)peer.Dispose();}
Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"community-tests.json"),JsonSerializer.Serialize(new{passed=errors.Count==0,total=checks.Count,checks,errors},new JsonSerializerOptions{WriteIndented=true}));return errors.Count==0 ? 0 : 1;

sealed class Peer : IDisposable
{
 private readonly string service,token;private readonly MatchAssignment assignment;private readonly ClientWebSocket socket=new();private readonly CancellationTokenSource stop=new();private readonly SemaphoreSlim send=new(1);private readonly object gate=new();private long sequence;
 public MatchSimulation Replica;public int Slot=>assignment.Slot;public bool Started,Finished;public int Signals;public ConcurrentBag<ChatMessage> Chat=new();public ConcurrentBag<string> Errors=new();
 public Peer(string service,string token,MatchAssignment assignment){this.service=service;this.token=token;this.assignment=assignment;}
 public async Task Connect(){socket.Options.SetRequestHeader("Authorization","Bearer "+token);socket.Options.SetRequestHeader("X-SkillSoccer-Protocol",NetworkCodec.Version);await socket.ConnectAsync(new Uri(service.Replace("http://","ws://")+"/api/matches/"+assignment.Id+"/socket"),stop.Token);_=Run();}
 public async Task Send(PacketKind kind,byte[] body){await send.WaitAsync();try{await socket.SendAsync(new ArraySegment<byte>(RoomFrame.Encode(kind,body)),WebSocketMessageType.Binary,true,stop.Token);}finally{send.Release();}}
 public Task SendInput(PlayerCommand c)=>Send(PacketKind.Input,NetworkCodec.Pack(w=>{w.Write(1);w.Write(++sequence);NetworkCodec.WriteFields(w,c);}));
 public PlayerCommand StrikerCommand()
 {
  lock(gate){if(Replica==null || Replica.State.Phase!=MatchPhase.Playing)return default;var state=Replica.State;var p=state.Players[Slot];float side=Slot<3 ? 1 : -1;
   var goal=new Vec2(side*(Replica.Rules.FieldWidth/2+1),0);
   if(state.Ball.OwnerId==Slot && p.Position.X*side>=15)
    return new PlayerCommand{Aim=goal-p.Position,KickPressed=!p.Charging && p.KickCooldown<=0,KickHeld=true,KickReleased=p.Charging && p.ChargeTime>=.84f};
   var target=state.Ball.OwnerId!=Slot ? state.Ball.Position : new Vec2(side*16,9);
   return new PlayerCommand{Move=(target-p.Position).Normalized,Aim=(target-p.Position).Normalized,Navigate=true};}
 }
 private async Task Run()
 {
  try{var buffer=new byte[8192];while(!stop.IsCancellationRequested){using var bytes=new MemoryStream();WebSocketReceiveResult part;do{part=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),stop.Token);if(part.MessageType==WebSocketMessageType.Close)return;bytes.Write(buffer,0,part.Count);}while(!part.EndOfMessage);
   var packet=RoomFrame.Decode(bytes.ToArray());switch(packet.Kind){
    case PacketKind.Draft:var draft=CommunityWire.ReadDraft(packet.Body);if(draft.CurrentSlot==Slot){var pick=draft.Picks[Slot];await Send(PacketKind.DraftLock,NetworkCodec.Pack(w=>{w.Write(draft.Epoch);w.Write((int)pick.Character);w.Write(pick.Position.X);w.Write(pick.Position.Y);}));}break;
    case PacketKind.Start:var start=CommunityWire.ReadStart(packet.Body);lock(gate){Replica=new MatchSimulation(start.Rules,StarStrikerLoadouts.CreateRoster(),start.Lineup[0],MatchMode.BotMatch,start.Lineup);if(start.Positions!=null)Replica.ApplyOpeningPositions(start.Positions);for(int i=0;i<6;i++)Replica.ConfigureBot(i,(assignment.BotMask&(1<<i))!=0);Started=true;}break;
    case PacketKind.Snapshot:lock(gate){NetworkCodec.ApplySnapshot(Replica.State,packet.Body);Finished=Replica.State.Phase==MatchPhase.Finished;}break;
    case PacketKind.Chat:ChatMessage message=null;NetworkCodec.Read(packet.Body,r=>message=NetworkCodec.ReadFields<ChatMessage>(r));Chat.Add(message);break;
    case PacketKind.TeamSignal:Interlocked.Increment(ref Signals);break;
   }
  }}catch(Exception e) when(e is OperationCanceledException || e is WebSocketException){if(!stop.IsCancellationRequested)Errors.Add(e.Message);}catch(Exception e){Errors.Add(e.ToString());}
 }
 public void Dispose(){stop.Cancel();socket.Abort();socket.Dispose();}
}
