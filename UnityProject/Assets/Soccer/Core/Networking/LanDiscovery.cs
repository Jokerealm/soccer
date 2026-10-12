using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Soccer.Core.Networking
{
    internal sealed class LanAdvertiser : IDisposable
    {
        internal const int DiscoveryPort = 27861;
        internal static readonly byte[] Query = Encoding.ASCII.GetBytes("SkillSoccer-LAN-0.17.1");
        private readonly UdpClient socket;
        private volatile byte[] description;
        private volatile bool disposed;
        public LanAdvertiser()
        {
            socket = new UdpClient(AddressFamily.InterNetwork);
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort)); Task.Run(Receive);
        }
        public void Update(byte[] bytes) => description = bytes;
        private async Task Receive()
        {
            try
            {
                while (!disposed)
                {
                    var request = await socket.ReceiveAsync(); var bytes = description;
                    if (bytes != null && request.Buffer.SequenceEqual(Query)) await socket.SendAsync(bytes, bytes.Length, request.RemoteEndPoint);
                }
            }
            catch (Exception) { }
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
        private readonly UdpClient socket;
        private readonly ConcurrentQueue<Tuple<RoomLobby, string>> incoming = new ConcurrentQueue<Tuple<RoomLobby, string>>();
        private readonly Dictionary<string, DiscoveredRoom> rooms = new Dictionary<string, DiscoveredRoom>();
        private volatile bool disposed;
        private double lastSearch = -10;
        public IReadOnlyList<DiscoveredRoom> Rooms => rooms.Values.OrderBy(r => r.Lobby.Name, StringComparer.Ordinal).ToArray();
        public LanDiscovery()
        { socket = new UdpClient(0); socket.EnableBroadcast = true; Task.Run(Receive); }
        public void Pump(double now, bool refresh = false)
        {
            if (disposed) return;
            if (refresh || now - lastSearch >= 2)
            {
                lastSearch = now;
                foreach (var address in new[] { IPAddress.Broadcast, IPAddress.Loopback })
                    try { socket.Send(LanAdvertiser.Query, LanAdvertiser.Query.Length, new IPEndPoint(address, LanAdvertiser.DiscoveryPort)); } catch (SocketException) { }
            }
            while (incoming.TryDequeue(out var result))
            {
                var address = result.Item2 + ":" + result.Item1.Port;
                if (rooms.Count >= 64 && !rooms.ContainsKey(address)) continue;
                rooms[address] = new DiscoveredRoom { Lobby = result.Item1, Address = address, Seen = now };
            }
            foreach (var key in rooms.Where(r => now - r.Value.Seen > 6).Select(r => r.Key).ToArray()) rooms.Remove(key);
        }
        private async Task Receive()
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
            catch (Exception) { }
        }
        public void Dispose() { disposed = true; socket.Close(); }
    }
}
