using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Soccer.Core.Networking
{
    public enum PacketKind : byte { Hello = 1, Challenge, Authenticate, Lobby, Reject, Choice, Ready, Start, Input, Snapshot, Ping, Pong, Notice, Draft, DraftLock }
    public sealed class WirePacket
    {
        public PacketKind Kind;
        public byte[] Body;
        public WirePacket(PacketKind kind, byte[] body) { Kind = kind; Body = body; }
    }
    public interface IRoomPeer : IDisposable
    {
        bool Connected {get;}
        string AuthenticatedAccountId {get;}
        bool Send(PacketKind kind,byte[] body);
        bool TryRead(out WirePacket packet);
        void Pump();
    }
    public static class RoomFrame
    {
        public static byte[] Encode(PacketKind kind,byte[] body)
        {Validate(kind,body);var bytes=new byte[body.Length+1];bytes[0]=(byte)kind;Buffer.BlockCopy(body,0,bytes,1,body.Length);return bytes;}
        public static WirePacket Decode(byte[] bytes)
        {if(bytes==null || bytes.Length<1 || bytes.Length>NetworkCodec.MaxFrameBytes+1)throw new InvalidDataException();var body=new byte[bytes.Length-1];Buffer.BlockCopy(bytes,1,body,0,body.Length);var kind=(PacketKind)bytes[0];Validate(kind,body);return new WirePacket(kind,body);}
        public static void Validate(PacketKind kind,byte[] body)
        {int limit=kind==PacketKind.Snapshot ? NetworkCodec.MaxFrameBytes : kind==PacketKind.Input ? 512 : 8192;if(body==null || !Enum.IsDefined(typeof(PacketKind),kind) || body.Length>limit)throw new InvalidDataException("网络消息无效");}
    }
    internal sealed class RoomPeer : IRoomPeer
    {
        private readonly TcpClient client;
        private readonly BlockingCollection<WirePacket> outgoing = new BlockingCollection<WirePacket>(128);
        private readonly ConcurrentQueue<WirePacket> incoming = new ConcurrentQueue<WirePacket>();
        private int incomingCount, disposed;
        public bool Connected => Volatile.Read(ref disposed) == 0;
        public string AuthenticatedAccountId=>"";
        public void Pump(){}
        public RoomPeer(TcpClient socket)
        {
            client = socket; client.NoDelay = true;
            new Thread(ReadLoop) { IsBackground = true, Name = "Soccer socket receive" }.Start();
            new Thread(WriteLoop) { IsBackground = true, Name = "Soccer socket send" }.Start();
        }
        public bool Send(PacketKind kind, byte[] body)
        {
            if (!Connected || body.Length > NetworkCodec.MaxFrameBytes) return false;
            try { if (outgoing.TryAdd(new WirePacket(kind, body))) return true; }
            catch (InvalidOperationException) { }
            Dispose(); return false;
        }
        public bool TryRead(out WirePacket packet)
        { if (!incoming.TryDequeue(out packet)) return false; Interlocked.Decrement(ref incomingCount); return true; }
        private void ReadLoop()
        {
            try
            {
                var stream = client.GetStream(); var header = new byte[5];
                while (Connected)
                {
                    ReadExact(stream, header); int length = header[1] | header[2] << 8 | header[3] << 16 | header[4] << 24;
                    int limit = header[0] == (byte)PacketKind.Snapshot ? NetworkCodec.MaxFrameBytes : header[0] == (byte)PacketKind.Input ? 512 : 8192;
                    if (!Enum.IsDefined(typeof(PacketKind), header[0]) || length < 0 || length > limit) throw new InvalidDataException();
                    var bytes = new byte[length]; ReadExact(stream, bytes);
                    if (Interlocked.Increment(ref incomingCount) > 128) throw new InvalidDataException();
                    incoming.Enqueue(new WirePacket((PacketKind)header[0], bytes));
                }
            }
            catch (Exception) { Dispose(); }
        }
        private static void ReadExact(Stream stream, byte[] bytes)
        { int offset = 0; while (offset < bytes.Length) { int n = stream.Read(bytes, offset, bytes.Length - offset); if (n == 0) throw new EndOfStreamException(); offset += n; } }
        private void WriteLoop()
        {
            try
            {
                var stream = client.GetStream();
                foreach (var packet in outgoing.GetConsumingEnumerable())
                {
                    int n = packet.Body.Length; var header = new byte[] { (byte)packet.Kind, (byte)n, (byte)(n >> 8), (byte)(n >> 16), (byte)(n >> 24) };
                    stream.Write(header, 0, 5); stream.Write(packet.Body, 0, n);
                    if (packet.Kind == PacketKind.Reject) { Dispose(); return; }
                }
            }
            catch (Exception) { Dispose(); }
        }
        public void Dispose()
        { if (Interlocked.Exchange(ref disposed, 1) != 0) return; outgoing.CompleteAdding(); client.Close(); }
    }
}
