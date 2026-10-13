using Soccer.Core;
using Soccer.Core.Networking;
using System.Diagnostics;
using System.Text.Json;

var results = new List<object>(); int failed = 0;
void Assert(bool value, string why) { if (!value) throw new Exception(why); }
void Test(string name, Action action)
{ try { action(); results.Add(new { name, passed=true, error="" }); Console.WriteLine("PASS "+name); } catch(Exception e) { failed++; results.Add(new {name,passed=false,error=e.Message}); Console.WriteLine("FAIL "+name+": "+e.Message); } }
double Now() => Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
void Until(Func<bool> done, params NetworkRoom[] rooms)
{ double deadline=Now()+8; while(!done() && Now()<deadline) {foreach(var room in rooms) room.Pump(Now());Thread.Sleep(2);} Assert(done(),"network operation timed out"); }
RoomOptions Options(NetworkRoute route=NetworkRoute.Internet, Team team=Team.Cyan, string password="", string account="a") => new RoomOptions { Route=route,Team=team,Name="联机测试房间",PlayerName="测试球员",Port=0,UsePassword=password.Length>0,Password=password,Account=route==NetworkRoute.Internet?DebugAccounts.Login(account,account):null };
NetworkRoom Client(NetworkRoom host, Team team=Team.Amber, string password="") => NetworkRoom.Join("127.0.0.1:"+host.Port,Options(host.Lobby.Route,team,password,"b"));
MatchSimulation Simulation(CharacterId[] lineup=null) => new MatchSimulation(new MatchRules(),StarStrikerLoadouts.CreateRoster(),CharacterId.Marine,MatchMode.BotMatch,lineup);
Test("both public debug accounts log in and reject invalid credentials",()=> {
 using var a=DebugAccounts.Login("a","a");using var b=DebugAccounts.Login("b","b");
 Assert(a?.Id=="a" && b?.Id=="b","debug login failed");
 foreach(var pair in new[]{("a","b"),("b","a"),("a",""),("c","c"),("A","a")})
  Assert(DebugAccounts.Login(pair.Item1,pair.Item2)==null,"invalid credentials admitted");
});
Test("account proof binds account, nonce and player identity",()=> {
 using var a=DebugAccounts.Login("a","a");var nonce=RoomPassword.Random(32);var proof=a.Proof(nonce,"identity-a");
 Assert(DebugAccounts.Verify("a",nonce,"identity-a",proof),"valid account proof rejected");
 Assert(!DebugAccounts.Verify("b",nonce,"identity-a",proof),"account substitution admitted");
 Assert(!DebugAccounts.Verify("a",RoomPassword.Random(32),"identity-a",proof),"account proof replay admitted");
 Assert(!DebugAccounts.Verify("a",nonce,"identity-b",proof),"player substitution admitted");
 a.Dispose();bool rejected=false;try{a.Proof(nonce,"identity-a");}catch(ObjectDisposedException){rejected=true;}Assert(rejected,"logout retained signing key");
});
Test("internet rooms require login while LAN remains available without accounts",()=> {
 foreach(bool host in new[]{true,false}){var options=Options();options.Account=null;bool rejected=false;try{using var room=host?NetworkRoom.Host(options):NetworkRoom.Join("127.0.0.1:27860",options);}catch(ArgumentException e){rejected=e.Message.Contains("登录");}Assert(rejected,"internet admitted missing login");}
 using var lan=NetworkRoom.Host(Options(NetworkRoute.LAN));using var guest=Client(lan);Until(()=>guest.Connected,lan,guest);
 Assert(lan.Lobby.Members.All(m=>m.AccountId==""),"LAN assigned a fabricated account");
});
Test("internet host rejects forged account proof even in an unlocked room",()=> {
 using var host=NetworkRoom.Host(Options());using var socket=new System.Net.Sockets.TcpClient("127.0.0.1",host.Port);using var peer=new RoomPeer(socket);
 peer.Send(PacketKind.Hello,NetworkCodec.Pack(w=>{NetworkCodec.Text(w,NetworkCodec.Version);NetworkCodec.Text(w,"forged",24);w.Write((int)CharacterId.Marine);w.Write((int)Team.Amber);w.Write((int)NetworkRoute.Internet);NetworkCodec.Text(w,"b",16);}));
 bool challenged=false;Until(()=>{while(peer.TryRead(out var packet))if(packet.Kind==PacketKind.Challenge)challenged=true;return challenged;},host);
 peer.Send(PacketKind.Authenticate,NetworkCodec.Pack(w=>{w.Write((byte)32);w.Write(new byte[32]);w.Write((byte)0);}));
 string reason="";Until(()=>{while(peer.TryRead(out var packet))if(packet.Kind==PacketKind.Reject)NetworkCodec.Read(packet.Body,r=>reason=NetworkCodec.Text(r));return reason.Length>0;},host);
 Assert(reason.Contains("帐号验证失败") && host.Lobby.Members.Length==1,"forged account joined");
});
Test("password proofs bind nonce and player identity",()=> {
 var salt=RoomPassword.Random(16);var nonce=RoomPassword.Random(32);var key=RoomPassword.Derive("test-password",salt);var proof=RoomPassword.Proof(key,nonce,"player-a");
 Assert(RoomPassword.Equal(proof,RoomPassword.Proof(key,nonce,"player-a")),"valid proof rejected");
 Assert(!RoomPassword.Equal(proof,RoomPassword.Proof(key,RoomPassword.Random(32),"player-a")),"nonce replay accepted");
 Assert(!RoomPassword.Equal(proof,RoomPassword.Proof(key,nonce,"player-b")),"identity substitution accepted");
});
Test("open internet room ignores disabled password and joins without password",()=> {
 var options=Options();options.Password="ignored";using var host=NetworkRoom.Host(options);using var client=Client(host);
 Until(()=>client.Connected,host,client);Assert(!host.Lobby.RequiresPassword && host.Lobby.Members.Length==2 && client.LocalSlot==3,"open join did not allocate opposing team");
 Assert(host.Lobby.Members[0].AccountId=="a" && host.Lobby.Members[1].AccountId=="b" && client.Lobby.Members[1].AccountId=="b","verified identities were not replicated");
});
Test("locked internet room rejects incorrect password",()=> {
 using var host=NetworkRoom.Host(Options(password:"correct"));using var client=Client(host,password:"wrong");string reason="";client.Failed+=s=>reason=s;
 Until(()=>reason.Length>0,host,client);Assert(reason.Contains("密码") && host.Lobby.Members.Length==1 && !client.Connected,"wrong password admitted client");
});
Test("locked internet room rejects omitted password",()=> {
 using var host=NetworkRoom.Host(Options(password:"correct"));using var client=Client(host);string reason="";client.Failed+=s=>reason=s;
 Until(()=>reason.Length>0,host,client);Assert(reason.Contains("密码") && host.Lobby.Members.Length==1,"missing password admitted client");
});
Test("locked internet room accepts correct password",()=> {
 using var host=NetworkRoom.Host(Options(password:"正确密码123"));using var client=Client(host,password:"正确密码123");
 Until(()=>client.Connected,host,client);Assert(client.Lobby.RequiresPassword && host.Lobby.Members.Length==2,"correct proof not accepted");
});
Test("room route mismatch cannot join a LAN room as internet",()=> {
 using var host=NetworkRoom.Host(Options(NetworkRoute.LAN));using var client=NetworkRoom.Join("127.0.0.1:"+host.Port,Options(NetworkRoute.Internet,Team.Amber));string reason="";client.Failed+=s=>reason=s;
 Until(()=>reason.Length>0,host,client);Assert(host.Lobby.Members.Length==1,"route mismatch bypassed admission");
});
Test("LAN discovery finds real UDP responder and usable TCP address",()=> {
 using var host=NetworkRoom.Host(Options(NetworkRoute.LAN));using var discovery=new LanDiscovery();double deadline=Now()+4;
 while(discovery.Rooms.Count==0 && Now()<deadline){host.Pump(Now());discovery.Pump(Now());Thread.Sleep(5);}
 Assert(discovery.Rooms.Any(r=>r.Lobby.Name==host.Lobby.Name && r.Address.EndsWith(":"+host.Port)),"no LAN advertisement received");
 using var client=NetworkRoom.Join(discovery.Rooms.First().Address,Options(NetworkRoute.LAN,Team.Amber));Until(()=>client.Connected,host,client);
});
Test("ready and character/team selection synchronize before host starts",()=> {
 using var host=NetworkRoom.Host(Options());using var client=Client(host);Until(()=>client.Connected,host,client);
 Assert(!host.CanStart,"unready guest did not block start");client.SetChoice(CharacterId.Thor,Team.Cyan);
 Until(()=>client.LocalSlot==1 && host.Lobby.Members.Any(m=>!m.Host && m.Character==CharacterId.Thor),host,client);
 client.SetReady(true);Until(()=>host.CanStart,host,client);NetworkStart start=null;client.Started+=s=>start=s;
 host.StartMatch(new MatchRules());Until(()=>start!=null,host,client);Assert(start.Lineup[1]==CharacterId.Thor && start.Lineup.Length==6,"selected lineup was not sent");
});
Test("full team rejects fourth member while the other team can still join",()=> {
 using var host=NetworkRoom.Host(Options());using var a=Client(host,Team.Cyan);using var b=Client(host,Team.Cyan);Until(()=>a.Connected&&b.Connected,host,a,b);
 using var extra=Client(host,Team.Cyan);string reason="";extra.Failed+=s=>reason=s;Until(()=>reason.Length>0,host,a,b,extra);Assert(reason.Contains("队伍已满"),"team capacity was not enforced");
 using var opponent=Client(host);Until(()=>opponent.Connected,host,a,b,opponent);Assert(host.Lobby.Members.Length==4,"free opposing slot rejected");
});
Test("late join rejected after match start",()=> {
 using var host=NetworkRoom.Host(Options());host.StartMatch(new MatchRules());using var client=Client(host);string reason="";client.Failed+=s=>reason=s;
 Until(()=>reason.Length>0,host,client);Assert(reason.Contains("比赛已经开始"),"late join entered active match");
});
Test("remote input controls only assigned slot and host snapshots synchronize ball and players",()=> {
 using var host=NetworkRoom.Host(Options());using var client=Client(host);Until(()=>client.Connected,host,client);client.SetReady(true);Until(()=>host.CanStart,host,client);
 MatchSimulation authority=null, replica=null;host.Started+=s=>authority=Simulation(s.Lineup);client.Started+=s=>replica=Simulation(s.Lineup);
 client.SnapshotReceived+=bytes=>NetworkCodec.ApplySnapshot(replica.State,bytes);host.StartMatch(new MatchRules());Until(()=>replica!=null,host,client);
 var origin=authority.State.Players[3].Position;var other=authority.State.Players[0].Position;
 client.SendInput(new PlayerCommand {Move=new Vec2(0,1),Aim=new Vec2(0,1)});Thread.Sleep(20);host.Pump(Now());
 for(int i=0;i<12;i++){var commands=new PlayerCommand[6];commands[3]=host.ReadInput(3);authority.Step(commands);host.Publish(authority.State);}
 Until(()=>replica.State.Tick==authority.State.Tick,host,client);
 Assert(authority.State.Players[3].Position.Y>origin.Y,"assigned player did not move");Assert(authority.State.Players[0].Position.X==other.X && authority.State.Players[0].Position.Y==other.Y,"remote controlled another slot");
 Assert(replica.State.Players[3].Position.Y==authority.State.Players[3].Position.Y && replica.State.Ball.Position.X==authority.State.Ball.Position.X,"replica diverged from authoritative state");
});
Test("guest disconnect releases slot and notifies host for AI takeover",()=> {
 using var host=NetworkRoom.Host(Options());var client=Client(host);Until(()=>client.Connected,host,client);int left=-1;host.PlayerLeft+=s=>left=s;client.Dispose();
 Until(()=>left==3,host);Assert(!host.IsHumanSlot(3) && host.Lobby.Members.Length==1,"disconnected slot remained human");
});
Test("host closure is reported and client stops",()=> {
 var host=NetworkRoom.Host(Options());using var client=Client(host);Until(()=>client.Connected,host,client);string reason="";client.Failed+=s=>reason=s;host.Dispose();
 Until(()=>reason.Length>0,client);Assert(!client.Connected,"client remained connected after host exit");
});
Test("return to lobby resets readiness and allows a new match",()=> {
 using var host=NetworkRoom.Host(Options());using var client=Client(host);Until(()=>client.Connected,host,client);client.SetReady(true);Until(()=>host.CanStart,host,client);
 bool started=false,returned=false;client.Started+=s=>started=true;client.ReturnedToLobby+=()=>returned=true;host.StartMatch(new MatchRules());Until(()=>started,host,client);
 host.ReturnToLobby();Until(()=>returned,host,client);Assert(!client.Running && !host.CanStart && !client.Lobby.Members.Single(m=>!m.Host).Ready,"rematch retained previous readiness");
});
Test("input buffer preserves short button edges and rejects replay/nonfinite commands",()=> {
 var buffer=new RemoteCommandBuffer();Assert(buffer.Accept(1,new PlayerCommand {KickPressed=true},1),"first input rejected");buffer.Accept(2,new PlayerCommand {KickReleased=true},1.01);
 var input=buffer.Read(1.02);Assert(input.KickPressed&&input.KickReleased,"short click edge lost");Assert(!buffer.Read(1.03).KickPressed,"edge executed twice");
 Assert(!buffer.Accept(1,default,1.04),"replayed input accepted");Assert(!buffer.Accept(3,new PlayerCommand{Move=new Vec2(float.NaN,0)},1.05),"nonfinite input accepted");
 Assert(buffer.Read(2).Cancel && !buffer.Read(2.1).Cancel,"timeout did not clear actions once");
});
Test("complete snapshot round trip includes effects, resources, orders and events",()=> {
 var source=Simulation();source.State.Phase=MatchPhase.Playing;source.State.Players[3].Health=42;source.State.Players[3].ECooldown=3;source.State.Players[3].Order=UnitOrderKind.Move;source.State.Players[3].OrderPosition=new Vec2(4,5);source.State.Players[3].MutablePath.Add(new Vec2(2,3));
 source.State.MutableBombs.Add(new RemoteBomb{ActorId=3,Position=new Vec2(4,5),Remaining=2,Radius=3});source.State.MutableEvents.Add(new MatchEvent(MatchEventKind.Skill,3,slot:SkillSlot.E));
 var target=Simulation();NetworkCodec.ApplySnapshot(target.State,NetworkCodec.Snapshot(source.State));
 Assert(target.State.Players[3].Health==42&&target.State.Players[3].ECooldown==3&&target.State.Players[3].NavigationPath.Count==1&&target.State.Bombs[0].ActorId==3&&target.State.Events.Count==1,"snapshot omitted visible game state");
});
Test("malformed snapshot cannot partially mutate live state",()=> {
 var source=Simulation();source.State.Players[3].Health=12;var bytes=NetworkCodec.Snapshot(source.State);var target=Simulation();bool rejected=false;
 try{NetworkCodec.ApplySnapshot(target.State,bytes[..^1]);}catch{rejected=true;}Assert(rejected&&target.State.Players[3].Health==100,"truncated snapshot partially mutated state");
});
Test("invalid passwords, ports and room addresses reject before listening",()=> {
 foreach(var options in new[]{new RoomOptions{UsePassword=true,Password=""},new RoomOptions{Port=-1},new RoomOptions{Route=NetworkRoute.LAN,UsePassword=true,Password="x"}}){bool rejected=false;try{options.Validate();}catch{rejected=true;}Assert(rejected,"invalid room option accepted");}
 foreach(var address in new[]{"", "localhost:0", "localhost:99999", "host/path", "localhost:bad"}){bool rejected=false;try{NetworkRoom.ParseAddress(address,out _,out _);}catch{rejected=true;}Assert(rejected,"invalid address accepted");}
});
Test("in-flight ready and input packets survive return-to-lobby transitions",()=> {
 using var host=NetworkRoom.Host(Options());using var socket=new System.Net.Sockets.TcpClient("127.0.0.1",host.Port);using var peer=new RoomPeer(socket);
 peer.Send(PacketKind.Hello,NetworkCodec.Pack(w=>{NetworkCodec.Text(w,NetworkCodec.Version);NetworkCodec.Text(w,"raw-guest",24);w.Write((int)CharacterId.Marine);w.Write((int)Team.Amber);w.Write((int)NetworkRoute.Internet);NetworkCodec.Text(w,"b",16);}));
 byte[] nonce=null;Until(()=>{while(peer.TryRead(out var packet))if(packet.Kind==PacketKind.Challenge)NetworkCodec.Read(packet.Body,r=>{r.ReadBoolean();r.ReadBytes(16);nonce=r.ReadBytes(32);});return nonce!=null;},host);
 using var account=DebugAccounts.Login("b","b");var proof=account.Proof(nonce,NetworkCodec.Version+"|b|raw-guest|"+(int)CharacterId.Marine+"|"+(int)Team.Amber);
 peer.Send(PacketKind.Authenticate,NetworkCodec.Pack(w=>{w.Write((byte)proof.Length);w.Write(proof);w.Write((byte)0);}));Until(()=>host.Lobby.Members.Length==2,host);
 peer.Send(PacketKind.Ready,NetworkCodec.Pack(w=>w.Write(true)));Until(()=>host.CanStart,host);host.StartMatch(new MatchRules());
 peer.Send(PacketKind.Ready,NetworkCodec.Pack(w=>w.Write(false)));Thread.Sleep(20);host.Pump(Now());
 Assert(peer.Connected&&host.Lobby.Members.Length==2,"late ready disconnected a valid guest");host.ReturnToLobby();
 peer.Send(PacketKind.Input,NetworkCodec.Pack(w=>{w.Write(1L);NetworkCodec.WriteFields(w,new PlayerCommand{Move=new Vec2(0,1)});}));Thread.Sleep(20);host.Pump(Now());
 Assert(peer.Connected&&host.Lobby.Members.Length==2,"late match input disconnected a rematching guest");
 peer.Send(PacketKind.Ready,NetworkCodec.Pack(w=>w.Write(true)));Until(()=>host.CanStart,host);host.StartMatch(new MatchRules());
 peer.Send(PacketKind.Input,NetworkCodec.Pack(w=>{w.Write(1);w.Write(1L);NetworkCodec.WriteFields(w,new PlayerCommand{Move=new Vec2(0,1)});}));Thread.Sleep(20);host.Pump(Now());
 Assert(host.ReadInput(3).Move.LengthSquared==0,"previous game's input moved new game's player");
 peer.Send(PacketKind.Input,NetworkCodec.Pack(w=>{w.Write(2);w.Write(2L);NetworkCodec.WriteFields(w,new PlayerCommand{Move=new Vec2(0,1)});}));Thread.Sleep(20);host.Pump(Now());
 Assert(host.ReadInput(3).Move.Y==1,"current game's input was not accepted after rematch");
});
Test("oversized unauthenticated control frame is rejected without room admission",()=> {
 using var host=NetworkRoom.Host(Options());using var socket=new System.Net.Sockets.TcpClient("127.0.0.1",host.Port);using var peer=new RoomPeer(socket);
 peer.Send(PacketKind.Hello,new byte[9000]);Until(()=>!peer.Connected,host);Assert(host.Lobby.Members.Length==1,"oversized frame admitted a guest");
});
Test("invalid owner snapshot is rejected atomically",()=> {
 var source=Simulation();source.State.Players[3].Health=12;source.State.Ball.OwnerId=99;var target=Simulation();bool rejected=false;
 try{NetworkCodec.ApplySnapshot(target.State,NetworkCodec.Snapshot(source.State));}catch{rejected=true;}Assert(rejected&&target.State.Players[3].Health==100&&target.State.Ball.OwnerId==-1,"invalid ball owner partly applied");
});
Test("six concurrent players occupy unique slots and all can ready",()=> {
 using var host=NetworkRoom.Host(Options(team:Team.Amber));var clients=new List<NetworkRoom>();
 try{
  for(int i=0;i<5;i++)clients.Add(Client(host,i<3?Team.Cyan:Team.Amber));
  var all=new[]{host}.Concat(clients).ToArray();Until(()=>clients.All(c=>c.Connected),all);
  Assert(host.Lobby.Members.Length==6&&host.Lobby.Members.Select(m=>m.Slot).Distinct().Count()==6&&host.LocalSlot==3,"concurrent admission duplicated positions");
  foreach(var client in clients)client.SetReady(true);Until(()=>host.CanStart,all);
  foreach(var client in clients)Assert(client.LocalSlot!=host.LocalSlot,"guest got host position");
 }finally{foreach(var client in clients)client.Dispose();}
});
Test("each IPv4 subnet gets its own directed broadcast including non-24-bit masks",()=> {
 Assert(LanInterfaces.Broadcast(System.Net.IPAddress.Parse("192.168.50.7"),System.Net.IPAddress.Parse("255.255.255.0")).ToString()=="192.168.50.255","24-bit broadcast wrong");
 Assert(LanInterfaces.Broadcast(System.Net.IPAddress.Parse("10.2.3.4"),System.Net.IPAddress.Parse("255.255.0.0")).ToString()=="10.2.255.255","16-bit broadcast wrong");
});
Test("LAN announcements discover two hosts and deduplicate broadcast and loopback replies",()=> {
 using var first=NetworkRoom.Host(Options(NetworkRoute.LAN));using var second=NetworkRoom.Host(Options(NetworkRoute.LAN));using var discovery=new LanDiscovery();
 double deadline=Now()+5;while(discovery.Rooms.Count<2 && Now()<deadline){first.Pump(Now());second.Pump(Now());discovery.Pump(Now());Thread.Sleep(5);}
 Assert(discovery.Rooms.Count==2 && discovery.Rooms.Select(r=>r.Lobby.Id).Distinct().Count()==2,"missing or duplicate LAN host");
 var found=discovery.Rooms.Single(r=>r.Lobby.Id==second.Lobby.Id);using var guest=NetworkRoom.Join(found.Address,Options(NetworkRoute.LAN,Team.Amber));Until(()=>guest.Connected,first,second,guest);
 Assert(second.Lobby.Members.Length==2 && first.Lobby.Members.Length==1,"discovered room address connected to wrong host");
 first.Dispose();deadline=Now()+6;while(discovery.Rooms.Any(r=>r.Lobby.Id==first.Lobby.Id) && Now()<deadline){second.Pump(Now());guest.Pump(Now());discovery.Pump(Now());Thread.Sleep(5);}
 Assert(discovery.Rooms.Count==1 && discovery.Rooms[0].Lobby.Id==second.Lobby.Id,"closed room retained in discovery");
});
Test("automatic hosting survives occupied preferred port and assigns distinct usable endpoints",()=> {
 using var blocker=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any,RoomAddress.DefaultPort);blocker.Start();
 using var first=NetworkRoom.Host(Options(NetworkRoute.LAN));using var second=NetworkRoom.Host(Options(NetworkRoute.LAN));
 Assert(first.Port>0 && first.Port!=RoomAddress.DefaultPort && second.Port>0 && first.Port!=second.Port,"automatic host did not choose free ports");
 using var guest=Client(second);Until(()=>guest.Connected,first,second,guest);
 Assert(second.Lobby.Members.Length==2 && first.Lobby.Members.Length==1,"fallback listener unusable");
});
Test("LAN IP-only join resolves occupied-port fallback through directed discovery",()=> {
 using var blocker=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any,RoomAddress.DefaultPort);blocker.Start();
 using var host=NetworkRoom.Host(Options(NetworkRoute.LAN));using var guest=NetworkRoom.Join("127.0.0.1",Options(NetworkRoute.LAN,Team.Amber));
 Until(()=>guest.Connected,host,guest);
 Assert(guest.Port==host.Port && host.Port!=RoomAddress.DefaultPort && guest.Lobby.Id==host.Lobby.Id,"host-only input lost the actual port");
});
Test("IP-only lookup refuses ambiguous hosts and invitations select the intended room",()=> {
 using var first=NetworkRoom.Host(Options(NetworkRoute.LAN));using var second=NetworkRoom.Host(Options(NetworkRoute.LAN));
 using var ambiguous=NetworkRoom.Join("localhost",Options(NetworkRoute.LAN,Team.Amber));string reason="";ambiguous.Failed+=s=>reason=s;
 Until(()=>reason.Length>0,first,second,ambiguous);Assert(reason.Contains("多个房间") && !ambiguous.Connected,"ambiguous IP silently joined the wrong room");
 using var selected=NetworkRoom.Join(second.Invitation("127.0.0.1"),Options(NetworkRoute.LAN,Team.Amber));Until(()=>selected.Connected,first,second,selected);
 Assert(selected.Lobby.Id==second.Lobby.Id && first.Lobby.Members.Length==1,"invitation selected wrong same-machine room");
});
Test("internet invitation carries an automatically selected port and still requires room password",()=> {
 using var blocker=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any,RoomAddress.DefaultPort);blocker.Start();
 using var host=NetworkRoom.Host(Options(password:"secret"));string invite=host.Invitation("127.0.0.1");
 Assert(invite.StartsWith("SS1-") && !invite.Contains(":"),"invitation exposed a manual endpoint");
 NetworkRoom.ParseAddress(invite,out string ip,out int port);Assert(ip=="127.0.0.1" && port==host.Port,"invitation lost endpoint");
 string unicodeHost=new string('测',250)+".cn";NetworkRoom.ParseAddress(host.Invitation(unicodeHost),out string roundTrip,out _);
 Assert(roundTrip==unicodeHost,"maximum UTF-8 host invitation could not round trip");
 using var guest=NetworkRoom.Join(invite,Options(team:Team.Amber,password:"secret",account:"b"));Until(()=>guest.Connected,host,guest);
 Assert(guest.Port==host.Port && guest.Lobby.RequiresPassword,"automatic internet invitation bypassed password or port");
});
Test("malformed, cross-route and stale invitations are rejected",()=> {
 foreach(string value in new[]{"SS1-", "SS1-%%%%", "SS1-a", "SS1-"+new string('a',1401)})
 { bool rejected=false;try{NetworkRoom.ParseAddress(value,out _,out _);}catch(ArgumentException){rejected=true;}Assert(rejected,"malformed invite accepted"); }
 using var host=NetworkRoom.Host(Options(NetworkRoute.LAN));bool wrongRoute=false;
 try{using var wrong=NetworkRoom.Join(host.Invitation("127.0.0.1"),Options());}catch(ArgumentException){wrongRoute=true;}
 Assert(wrongRoute,"invitation route ignored");
 string stale=RoomAddress.Invite("127.0.0.1",host.Port,NetworkRoute.LAN,Guid.NewGuid().ToString("N"));
 using var guest=NetworkRoom.Join(stale,Options(NetworkRoute.LAN,Team.Amber));string reason="";guest.Failed+=s=>reason=s;
 Until(()=>reason.Length>0,host,guest);Assert(reason.Contains("失效") && !guest.Connected,"reused port admitted stale invitation");
});
Test("every room receives a mixed six-character uppercase alphanumeric code",()=> {
 for(int i=0;i<1000;i++){string code=RoomCode.Generate();Assert(RoomCode.IsValid(code)&&code.Any(char.IsDigit)&&code.Any(char.IsLetter),"invalid generated room code");}
 Assert(RoomCode.TryNormalize(" ab12cd ",out var normalized)&&normalized=="AB12CD","lowercase paste was not normalized");
 foreach(string code in new[]{"", "ABC12", "ABC1234", "AB-12C", "ＡB12CD", "AB12 C"})Assert(!RoomCode.TryNormalize(code,out _),"invalid code format accepted");
 using var host=NetworkRoom.Host(Options(NetworkRoute.LAN));using var guest=Client(host);Until(()=>guest.Connected,host,guest);
 Assert(RoomCode.IsValid(host.Lobby.Code)&&host.Lobby.Code==guest.Lobby.Code,"room code was not synchronized");
 host.SetRoomCode("ZX90AB");Until(()=>guest.Lobby.Code=="ZX90AB",host,guest);
 bool refused=false;try{guest.SetRoomCode("AB12CD");}catch(ArgumentException){refused=true;}Assert(refused,"guest changed room code");
});
Test("LAN discovery includes the current room code",()=> {
 using var host=NetworkRoom.Host(Options(NetworkRoute.LAN));using var discovery=new LanDiscovery();
 double deadline=Now()+5;while(!discovery.Rooms.Any(r=>r.Lobby.Id==host.Lobby.Id) && Now()<deadline){host.Pump(Now());discovery.Pump(Now());Thread.Sleep(5);}
 Assert(discovery.Rooms.Single(r=>r.Lobby.Id==host.Lobby.Id).Lobby.Code==host.Lobby.Code,"LAN broadcast lost room code");
});
Test("room enters replicated draft after ready and cannot join or change teams during selection",()=> {
 using var host=NetworkRoom.Host(Options(NetworkRoute.LAN));using var client=Client(host);Until(()=>client.Connected,host,client);
 bool rejected=false;try{host.BeginDraft(new MatchRules());}catch(InvalidOperationException){rejected=true;}Assert(rejected,"unready guest admitted draft");
 client.SetReady(true);Until(()=>host.CanStart,host,client);host.BeginDraft(new MatchRules());Until(()=>client.Draft!=null,host,client);
 Assert(host.Lobby.Drafting && client.Lobby.Drafting && !host.Running && !host.CanStart && host.Draft.CurrentSlot==0 && client.Draft.CurrentSlot==0,"draft was not replicated");
 client.SetChoice(CharacterId.Thor,Team.Cyan);client.SetReady(false);for(int i=0;i<4;i++){host.Pump(Now());client.Pump(Now());Thread.Sleep(5);}Assert(client.LocalSlot==3 && host.Lobby.Members.Last().Ready,"draft allowed lobby changes");
 using var late=Client(host);string reason="";late.Failed+=x=>reason=x;Until(()=>reason.Length>0,host,client,late);Assert(!late.Connected,"late join entered draft");
});
Test("only current player's lock is accepted and final lineup and positions start both peers",()=> {
 using var host=NetworkRoom.Host(Options(NetworkRoute.LAN));using var client=Client(host);Until(()=>client.Connected,host,client);client.SetReady(true);Until(()=>host.CanStart,host,client);
 NetworkStart local=null,remote=null;host.Started+=x=>local=x;client.Started+=x=>remote=x;host.BeginDraft(new MatchRules());Until(()=>client.Draft!=null,host,client);
 client.LockDraft(CharacterId.Thor,client.Draft.Picks[3].Position);Until(()=>client.Status.Contains("等待"),host,client);Assert(host.Draft.TurnIndex==0,"wrong peer advanced draft");
 var hostPosition=new Vec2(-14,2);Assert(host.LockDraft(CharacterId.Thor,hostPosition),"host pick rejected");Until(()=>client.Draft.CurrentSlot==3,host,client);
 client.LockDraft(CharacterId.Thor,new Vec2(-10,0));Until(()=>client.Status.Contains("半场"),host,client);Assert(host.Draft.CurrentSlot==3,"enemy-half client position accepted");
 var clientPosition=new Vec2(14,-2);client.LockDraft(CharacterId.Marine,clientPosition);Until(()=>remote!=null,host,client);
 Assert(local!=null && local.Lineup[0]==CharacterId.Thor && remote.Lineup[3]==CharacterId.Marine && remote.Positions.Length==6,"draft lineup or positions lost on start");
 Assert(Vec2.Distance(local.Positions[0],hostPosition)<.001f && Vec2.Distance(remote.Positions[3],clientPosition)<.001f,"selected placement was not transmitted");MatchDraft.ValidateOpening(remote.Rules,remote.Positions);
});
Test("draft cancellation returns both players to lobby and rematch starts a new selection epoch",()=> {
 using var host=NetworkRoom.Host(Options(NetworkRoute.LAN));using var client=Client(host);Until(()=>client.Connected,host,client);client.SetReady(true);Until(()=>host.CanStart,host,client);host.BeginDraft(new MatchRules());Until(()=>client.Draft!=null,host,client);
 int old=client.Draft.Epoch;bool returned=false;client.ReturnedToLobby+=()=>returned=true;host.ReturnToLobby();Until(()=>returned,host,client);Assert(client.Draft==null && !client.Lobby.Drafting,"canceled selection stayed visible");
 client.SetReady(true);Until(()=>host.CanStart,host,client);host.BeginDraft(new MatchRules());Until(()=>client.Draft?.Epoch>old,host,client);Assert(client.Draft.TurnIndex==0 && client.Draft.Picks.All(x=>!x.Locked),"new round retained locked picks");
});
Test("draft snapshot rejects impossible locked order and nonfinite placement",()=> {
 var rules=new MatchRules();var draft=new MatchDraft(rules,new bool[6]);draft.Picks[3].Locked=true;bool rejected=false;
 try{NetworkCodec.Read(NetworkCodec.Pack(w=>NetworkRoom.WriteDraft(w,draft)),r=>NetworkRoom.ReadDraft(r));}catch(ArgumentException){rejected=true;}Assert(rejected,"malformed pick order accepted");
 draft.Picks[3].Locked=false;draft.Picks[0].Position=new Vec2(float.NaN,0);rejected=false;
 try{NetworkCodec.Read(NetworkCodec.Pack(w=>NetworkRoom.WriteDraft(w,draft)),r=>NetworkRoom.ReadDraft(r));}catch(ArgumentException){rejected=true;}Assert(rejected,"nonfinite placement accepted");
});
var report=args.Length>0?args[0]:"reports/network-017-transport";Directory.CreateDirectory(report);File.WriteAllText(Path.Combine(report,"network-tests.json"),JsonSerializer.Serialize(new{passed=failed==0,total=results.Count,failed,results},new JsonSerializerOptions{WriteIndented=true}));return failed==0?0:1;
