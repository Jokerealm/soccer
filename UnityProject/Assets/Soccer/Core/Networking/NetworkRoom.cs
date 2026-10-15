using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Soccer.Core.Networking
{
    public enum NetworkRoute { Internet, LAN, Steam }
    public sealed class RoomOptions
    {
        public NetworkRoute Route;
        public string Name = "足球房间", PlayerName = "球员", Password = "";
        public bool UsePassword,AutoAssignTeam;
        // Zero lets the program choose a port; explicit values are reserved for developer tools.
        public int Port;
        public CharacterId Character = CharacterId.Marine;
        public Team Team = Team.Cyan;
        public DebugAccountSession Account;
        public string PlatformAccountId="";
        public void Validate()
        {
            Name = Clean(Name, 32); PlayerName = PlayerNickname.Normalize(PlayerName);
            if (Port < 0 || Port > 65535 || !Enum.IsDefined(typeof(NetworkRoute), Route) || !Enum.IsDefined(typeof(Team), Team) || !ValidCharacter(Character)) throw new ArgumentException("房间设置无效");
            if (Route == NetworkRoute.Internet && (Account == null || !DebugAccounts.IsKnown(Account.Id))) throw new ArgumentException("请先登录调试帐号 a 或 b");
            if(Route==NetworkRoute.Steam && !SteamAccount.IsValid(PlatformAccountId))throw new ArgumentException("请先登录 Steam");
            if (UsePassword && (Route == NetworkRoute.LAN || string.IsNullOrWhiteSpace(Password) || Password.Length > 64)) throw new ArgumentException("开启密码后请输入 1—64 个字符的密码");
            if (!UsePassword) Password = "";
        }
        internal static bool ValidCharacter(CharacterId id) => id == CharacterId.Marine || id == CharacterId.Thor;
        internal static string Clean(string value, int max)
        {
            value = (value ?? "").Trim();
            if (value.Length < 1 || value.Length > max || value.Any(char.IsControl)) throw new ArgumentException("名称不能为空、过长或包含控制字符");
            return value;
        }
    }
    public sealed class RoomMember
    {
        public int Slot;
        public string Name;
        public string AccountId = "";
        public CharacterId Character;
        public bool Ready, Host;
        public Team Team => Slot < 3 ? Team.Cyan : Team.Amber;
    }
    public sealed class RoomLobby
    {
        public string Id;
        public string Code;
        public string Name;
        public NetworkRoute Route;
        public bool RequiresPassword, Running, Drafting;
        public int Port;
        public RoomMember[] Members = new RoomMember[0];
        public int[] Bots = new int[0];
        public float CountdownRemaining;
        public BotDifficulty BotDifficulty = BotDifficulty.Player;
    }
    public sealed class NetworkStart
    {
        public MatchRules Rules;
        public CharacterId[] Lineup;
        public Vec2[] Positions;
    }
    public static class RoomPassword
    {
        public static byte[] Random(int size) { var result = new byte[size]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(result); return result; }
        public static byte[] Derive(string password, byte[] salt)
        {
            if (password == null || password.Length > 64 || salt == null || salt.Length != 16) throw new ArgumentException("密码参数无效");
#pragma warning disable SYSLIB0060
            using (var pbkdf = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256)) return pbkdf.GetBytes(32);
#pragma warning restore SYSLIB0060
        }
        public static byte[] Proof(byte[] key, byte[] nonce, string identity)
        { using (var mac = new HMACSHA256(key)) return mac.ComputeHash(nonce.Concat(Encoding.UTF8.GetBytes(identity)).ToArray()); }
        public static bool Equal(byte[] a, byte[] b)
        { if (a == null || b == null || a.Length != b.Length) return false; int difference = 0; for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i]; return difference == 0; }
    }
    /// <summary>All room state and callbacks run on Pump's caller; socket workers only exchange bounded packets.</summary>
    public sealed partial class NetworkRoom : IDisposable
    {
        private sealed class Guest
        {
            public IRoomPeer Peer;
            public RoomMember Member;
            public byte[] Nonce;
            public bool Challenged, Joined;
            public double Began, LastSeen;
            public readonly RemoteCommandBuffer Input = new RemoteCommandBuffer();
        }
        public bool IsHost { get; private set; }
        public int LocalSlot { get; private set; } = -1;
        public int Port { get; private set; }
        public bool Connected { get; private set; }
        public bool Running => Lobby?.Running == true;
        public RoomLobby Lobby { get; private set; }
        public string Status { get; private set; } = "连接中…";
        public event Action Changed;
        public event Action<NetworkStart> Started;
        public event Action<byte[]> SnapshotReceived;
        public event Action ReturnedToLobby;
        public event Action<int> PlayerLeft;
        public event Action<string> Failed;
        private RoomOptions options;
        private string expectedRoomId;
        private TcpListener listener;
        private LanAdvertiser advertiser;
        private IRoomPeer server;
        private readonly List<Guest> guests = new List<Guest>();
        private readonly ConcurrentQueue<TcpClient> accepted = new ConcurrentQueue<TcpClient>();
        private readonly ConcurrentQueue<Action> completed = new ConcurrentQueue<Action>();
        private byte[] passwordKey, salt;
        private long inputSequence;
        private int matchEpoch;
        private double clock, lastPing, lastServer;
        private volatile bool disposed;
        private bool failed;
        private readonly List<MatchEvent> eventBatch = new List<MatchEvent>();
        public string MatchId => matchEpoch>0 ? Lobby.Id+":"+matchEpoch : "";
        public bool CanStart => IsHost && Connected && !Running && !Lobby.Drafting && !CountingDown && EveryoneReady;
        public static NetworkRoom Host(RoomOptions value)
        {
            if(value.Route==NetworkRoute.Steam)throw new ArgumentException("Steam房间需要Steam传输");
            value.Validate(); var room = new NetworkRoom { IsHost = true, options = value };
            try
            {
                room.salt = RoomPassword.Random(16);
                room.passwordKey = value.UsePassword ? RoomPassword.Derive(value.Password, room.salt) : null;
                value.Password = ""; // Keep only a derived verifier in the host's live room.
                room.listener = new TcpListener(IPAddress.Any, value.Port == 0 ? RoomAddress.DefaultPort : value.Port);
                try { room.listener.Start(8); }
                catch (SocketException e) when (value.Port == 0 && e.SocketErrorCode == SocketError.AddressAlreadyInUse)
                { room.listener.Stop(); room.listener = new TcpListener(IPAddress.Any, 0); room.listener.Start(8); }
                room.Port = ((IPEndPoint)room.listener.LocalEndpoint).Port;
                room.LocalSlot = value.Team == Team.Cyan ? 0 : 3;
                room.Lobby = new RoomLobby { Id = Guid.NewGuid().ToString("N"), Code = RoomCode.Generate(), Name = value.Name, Route = value.Route, Port = room.Port, RequiresPassword = value.UsePassword,
                    Members = new[] { new RoomMember { Slot = room.LocalSlot, Name = value.PlayerName, AccountId = value.Account?.Id ?? "", Character = value.Character, Ready = false, Host = true } } };
                room.Connected = true; room.Status = "房间已创建，等待球员加入";
                if (value.Route == NetworkRoute.LAN)
                    try { room.advertiser = new LanAdvertiser(); }
                    catch (SocketException) { room.Status = "房间已创建；自动发现不可用，请使用房主地址加入"; }
                room.BroadcastLobby(); Task.Run(room.AcceptLoop); return room;
            }
            catch { room.Dispose(); throw; }
        }
        public static NetworkRoom Join(string address, RoomOptions value)
        {
            if(value.Route==NetworkRoute.Steam)throw new ArgumentException("Steam房间需要Steam传输");
            string password = value.Password ?? "";
            if (password.Length > 64) throw new ArgumentException("密码不能超过 64 个字符");
            value.UsePassword = false; value.Validate(); value.Password = password;
            var endpoint = RoomAddress.Parse(address);
            if (endpoint.Route.HasValue && endpoint.Route.Value != value.Route) throw new ArgumentException("请在对应的局域网或互联网方式中使用此邀请");
            var room = new NetworkRoom { options = value, Port = endpoint.Port, expectedRoomId = endpoint.RoomId };
            Task.Run(async () =>
            {
                var socket = new TcpClient();
                try
                {
                    if (value.Route == NetworkRoute.LAN && !endpoint.HasPort)
                    {
                        var found = await LanDiscovery.FindAt(endpoint.Host);
                        if (found.Length > 1) throw new ArgumentException("这台电脑有多个房间，请从房间列表选择，或粘贴房间邀请");
                        if (found.Length == 1) { endpoint.Port = found[0].Port; room.expectedRoomId = found[0].Id; }
                    }
                    if (room.disposed) { socket.Close(); return; }
                    var connect = socket.ConnectAsync(endpoint.Host, endpoint.Port);
                    if (await Task.WhenAny(connect, Task.Delay(6000)) != connect) throw new TimeoutException();
                    await connect;
                    if (room.disposed) { socket.Close(); return; }
                    room.completed.Enqueue(() =>
                    {
                        if (room.disposed) { socket.Close(); return; }
                        room.Port = endpoint.Port; room.server = new RoomPeer(socket); room.lastServer = room.clock;
                        room.SendHello();
                    });
                }
                catch (Exception e) { socket.Close(); room.completed.Enqueue(() => room.Fail(e is ArgumentException ? e.Message : "无法加入房间，请确认房主在线，或重新复制房间邀请")); }
            });
            return room;
        }
        public static void ParseAddress(string address, out string host, out int port)
        {
            var endpoint = RoomAddress.Parse(address); host = endpoint.Host; port = endpoint.Port;
        }
        public void SetRoomCode(string code)
        {
            if (!IsHost || !Connected || !RoomCode.IsValid(code)) throw new ArgumentException("房间号无效");
            Lobby.Code = code; BroadcastLobby();
        }
        public static string CreateInvitation(string host, RoomLobby lobby)
        { if (lobby == null) throw new ArgumentException("房间信息无效"); return RoomAddress.Invite(host, lobby.Port, lobby.Route, lobby.Id); }
        public string Invitation(string host)
        { if (!Connected || Lobby == null) throw new InvalidOperationException("房间尚未连接"); return RoomAddress.Invite(host, Port, Lobby.Route, Lobby.Id); }
        public static string[] LocalAddresses()
        {
            try { return NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()).Distinct().ToArray(); }
            catch { return new string[0]; }
        }
        private async Task AcceptLoop()
        {
            try
            {
                while (!disposed)
                {
                    var socket = await listener.AcceptTcpClientAsync();
                    if (disposed || accepted.Count >= 8) socket.Close(); else accepted.Enqueue(socket);
                }
            }
            catch (Exception) { if (!disposed) completed.Enqueue(() => Fail("房间监听已停止")); }
        }
        public void Pump(double now)
        {
            if (disposed || failed) return; double elapsed = Math.Max(0,now-clock); clock = now;
            advertiser?.Pump(now);
            while (completed.TryDequeue(out var action)) action();
            if (disposed || failed) return;
            if (IsHost)
            {
                while (accepted.TryDequeue(out var socket))
                {
                    var peer = new RoomPeer(socket);
                    if (guests.Count >= 8) { Reject(peer, "房间连接数已满，请稍后再试"); continue; }
                    guests.Add(new Guest { Peer = peer, Began = now, LastSeen = now });
                }
                foreach (var guest in guests.ToArray())
                {
                    guest.Peer.Pump();
                    if(guest.Began<0){guest.Began=now;guest.LastSeen=now;}
                    while (guest.Peer.TryRead(out var packet))
                    {
                        guest.LastSeen = now;
                        try { HostPacket(guest, packet); } catch (Exception) { Reject(guest.Peer, "网络消息无效或版本不一致"); }
                    }
                    if ((!guest.Joined && now - guest.Began > 10) || now - guest.LastSeen > 10) guest.Peer.Dispose();
                    if (!guest.Peer.Connected)
                    {
                        guests.Remove(guest);
                        if (guest.Joined)
                        { Lobby.Members = Lobby.Members.Where(m => m != guest.Member).ToArray();CancelCountdown(); Draft?.SetHuman(guest.Member.Slot,false); PlayerLeft?.Invoke(guest.Member.Slot); BroadcastLobby(); }
                    }
                }
            }
            else if (server != null)
            {
                server.Pump();
                if(lastServer<0)lastServer=now;
                while (server.TryRead(out var packet))
                { lastServer = now; try { ClientPacket(packet); } catch (Exception) { Fail("房主消息无效或版本不一致，已停止连接"); return; } }
                if (!server.Connected || now - lastServer > 10) { Fail("与房主的连接已断开，请重新加入房间"); return; }
            }
            PumpCountdown((float)Math.Min(elapsed,1));PumpDraft((float)Math.Min(elapsed,1));
            if (now - lastPing > 1)
            {
                lastPing = now;
                if (IsHost) foreach (var guest in guests) guest.Peer.Send(PacketKind.Ping, new byte[0]);
                else server?.Send(PacketKind.Ping, new byte[0]);
            }
        }
        private static void Reject(IRoomPeer peer, string reason) => peer.Send(PacketKind.Reject, NetworkCodec.Pack(w => NetworkCodec.Text(w, reason)));
        private string Identity(RoomMember member) => NetworkCodec.Version + "|" + member.AccountId + "|" + member.Name + "|" + (int)member.Character + "|" + (int)member.Team;
        private void HostPacket(Guest guest, WirePacket packet)
        {
            if (packet.Kind == PacketKind.Ping) { guest.Peer.Send(PacketKind.Pong, new byte[0]); return; }
            if (packet.Kind == PacketKind.Pong) return;
            if (packet.Kind == PacketKind.Hello && !guest.Challenged && !guest.Joined)
            {
                if (Running || Lobby.Drafting || CountingDown || OccupiedSlots >= 6) { Reject(guest.Peer, Running || Lobby.Drafting || CountingDown ? "比赛已经开始，请等待下一局" : "房间已满"); return; }
                var member = new RoomMember(); Team team = Team.Cyan; NetworkRoute route = NetworkRoute.Internet;
                NetworkCodec.Read(packet.Body, r =>
                { if (NetworkCodec.Text(r) != NetworkCodec.Version) throw new InvalidDataException(); member.Name = PlayerNickname.Normalize(NetworkCodec.Text(r, PlayerNickname.MaxWireLength)); member.Character = (CharacterId)r.ReadInt32(); team = (Team)r.ReadInt32(); route = (NetworkRoute)r.ReadInt32(); member.AccountId = NetworkCodec.Text(r, 32); });
                bool automatic=(int)team==-1;
                if (!RoomOptions.ValidCharacter(member.Character) || !automatic && !Enum.IsDefined(typeof(Team), team) || route != Lobby.Route) throw new InvalidDataException();
                if(route==NetworkRoute.Steam && (member.AccountId!=guest.Peer.AuthenticatedAccountId || !SteamAccount.IsValid(member.AccountId))){Reject(guest.Peer,"Steam身份与连接不一致");return;}
                if (route!=NetworkRoute.Steam && (route == NetworkRoute.Internet && !DebugAccounts.IsKnown(member.AccountId) || member.AccountId.Length > 0 && !DebugAccounts.IsKnown(member.AccountId)))
                { Reject(guest.Peer, "帐号验证失败，请使用调试帐号 a 或 b 登录"); return; }
                member.Slot = automatic ? AutomaticSlot() : FreeSlot(team); if (member.Slot < 0) { Reject(guest.Peer, automatic ? "房间已满" : "所选队伍已满，请换队后加入"); return; }
                guest.Member = member; guest.Nonce = RoomPassword.Random(32); guest.Challenged = true;
                guest.Peer.Send(PacketKind.Challenge, NetworkCodec.Pack(w => { w.Write(Lobby.RequiresPassword); w.Write(salt); w.Write(guest.Nonce); w.Write(member.Slot); })); return;
            }
            if (packet.Kind == PacketKind.Authenticate && guest.Challenged && !guest.Joined)
            {
                guest.Challenged = false;
                byte[] accountProof = null, roomProof = null;
                NetworkCodec.Read(packet.Body, r => { accountProof = ReadProof(r); roomProof = ReadProof(r); });
                if (Lobby.Route!=NetworkRoute.Steam && guest.Member.AccountId.Length > 0 && !DebugAccounts.Verify(guest.Member.AccountId, guest.Nonce, Identity(guest.Member), accountProof))
                { Reject(guest.Peer, "帐号验证失败，请重新登录"); return; }
                bool valid = !Lobby.RequiresPassword && roomProof.Length == 0 || Lobby.RequiresPassword && RoomPassword.Equal(roomProof, RoomPassword.Proof(passwordKey, guest.Nonce, Identity(guest.Member)));
                if (!valid) { Reject(guest.Peer, "房间密码不正确"); return; }
                if (Running || Lobby.Drafting || CountingDown || OccupiedSlots >= 6 || Lobby.Bots.Contains(guest.Member.Slot) || Lobby.Members.Any(m => m.Slot == guest.Member.Slot)) { Reject(guest.Peer, "该位置已被其他球员占用，请重新加入"); return; }
                if (Lobby.Route==NetworkRoute.Steam && Lobby.Members.Any(m=>m.AccountId==guest.Member.AccountId)) { Reject(guest.Peer,"该Steam帐号已在房间内"); return; }
                guest.Joined = true;CancelCountdown();foreach(var existing in Lobby.Members)existing.Ready=false; Lobby.Members = Lobby.Members.Concat(new[] { guest.Member }).OrderBy(m => m.Slot).ToArray(); BroadcastLobby(); return;
            }
            if (!guest.Joined) throw new InvalidDataException();
            if(packet.Kind==PacketKind.TeamSignal){ReceiveSignal(packet,guest.Member.Slot);return;}
            if(packet.Kind==PacketKind.Chat){ReceiveChat(packet,guest.Member.Slot);return;}
            if (packet.Kind == PacketKind.DraftLock) { if(Lobby.Drafting)ReceiveDraftLock(guest,packet);return; }
            if (Lobby.Drafting && (packet.Kind==PacketKind.Choice || packet.Kind==PacketKind.Ready || packet.Kind==PacketKind.Input)) return;
            // In-flight lobby buttons and inputs may arrive just after a phase transition.
            if (Running && (packet.Kind == PacketKind.Choice || packet.Kind == PacketKind.Ready) || !Running && packet.Kind == PacketKind.Input) return;
            if (packet.Kind == PacketKind.Choice && !Running)
                NetworkCodec.Read(packet.Body, r => { if (!ApplyChoice(guest.Member, (CharacterId)r.ReadInt32(), (Team)r.ReadInt32())) guest.Peer.Send(PacketKind.Notice, NetworkCodec.Pack(w => NetworkCodec.Text(w, "所选队伍已满"))); });
            else if (packet.Kind == PacketKind.Ready && !Running)
                NetworkCodec.Read(packet.Body, r => { guest.Member.Ready = r.ReadBoolean();if(!guest.Member.Ready)CancelCountdown(); BroadcastLobby(); });
            else if (packet.Kind == PacketKind.Input && Running)
                NetworkCodec.Read(packet.Body, r => { int epoch = r.ReadInt32(); long seq = r.ReadInt64(); var input = NetworkCodec.ReadFields<PlayerCommand>(r); if (epoch == matchEpoch && !guest.Input.Accept(seq, input, clock)) throw new InvalidDataException(); });
            else throw new InvalidDataException();
        }
        private void ClientPacket(WirePacket packet)
        {
            if(packet.Kind==PacketKind.Chat && Connected){ReceiveChat(packet);return;}
            if(packet.Kind==PacketKind.TeamSignal && Connected){ReceiveSignal(packet);return;}

            if (packet.Kind == PacketKind.Reject) { string reason = ""; NetworkCodec.Read(packet.Body, r => reason = NetworkCodec.Text(r)); Fail(reason); return; }
            if (packet.Kind == PacketKind.Ping) { server.Send(PacketKind.Pong, new byte[0]); return; }
            if (packet.Kind == PacketKind.Pong) return;
            if (packet.Kind == PacketKind.Notice && Connected)
            { NetworkCodec.Read(packet.Body, r => Status = NetworkCodec.Text(r)); Changed?.Invoke(); return; }
            if (packet.Kind == PacketKind.Challenge && !Connected)
            {
                bool locked = false; byte[] roomSalt = null, nonce = null;int assigned=-1;
                NetworkCodec.Read(packet.Body, r => { locked = r.ReadBoolean(); roomSalt = r.ReadBytes(16); nonce = r.ReadBytes(32); assigned=r.ReadInt32(); });
                if (roomSalt.Length != 16 || nonce.Length != 32) throw new InvalidDataException();
                if(assigned<0 || assigned>=6 || !options.AutoAssignTeam && (assigned<3 ? Team.Cyan : Team.Amber)!=options.Team)throw new InvalidDataException();
                options.Team=assigned<3 ? Team.Cyan : Team.Amber;
                byte[] proof = new byte[0];
                if (locked)
                {
                    if (string.IsNullOrEmpty(options.Password)) { Fail("此房间需要密码，请输入密码后重新加入"); return; }
                    var identity = NetworkCodec.Version + "|" + OwnAccountId + "|" + options.PlayerName + "|" + (int)options.Character + "|" + (int)options.Team;
                    proof = RoomPassword.Proof(RoomPassword.Derive(options.Password, roomSalt), nonce, identity);
                }
                var accountIdentity = NetworkCodec.Version + "|" + OwnAccountId + "|" + options.PlayerName + "|" + (int)options.Character + "|" + (int)options.Team;
                var accountProof = options.Route==NetworkRoute.Steam ? new byte[0] : options.Account?.Proof(nonce, accountIdentity) ?? new byte[0];
                options.Password = "";
                server.Send(PacketKind.Authenticate, NetworkCodec.Pack(w => { w.Write((byte)accountProof.Length); w.Write(accountProof); w.Write((byte)proof.Length); w.Write(proof); })); return;
            }
            if (packet.Kind == PacketKind.Lobby)
            {
                bool wasRunning = Running, wasDrafting=Lobby?.Drafting==true;
                NetworkCodec.Read(packet.Body, r => { LocalSlot = r.ReadInt32(); Lobby = ReadLobby(r); });
                if (!Lobby.Members.Any(m => m.Slot == LocalSlot)) throw new InvalidDataException();
                if(Lobby.Route==NetworkRoute.Steam && (Lobby.Members.Single(m=>m.Host).AccountId!=server.AuthenticatedAccountId || Lobby.Members.Single(m=>m.Slot==LocalSlot).AccountId!=OwnAccountId))throw new InvalidDataException("Steam身份无效");
                if (expectedRoomId != null && Lobby.Id != expectedRoomId) { Fail("这个房间邀请已失效，请让房主重新复制邀请"); return; }
                if(!Lobby.Drafting)Draft=null;
                Connected = true; Status = "已加入房间"; Changed?.Invoke(); if ((wasRunning || wasDrafting) && !Running && !Lobby.Drafting) ReturnedToLobby?.Invoke(); return;
            }
            if (packet.Kind == PacketKind.Draft && Connected) { ReceiveDraft(packet);return; }
            if (packet.Kind == PacketKind.Start && Connected)
            {
                var start = new NetworkStart();
                NetworkCodec.Read(packet.Body, r => { matchEpoch = r.ReadInt32(); if (matchEpoch <= 0) throw new InvalidDataException(); start.Rules = NetworkCodec.ReadFields<MatchRules>(r); start.Rules.Validate(); start.Lineup = new CharacterId[6]; for (int i = 0; i < 6; i++) { start.Lineup[i] = (CharacterId)r.ReadInt32(); if (!RoomOptions.ValidCharacter(start.Lineup[i])) throw new InvalidDataException(); }
                    if(r.ReadBoolean()) { start.Positions=new Vec2[6];for(int i=0;i<6;i++)start.Positions[i]=new Vec2(r.ReadSingle(),r.ReadSingle());MatchDraft.ValidateOpening(start.Rules,start.Positions); } });
                Lobby.Running = true; Lobby.Drafting=false; Draft=null; ResetSignals(start.Rules);Started?.Invoke(start); return;
            }
            if (packet.Kind == PacketKind.Snapshot && Running) { SnapshotReceived?.Invoke(packet.Body); return; }
            throw new InvalidDataException();
        }
        private int FreeSlot(Team team)
        { int start = team == Team.Cyan ? 0 : 3; for (int i = start; i < start + 3; i++) if (!Lobby.Members.Any(m => m.Slot == i) && !Lobby.Bots.Contains(i) && !guests.Any(g => g.Challenged && !g.Joined && g.Peer.Connected && g.Member.Slot == i)) return i; return -1; }
        private int AutomaticSlot()
        {
            int cyan=FreeSlot(Team.Cyan),amber=FreeSlot(Team.Amber);if(cyan<0)return amber;if(amber<0)return cyan;
            int cyanCount=Lobby.Members.Count(m=>m.Team==Team.Cyan)+Lobby.Bots.Count(s=>s<3)+guests.Count(g=>g.Challenged && !g.Joined && g.Peer.Connected && g.Member.Team==Team.Cyan);
            int amberCount=Lobby.Members.Count(m=>m.Team==Team.Amber)+Lobby.Bots.Count(s=>s>=3)+guests.Count(g=>g.Challenged && !g.Joined && g.Peer.Connected && g.Member.Team==Team.Amber);
            return cyanCount<=amberCount ? cyan : amber;
        }
        private bool ApplyChoice(RoomMember member, CharacterId character, Team team)
        {
            if (!RoomOptions.ValidCharacter(character) || !Enum.IsDefined(typeof(Team), team)) throw new InvalidDataException();
            if (member.Team != team) { int slot = FreeSlot(team); if (slot < 0) { Status = "所选队伍已满"; Changed?.Invoke(); return false; } member.Slot = slot; }
            CancelCountdown();member.Character = character; foreach (var m in Lobby.Members) m.Ready = false;
            if (member.Host) LocalSlot = member.Slot; Status = "角色与队伍已更新，等待球员准备"; BroadcastLobby(); return true;
        }
        public void SetChoice(CharacterId character, Team team)
        {
            if (!Connected || Running || Lobby.Drafting) return;
            if (IsHost) ApplyChoice(Lobby.Members.Single(m => m.Host), character, team);
            else server.Send(PacketKind.Choice, NetworkCodec.Pack(w => { w.Write((int)character); w.Write((int)team); }));
        }
        public void SetReady(bool ready)
        {if(!Connected || Running || Lobby.Drafting)return;if(IsHost){Lobby.Members.Single(m=>m.Host).Ready=ready;if(!ready)CancelCountdown();BroadcastLobby();}else server.Send(PacketKind.Ready, NetworkCodec.Pack(w => w.Write(ready)));}
        public void StartMatch(MatchRules rules)
        {
            rules.BotDifficulty=Lobby.BotDifficulty;
            if (!CanStart) throw new InvalidOperationException("等待所有球员准备后才能开始");
            rules.Validate(); var lineup = new[] { CharacterId.Marine, CharacterId.Thor, CharacterId.Thor, CharacterId.Marine, CharacterId.Thor, CharacterId.Thor };
            foreach (var member in Lobby.Members) lineup[member.Slot] = member.Character;
            LaunchMatch(new NetworkStart {Rules=rules,Lineup=lineup});
        }
        private void LaunchMatch(NetworkStart start)
        {
            ResetSignals(start.Rules);
            CancelCountdown();Lobby.Running=true;Lobby.Drafting=false;Draft=null;matchEpoch++;eventBatch.Clear();
            foreach(var guest in guests) guest.Input.Reset();BroadcastLobby();
            var bytes=NetworkCodec.Pack(w=> { w.Write(matchEpoch);NetworkCodec.WriteFields(w,start.Rules);foreach(var character in start.Lineup)w.Write((int)character);
                w.Write(start.Positions!=null);if(start.Positions!=null)foreach(var point in start.Positions){w.Write(point.X);w.Write(point.Y);} });
            foreach(var guest in guests.Where(g=>g.Joined))guest.Peer.Send(PacketKind.Start,bytes);
            Started?.Invoke(start);
        }

        public bool IsHumanSlot(int slot) => Connected && Lobby.Members.Any(m => m.Slot == slot);
        public PlayerCommand ReadInput(int slot) => guests.FirstOrDefault(g => g.Joined && g.Member.Slot == slot)?.Input.Read(clock) ?? default;
        public void SendInput(PlayerCommand input)
        { if (!IsHost && Connected && Running && NetworkCodec.ValidCommand(input)) server.Send(PacketKind.Input, NetworkCodec.Pack(w => { w.Write(matchEpoch); w.Write(++inputSequence); NetworkCodec.WriteFields(w, input); })); }
        public void Publish(MatchState state, bool force = false)
        {
            if (!IsHost || !Running) return; eventBatch.AddRange(state.Events);
            if (!force && state.Tick % 3 != 0 && state.Phase != MatchPhase.Finished) return;
            var bytes = NetworkCodec.Snapshot(state, eventBatch); eventBatch.Clear();
            foreach (var guest in guests.Where(g => g.Joined)) guest.Peer.Send(PacketKind.Snapshot, bytes);
        }
        public void ReturnToLobby()
        {
            if (!IsHost || !Connected) return;CancelCountdown(); Lobby.Running = false; Lobby.Drafting=false; Draft=null; eventBatch.Clear();
            foreach (var guest in guests) guest.Input.Reset();
            foreach (var member in Lobby.Members) member.Ready = false; BroadcastLobby(); ReturnedToLobby?.Invoke();
        }
        internal static void WriteLobby(BinaryWriter w, RoomLobby lobby)
        {
            NetworkCodec.Text(w, NetworkCodec.Version); NetworkCodec.Text(w, lobby.Id, 32); NetworkCodec.Text(w, lobby.Code, 6); NetworkCodec.Text(w, lobby.Name, 32); w.Write((int)lobby.Route); w.Write(lobby.RequiresPassword); w.Write(lobby.Running); w.Write(lobby.Drafting); w.Write(lobby.Port);
            w.Write(lobby.Members.Length); foreach (var m in lobby.Members) { w.Write(m.Slot); NetworkCodec.Text(w, m.Name, PlayerNickname.MaxWireLength); NetworkCodec.Text(w, m.AccountId, 32); w.Write((int)m.Character); w.Write(m.Ready); w.Write(m.Host); }
            w.Write(lobby.Bots.Length);foreach(int slot in lobby.Bots)w.Write(slot);w.Write((int)lobby.BotDifficulty);w.Write(lobby.CountdownRemaining);
        }
        internal static RoomLobby ReadLobby(BinaryReader r)
        {
            if (NetworkCodec.Text(r) != NetworkCodec.Version) throw new InvalidDataException();
            var lobby = new RoomLobby { Id = NetworkCodec.Text(r, 32), Code = NetworkCodec.Text(r, 6), Name = NetworkCodec.Text(r, 32), Route = (NetworkRoute)r.ReadInt32(), RequiresPassword = r.ReadBoolean(), Running = r.ReadBoolean(), Drafting = r.ReadBoolean(), Port = r.ReadInt32() };
            if (!Guid.TryParseExact(lobby.Id, "N", out _) || !RoomCode.IsValid(lobby.Code)) throw new InvalidDataException();
            if (!Enum.IsDefined(typeof(NetworkRoute), lobby.Route) || lobby.Port < 1 || lobby.Port > 65535) throw new InvalidDataException();
            int count = r.ReadInt32(); if (count < 1 || count > 6) throw new InvalidDataException(); var members = new List<RoomMember>();
            for (int i = 0; i < count; i++)
            {
                var m = new RoomMember { Slot = r.ReadInt32(), Name = NetworkCodec.Text(r, PlayerNickname.MaxWireLength), AccountId = NetworkCodec.Text(r, 32), Character = (CharacterId)r.ReadInt32(), Ready = r.ReadBoolean(), Host = r.ReadBoolean() };
                if(!PlayerNickname.IsValid(m.Name))throw new InvalidDataException();
                if(lobby.Route==NetworkRoute.Steam ? !SteamAccount.IsValid(m.AccountId) : m.AccountId.Length > 0 && !DebugAccounts.IsKnown(m.AccountId) || lobby.Route == NetworkRoute.Internet && !DebugAccounts.IsKnown(m.AccountId))throw new InvalidDataException();
                if (m.Slot < 0 || m.Slot >= 6 || !RoomOptions.ValidCharacter(m.Character) || members.Any(a => a.Slot == m.Slot)) throw new InvalidDataException(); members.Add(m);
            }
            if (members.Count(m => m.Host) != 1) throw new InvalidDataException(); lobby.Members = members.ToArray();
            int bots=r.ReadInt32();if(bots<0 || bots+count>6)throw new InvalidDataException();lobby.Bots=new int[bots];
            for(int i=0;i<bots;i++){int slot=r.ReadInt32();if(slot<0 || slot>=6 || members.Any(m=>m.Slot==slot) || lobby.Bots.Take(i).Contains(slot))throw new InvalidDataException();lobby.Bots[i]=slot;}
            lobby.BotDifficulty=(BotDifficulty)r.ReadInt32();if(!Enum.IsDefined(typeof(BotDifficulty),lobby.BotDifficulty))throw new InvalidDataException();
            lobby.CountdownRemaining=r.ReadSingle();if(!Geometry.IsFinite(lobby.CountdownRemaining) || lobby.CountdownRemaining<0 || lobby.CountdownRemaining>3 || lobby.CountdownRemaining>0 && (lobby.Running || lobby.Drafting || count+bots!=6 || members.Any(m=>!m.Ready)))throw new InvalidDataException();return lobby;
        }
        private void BroadcastLobby()
        {
            foreach (var guest in guests.Where(g => g.Joined)) guest.Peer.Send(PacketKind.Lobby, NetworkCodec.Pack(w => { w.Write(guest.Member.Slot); WriteLobby(w, Lobby); }));
            advertiser?.Update(NetworkCodec.Pack(w => WriteLobby(w, Lobby))); Changed?.Invoke();
        }
        private static byte[] ReadProof(BinaryReader reader)
        { int size = reader.ReadByte(); if (size != 0 && size != 32) throw new InvalidDataException(); var proof = reader.ReadBytes(size); if (proof.Length != size) throw new EndOfStreamException(); return proof; }
        private void Fail(string reason)
        { if (failed || disposed) return; failed = true; Connected = false; Status = reason; server?.Dispose(); Failed?.Invoke(reason); }
        public void Dispose()
        {
            if (disposed) return; disposed = true; Connected = false; listener?.Stop(); server?.Dispose(); advertiser?.Dispose();
            foreach (var guest in guests) guest.Peer.Dispose(); while (accepted.TryDequeue(out var socket)) socket.Close();
            if (passwordKey != null) Array.Clear(passwordKey, 0, passwordKey.Length); options = null;
        }
    }
}
