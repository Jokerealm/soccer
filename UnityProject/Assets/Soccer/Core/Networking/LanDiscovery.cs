using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Soccer.Core.Networking
{
    internal static class LanInterfaces
    {
        internal const int Port = 27861;
        internal static readonly IPAddress Group = IPAddress.Parse("239.255.27.86");
        internal static readonly byte[] Query = Encoding.ASCII.GetBytes("SkillSoccer-LAN-discover/1");
        internal static IPAddress Broadcast(IPAddress address, IPAddress mask)
        { var ip = address.GetAddressBytes(); var bits = mask.GetAddressBytes(); return new IPAddress(ip.Select((b, i) => (byte)(b | ~bits[i])).ToArray()); }
        internal static UnicastIPAddressInformation[] Active()
        {
            try { return NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && a.IPv4Mask != null).ToArray(); }
            catch (Exception) { return new UnicastIPAddressInformation[0]; }
        }
        internal static UdpClient Listener()
        {
            var socket = new UdpClient(AddressFamily.InterNetwork);
            try
            {
                socket.ExclusiveAddressUse = false; socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Client.Bind(new IPEndPoint(IPAddress.Any, Port)); socket.EnableBroadcast = true;
                var interfaces = Active(); bool joined = false;
                foreach (var nic in interfaces) try { socket.JoinMulticastGroup(Group, nic.Address); joined = true; } catch (SocketException) { }
                if (!joined) try { socket.JoinMulticastGroup(Group); } catch (SocketException) { }
                return socket;
            }
            catch { socket.Close(); throw; }
        }
        internal static void Send(UdpClient socket, byte[] bytes, bool loopback)
        {
            var interfaces = Active();
            var targets = interfaces.Select(n => Broadcast(n.Address, n.IPv4Mask)).Concat(new[] { IPAddress.Broadcast }).Distinct().ToList();
            if (loopback) targets.Add(IPAddress.Loopback);
            foreach (var address in targets) TrySend(socket, bytes, address);
            // Send on each NIC so Wi-Fi/Ethernet/VPN routing cannot select just one multicast interface.
            foreach (var nic in interfaces)
            {
                try { socket.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, nic.Address.GetAddressBytes()); TrySend(socket, bytes, Group); }
                catch (SocketException) { }
            }
        }
        private static void TrySend(UdpClient socket, byte[] bytes, IPAddress address)
        { try { socket.Send(bytes, bytes.Length, new IPEndPoint(address, Port)); } catch (SocketException) { } }
    }
    internal sealed class LanAdvertiser : IDisposable
    {
        private readonly UdpClient socket;
        private volatile byte[] description;
        private volatile bool disposed;
        private double lastAnnouncement = -10;
        public LanAdvertiser() { socket = LanInterfaces.Listener(); Task.Run(Receive); }
        public void Update(byte[] bytes) => description = bytes;
        public void Pump(double now)
        {
            if (disposed || description == null || now - lastAnnouncement < 1) return;
            lastAnnouncement = now; LanInterfaces.Send(socket, description, false);
        }
        private async Task Receive()
        {
            try
            {
                while (!disposed)
                {
                    var request = await socket.ReceiveAsync(); var bytes = description;
                    if (bytes == null || !request.Buffer.SequenceEqual(LanInterfaces.Query)) continue;
                    try { await socket.SendAsync(bytes, bytes.Length, request.RemoteEndPoint); } catch (SocketException) { }
                }
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }
        public void Dispose() { disposed = true; socket.Close(); }
    }
    public sealed class DiscoveredRoom
    {
        public RoomLobby Lobby;
        public string Address;
        internal double Seen;
    }
    public sealed class LanDiscovery : IDisposable
    {
        private readonly UdpClient queries, announcements;
        private readonly ConcurrentQueue<Tuple<RoomLobby, string>> incoming = new ConcurrentQueue<Tuple<RoomLobby, string>>();
        private readonly Dictionary<string, DiscoveredRoom> rooms = new Dictionary<string, DiscoveredRoom>();
        private volatile bool disposed;
        private double lastSearch = -10;
        public string Status { get; private set; } = "";
        public IReadOnlyList<DiscoveredRoom> Rooms => rooms.Values.OrderBy(r => r.Lobby.Name, StringComparer.Ordinal).ToArray();
        public LanDiscovery()
        {
            queries = new UdpClient(0); queries.EnableBroadcast = true; Task.Run(() => Receive(queries));
            try { announcements = LanInterfaces.Listener(); Task.Run(() => Receive(announcements)); }
            catch (SocketException) { Status = "房间通告端口不可用，仍在主动搜索；也可输入房主地址"; }
        }
        public void Pump(double now, bool refresh = false)
        {
            if (disposed) return;
            if (refresh || now - lastSearch >= 1)
            { lastSearch = now; LanInterfaces.Send(queries, LanInterfaces.Query, true); }
            while (incoming.TryDequeue(out var result))
            {
                string key = result.Item1.Id, address = result.Item2 + ":" + result.Item1.Port;
                if (rooms.Count >= 64 && !rooms.ContainsKey(key)) continue;
                if (rooms.TryGetValue(key, out var old) && IPAddress.IsLoopback(IPAddress.Parse(result.Item2)) && !old.Address.StartsWith("127."))
                { old.Lobby = result.Item1; old.Seen = now; continue; }
                rooms[key] = new DiscoveredRoom { Lobby = result.Item1, Address = address, Seen = now };
            }
            foreach (var key in rooms.Where(r => now - r.Value.Seen > 5).Select(r => r.Key).ToArray()) rooms.Remove(key);
        }
        private async Task Receive(UdpClient socket)
        {
            try
            {
                while (!disposed)
                {
                    var response = await socket.ReceiveAsync();
                    if (response.Buffer.Length > 8192 || incoming.Count >= 64) continue;
                    try
                    {
                        RoomLobby lobby = null; NetworkCodec.Read(response.Buffer, r => lobby = NetworkRoom.ReadLobby(r));
                        if (lobby.Route == NetworkRoute.LAN && !lobby.RequiresPassword) incoming.Enqueue(Tuple.Create(lobby, response.RemoteEndPoint.Address.ToString()));
                    }
                    catch (Exception) { }
                }
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }
        public void Dispose() { disposed = true; queries.Close(); announcements?.Close(); }
    }
}
