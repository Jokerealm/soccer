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
    public enum NetworkRoute { Internet, LAN }
    public sealed class RoomOptions
    {
        public NetworkRoute Route;
        public string Name = "足球房间", PlayerName = "球员", Password = "";
        public bool UsePassword;
        public int Port = 27860;
        public CharacterId Character = CharacterId.Marine;
        public Team Team = Team.Cyan;
        public DebugAccountSession Account;
        public void Validate()
        {
            Name = Clean(Name, 32); PlayerName = Clean(PlayerName, 24);
            if (Port < 0 || Port > 65535 || !Enum.IsDefined(typeof(NetworkRoute), Route) || !Enum.IsDefined(typeof(Team), Team) || !ValidCharacter(Character)) throw new ArgumentException("房间设置无效");
            if (Route == NetworkRoute.Internet && (Account == null || !DebugAccounts.IsKnown(Account.Id))) throw new ArgumentException("请先登录调试帐号 a 或 b");
            if (UsePassword && (Route != NetworkRoute.Internet || string.IsNullOrWhiteSpace(Password) || Password.Length > 64)) throw new ArgumentException("开启密码后请输入 1—64 个字符的密码");
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
        public string Name;
        public NetworkRoute Route;
        public bool RequiresPassword, Running;
        public int Port;
        public RoomMember[] Members = new RoomMember[0];
    }
    public sealed class NetworkStart
    {
        public MatchRules Rules;
        public CharacterId[] Lineup;
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
    public sealed class NetworkRoom : IDisposable
    {
        private sealed class Guest
        {
            public RoomPeer Peer;
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
        private TcpListener listener;
        private LanAdvertiser advertiser;
        private RoomPeer server;
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
        public bool CanStart => IsHost && Connected && !Running && Lobby.Members.All(m => m.Host || m.Ready);
        public static NetworkRoom Host(RoomOptions value)
        {
            value.Validate(); var room = new NetworkRoom { IsHost = true, options = value };
            try
            {
                room.salt = RoomPassword.Random(16);
                room.passwordKey = value.UsePassword ? RoomPassword.Derive(value.Password, room.salt) : null;
                value.Password = ""; // Keep only a derived verifier in the host's live room.
                room.listener = new TcpListener(IPAddress.Any, value.Port); room.listener.Start(8);
                room.Port = ((IPEndPoint)room.listener.LocalEndpoint).Port;
                room.LocalSlot = value.Team == Team.Cyan ? 0 : 3;
                room.Lobby = new RoomLobby { Id = Guid.NewGuid().ToString("N"), Name = value.Name, Route = value.Route, Port = room.Port, RequiresPassword = value.UsePassword,
                    Members = new[] { new RoomMember { Slot = room.LocalSlot, Name = value.PlayerName, AccountId = value.Account?.Id ?? "", Character = value.Character, Ready = true, Host = true } } };
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
            string password = value.Password ?? "";
            if (password.Length > 64) throw new ArgumentException("密码不能超过 64 个字符");
            value.UsePassword = false; value.Validate(); value.Password = password;
            ParseAddress(address, out string host, out int port);
            var room = new NetworkRoom { options = value, Port = port };
            Task.Run(async () =>
            {
                var socket = new TcpClient();
                try
                {
                    var connect = socket.ConnectAsync(host, port);
                    if (await Task.WhenAny(connect, Task.Delay(6000)) != connect) throw new TimeoutException();
                    await connect;
                    if (room.disposed) { socket.Close(); return; }
                    room.completed.Enqueue(() =>
                    {
                        if (room.disposed) { socket.Close(); return; }
                        room.server = new RoomPeer(socket); room.lastServer = room.clock;
                        room.server.Send(PacketKind.Hello, NetworkCodec.Pack(w => { NetworkCodec.Text(w, NetworkCodec.Version); NetworkCodec.Text(w, value.PlayerName, 24); w.Write((int)value.Character); w.Write((int)value.Team); w.Write((int)value.Route); NetworkCodec.Text(w, value.Account?.Id ?? "", 16); }));
                    });
                }
                catch (Exception) { socket.Close(); room.completed.Enqueue(() => room.Fail("连接失败：请检查房间地址、端口、防火墙和房主是否在线")); }
            });
            return room;
        }
        public static void ParseAddress(string address, out string host, out int port)
        {
            address = (address ?? "").Trim();
            if (address.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase)) address = address.Substring(6);
            int separator = address.LastIndexOf(':'); port = 27860; host = address;
            if (separator >= 0)
            { host = address.Substring(0, separator); if (!int.TryParse(address.Substring(separator + 1), out port)) throw new ArgumentException("请输入 IP 或域名:端口"); }
            if (host.Length < 1 || host.Length > 253 || host.Any(c => !(char.IsLetterOrDigit(c) || c == '.' || c == '-')) || port < 1 || port > 65535)
                throw new ArgumentException("请输入有效的 IPv4 地址或域名:端口");
        }
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
            if (disposed || failed) return; clock = now;
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
                        { Lobby.Members = Lobby.Members.Where(m => m != guest.Member).ToArray(); PlayerLeft?.Invoke(guest.Member.Slot); BroadcastLobby(); }
                    }
                }
            }
            else if (server != null)
            {
                while (server.TryRead(out var packet))
                { lastServer = now; try { ClientPacket(packet); } catch (Exception) { Fail("房主消息无效或版本不一致，已停止连接"); return; } }
                if (!server.Connected || now - lastServer > 10) { Fail("与房主的连接已断开，请重新加入房间"); return; }
            }
            if (now - lastPing > 1)
            {
                lastPing = now;
                if (IsHost) foreach (var guest in guests) guest.Peer.Send(PacketKind.Ping, new byte[0]);
                else server?.Send(PacketKind.Ping, new byte[0]);
            }
        }
        private static void Reject(RoomPeer peer, string reason) => peer.Send(PacketKind.Reject, NetworkCodec.Pack(w => NetworkCodec.Text(w, reason)));
        private string Identity(RoomMember member) => NetworkCodec.Version + "|" + member.AccountId + "|" + member.Name + "|" + (int)member.Character + "|" + (int)member.Team;
        private void HostPacket(Guest guest, WirePacket packet)
        {
            if (packet.Kind == PacketKind.Ping) { guest.Peer.Send(PacketKind.Pong, new byte[0]); return; }
            if (packet.Kind == PacketKind.Pong) return;
            if (packet.Kind == PacketKind.Hello && !guest.Challenged && !guest.Joined)
            {
                if (Running || Lobby.Members.Length >= 6) { Reject(guest.Peer, Running ? "比赛已经开始，请等待下一局" : "房间已满"); return; }
                var member = new RoomMember(); Team team = Team.Cyan; NetworkRoute route = NetworkRoute.Internet;
                NetworkCodec.Read(packet.Body, r =>
                { if (NetworkCodec.Text(r) != NetworkCodec.Version) throw new InvalidDataException(); member.Name = RoomOptions.Clean(NetworkCodec.Text(r, 24), 24); member.Character = (CharacterId)r.ReadInt32(); team = (Team)r.ReadInt32(); route = (NetworkRoute)r.ReadInt32(); member.AccountId = NetworkCodec.Text(r, 16); });
                if (!RoomOptions.ValidCharacter(member.Character) || !Enum.IsDefined(typeof(Team), team) || route != Lobby.Route) throw new InvalidDataException();
                if (route == NetworkRoute.Internet && !DebugAccounts.IsKnown(member.AccountId) || member.AccountId.Length > 0 && !DebugAccounts.IsKnown(member.AccountId))
                { Reject(guest.Peer, "帐号验证失败，请使用调试帐号 a 或 b 登录"); return; }
                member.Slot = FreeSlot(team); if (member.Slot < 0) { Reject(guest.Peer, "所选队伍已满，请换队后加入"); return; }
                guest.Member = member; guest.Nonce = RoomPassword.Random(32); guest.Challenged = true;
                guest.Peer.Send(PacketKind.Challenge, NetworkCodec.Pack(w => { w.Write(Lobby.RequiresPassword); w.Write(salt); w.Write(guest.Nonce); })); return;
            }
            if (packet.Kind == PacketKind.Authenticate && guest.Challenged && !guest.Joined)
            {
                guest.Challenged = false;
                byte[] accountProof = null, roomProof = null;
                NetworkCodec.Read(packet.Body, r => { accountProof = ReadProof(r); roomProof = ReadProof(r); });
                if (guest.Member.AccountId.Length > 0 && !DebugAccounts.Verify(guest.Member.AccountId, guest.Nonce, Identity(guest.Member), accountProof))
                { Reject(guest.Peer, "帐号验证失败，请重新登录"); return; }
                bool valid = !Lobby.RequiresPassword && roomProof.Length == 0 || Lobby.RequiresPassword && RoomPassword.Equal(roomProof, RoomPassword.Proof(passwordKey, guest.Nonce, Identity(guest.Member)));
                if (!valid) { Reject(guest.Peer, "房间密码不正确"); return; }
                if (Running || Lobby.Members.Length >= 6 || Lobby.Members.Any(m => m.Slot == guest.Member.Slot)) { Reject(guest.Peer, "该位置已被其他球员占用，请重新加入"); return; }
                guest.Joined = true; Lobby.Members = Lobby.Members.Concat(new[] { guest.Member }).OrderBy(m => m.Slot).ToArray(); BroadcastLobby(); return;
            }
            if (!guest.Joined) throw new InvalidDataException();
            // In-flight lobby buttons and inputs may arrive just after a phase transition.
            if (Running && (packet.Kind == PacketKind.Choice || packet.Kind == PacketKind.Ready) || !Running && packet.Kind == PacketKind.Input) return;
            if (packet.Kind == PacketKind.Choice && !Running)
                NetworkCodec.Read(packet.Body, r => { if (!ApplyChoice(guest.Member, (CharacterId)r.ReadInt32(), (Team)r.ReadInt32())) guest.Peer.Send(PacketKind.Notice, NetworkCodec.Pack(w => NetworkCodec.Text(w, "所选队伍已满"))); });
            else if (packet.Kind == PacketKind.Ready && !Running)
                NetworkCodec.Read(packet.Body, r => { guest.Member.Ready = r.ReadBoolean(); BroadcastLobby(); });
            else if (packet.Kind == PacketKind.Input && Running)
                NetworkCodec.Read(packet.Body, r => { int epoch = r.ReadInt32(); long seq = r.ReadInt64(); var input = NetworkCodec.ReadFields<PlayerCommand>(r); if (epoch == matchEpoch && !guest.Input.Accept(seq, input, clock)) throw new InvalidDataException(); });
            else throw new InvalidDataException();
        }
        private void ClientPacket(WirePacket packet)
        {
            if (packet.Kind == PacketKind.Reject) { string reason = ""; NetworkCodec.Read(packet.Body, r => reason = NetworkCodec.Text(r)); Fail(reason); return; }
            if (packet.Kind == PacketKind.Ping) { server.Send(PacketKind.Pong, new byte[0]); return; }
            if (packet.Kind == PacketKind.Pong) return;
            if (packet.Kind == PacketKind.Notice && Connected)
            { NetworkCodec.Read(packet.Body, r => Status = NetworkCodec.Text(r)); Changed?.Invoke(); return; }
            if (packet.Kind == PacketKind.Challenge && !Connected)
            {
                bool locked = false; byte[] roomSalt = null, nonce = null;
                NetworkCodec.Read(packet.Body, r => { locked = r.ReadBoolean(); roomSalt = r.ReadBytes(16); nonce = r.ReadBytes(32); });
                if (roomSalt.Length != 16 || nonce.Length != 32) throw new InvalidDataException();
                byte[] proof = new byte[0];
                if (locked)
                {
                    if (string.IsNullOrEmpty(options.Password)) { Fail("此房间需要密码，请输入密码后重新加入"); return; }
                    var identity = NetworkCodec.Version + "|" + (options.Account?.Id ?? "") + "|" + options.PlayerName + "|" + (int)options.Character + "|" + (int)options.Team;
                    proof = RoomPassword.Proof(RoomPassword.Derive(options.Password, roomSalt), nonce, identity);
                }
                var accountIdentity = NetworkCodec.Version + "|" + (options.Account?.Id ?? "") + "|" + options.PlayerName + "|" + (int)options.Character + "|" + (int)options.Team;
                var accountProof = options.Account?.Proof(nonce, accountIdentity) ?? new byte[0];
                options.Password = "";
                server.Send(PacketKind.Authenticate, NetworkCodec.Pack(w => { w.Write((byte)accountProof.Length); w.Write(accountProof); w.Write((byte)proof.Length); w.Write(proof); })); return;
            }
            if (packet.Kind == PacketKind.Lobby)
            {
                bool wasRunning = Running;
                NetworkCodec.Read(packet.Body, r => { LocalSlot = r.ReadInt32(); Lobby = ReadLobby(r); });
                if (!Lobby.Members.Any(m => m.Slot == LocalSlot)) throw new InvalidDataException();
                Connected = true; Status = "已加入房间"; Changed?.Invoke(); if (wasRunning && !Running) ReturnedToLobby?.Invoke(); return;
            }
            if (packet.Kind == PacketKind.Start && Connected)
            {
                var start = new NetworkStart();
                NetworkCodec.Read(packet.Body, r => { matchEpoch = r.ReadInt32(); if (matchEpoch <= 0) throw new InvalidDataException(); start.Rules = NetworkCodec.ReadFields<MatchRules>(r); start.Rules.Validate(); start.Lineup = new CharacterId[6]; for (int i = 0; i < 6; i++) { start.Lineup[i] = (CharacterId)r.ReadInt32(); if (!RoomOptions.ValidCharacter(start.Lineup[i])) throw new InvalidDataException(); } });
                Lobby.Running = true; Started?.Invoke(start); return;
            }
            if (packet.Kind == PacketKind.Snapshot && Running) { SnapshotReceived?.Invoke(packet.Body); return; }
            throw new InvalidDataException();
        }
        private int FreeSlot(Team team)
        { int start = team == Team.Cyan ? 0 : 3; for (int i = start; i < start + 3; i++) if (!Lobby.Members.Any(m => m.Slot == i) && !guests.Any(g => g.Challenged && !g.Joined && g.Peer.Connected && g.Member.Slot == i)) return i; return -1; }
        private bool ApplyChoice(RoomMember member, CharacterId character, Team team)
        {
            if (!RoomOptions.ValidCharacter(character) || !Enum.IsDefined(typeof(Team), team)) throw new InvalidDataException();
            if (member.Team != team) { int slot = FreeSlot(team); if (slot < 0) { Status = "所选队伍已满"; Changed?.Invoke(); return false; } member.Slot = slot; }
            member.Character = character; foreach (var m in Lobby.Members) m.Ready = m.Host;
            if (member.Host) LocalSlot = member.Slot; Status = "角色与队伍已更新，等待球员准备"; BroadcastLobby(); return true;
        }
        public void SetChoice(CharacterId character, Team team)
        {
            if (!Connected || Running) return;
            if (IsHost) ApplyChoice(Lobby.Members.Single(m => m.Host), character, team);
            else server.Send(PacketKind.Choice, NetworkCodec.Pack(w => { w.Write((int)character); w.Write((int)team); }));
        }
        public void SetReady(bool ready)
        { if (Connected && !Running && !IsHost) server.Send(PacketKind.Ready, NetworkCodec.Pack(w => w.Write(ready))); }
        public void StartMatch(MatchRules rules)
        {
            if (!CanStart) throw new InvalidOperationException("等待所有球员准备后才能开始");
            rules.Validate(); var lineup = new[] { CharacterId.Marine, CharacterId.Thor, CharacterId.Thor, CharacterId.Marine, CharacterId.Thor, CharacterId.Thor };
            foreach (var member in Lobby.Members) lineup[member.Slot] = member.Character;
            var start = new NetworkStart { Rules = rules, Lineup = lineup }; Lobby.Running = true; matchEpoch++; eventBatch.Clear();
            foreach (var guest in guests) guest.Input.Reset(); BroadcastLobby();
            var bytes = NetworkCodec.Pack(w => { w.Write(matchEpoch); NetworkCodec.WriteFields(w, rules); foreach (var character in lineup) w.Write((int)character); });
            foreach (var guest in guests.Where(g => g.Joined)) guest.Peer.Send(PacketKind.Start, bytes);
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
            if (!IsHost || !Connected) return; Lobby.Running = false; eventBatch.Clear();
            foreach (var guest in guests) guest.Input.Reset();
            foreach (var member in Lobby.Members) member.Ready = member.Host; BroadcastLobby(); ReturnedToLobby?.Invoke();
        }
        internal static void WriteLobby(BinaryWriter w, RoomLobby lobby)
        {
            NetworkCodec.Text(w, NetworkCodec.Version); NetworkCodec.Text(w, lobby.Id, 32); NetworkCodec.Text(w, lobby.Name, 32); w.Write((int)lobby.Route); w.Write(lobby.RequiresPassword); w.Write(lobby.Running); w.Write(lobby.Port);
            w.Write(lobby.Members.Length); foreach (var m in lobby.Members) { w.Write(m.Slot); NetworkCodec.Text(w, m.Name, 24); NetworkCodec.Text(w, m.AccountId, 16); w.Write((int)m.Character); w.Write(m.Ready); w.Write(m.Host); }
        }
        internal static RoomLobby ReadLobby(BinaryReader r)
        {
            if (NetworkCodec.Text(r) != NetworkCodec.Version) throw new InvalidDataException();
            var lobby = new RoomLobby { Id = NetworkCodec.Text(r, 32), Name = NetworkCodec.Text(r, 32), Route = (NetworkRoute)r.ReadInt32(), RequiresPassword = r.ReadBoolean(), Running = r.ReadBoolean(), Port = r.ReadInt32() };
            if (!Guid.TryParseExact(lobby.Id, "N", out _)) throw new InvalidDataException();
            if (!Enum.IsDefined(typeof(NetworkRoute), lobby.Route) || lobby.Port < 1 || lobby.Port > 65535) throw new InvalidDataException();
            int count = r.ReadInt32(); if (count < 1 || count > 6) throw new InvalidDataException(); var members = new List<RoomMember>();
            for (int i = 0; i < count; i++)
            {
                var m = new RoomMember { Slot = r.ReadInt32(), Name = NetworkCodec.Text(r, 24), AccountId = NetworkCodec.Text(r, 16), Character = (CharacterId)r.ReadInt32(), Ready = r.ReadBoolean(), Host = r.ReadBoolean() };
                if (m.AccountId.Length > 0 && !DebugAccounts.IsKnown(m.AccountId) || lobby.Route == NetworkRoute.Internet && !DebugAccounts.IsKnown(m.AccountId)) throw new InvalidDataException();
                if (m.Slot < 0 || m.Slot >= 6 || !RoomOptions.ValidCharacter(m.Character) || members.Any(a => a.Slot == m.Slot)) throw new InvalidDataException(); members.Add(m);
            }
            if (members.Count(m => m.Host) != 1) throw new InvalidDataException(); lobby.Members = members.ToArray(); return lobby;
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
