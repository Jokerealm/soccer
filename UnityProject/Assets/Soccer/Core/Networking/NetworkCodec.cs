using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Soccer.Core.Networking
{
    // A fixed binary schema of scalar state, never arbitrary object deserialization.
    public static class NetworkCodec
    {
        public const string Version = "SkillSoccer/0.31.0/11";
        public const int MaxFrameBytes = 262144;
        private static readonly ConcurrentDictionary<Type,PropertyInfo[]> properties = new ConcurrentDictionary<Type,PropertyInfo[]>();
        private static readonly ConcurrentDictionary<Type,FieldInfo[]> fields = new ConcurrentDictionary<Type,FieldInfo[]>();
        private static PropertyInfo[] Props(Type type) => properties.GetOrAdd(type,t=>t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p=>p.GetSetMethod(true)!=null && p.Name!=nameof(PlayerState.Stats)).OrderBy(p=>p.Name,StringComparer.Ordinal).ToArray());
        public static byte[] Pack(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            { using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) write(writer); return stream.ToArray(); }
        }
        public static void Read(byte[] bytes, Action<BinaryReader> read)
        {
            if (bytes == null || bytes.Length > MaxFrameBytes) throw new InvalidDataException("网络消息长度无效");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            { read(reader); if (stream.Position != stream.Length) throw new InvalidDataException("网络消息包含多余数据"); }
        }
        public static void Text(BinaryWriter w, string value, int max = 128)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? "");
            if (bytes.Length > max * 4 || (value ?? "").Length > max) throw new InvalidDataException("网络文本过长");
            w.Write((ushort)bytes.Length); w.Write(bytes);
        }
        public static string Text(BinaryReader r, int max = 128)
        {
            int count = r.ReadUInt16(); if (count > max * 4) throw new InvalidDataException("网络文本过长");
            var bytes = r.ReadBytes(count); if (bytes.Length != count) throw new EndOfStreamException();
            var result = new UTF8Encoding(false, true).GetString(bytes);
            if (result.Length > max) throw new InvalidDataException("网络文本过长"); return result;
        }
        private static void Scalar(BinaryWriter w, Type type, object value)
        {
            if (type.IsEnum) w.Write(Convert.ToInt32(value));
            else if (type == typeof(float)) w.Write((float)value);
            else if (type == typeof(int)) w.Write((int)value);
            else if (type == typeof(long)) w.Write((long)value);
            else if (type == typeof(bool)) w.Write((bool)value);
            else if (type == typeof(string)) Text(w, (string)value,512);
            else if (type == typeof(Vec2)) { var v = (Vec2)value; w.Write(v.X); w.Write(v.Y); }
            else throw new InvalidDataException("不支持的网络字段 " + type.Name);
        }
        private static float Number(BinaryReader r)
        { float n = r.ReadSingle(); if (!Geometry.IsFinite(n)) throw new InvalidDataException("网络数值非有限"); return n; }
        private static object Scalar(BinaryReader r, Type type)
        {
            if (type.IsEnum) { int n = r.ReadInt32(); if (!Enum.IsDefined(type, n)) throw new InvalidDataException("网络枚举无效"); return Enum.ToObject(type, n); }
            if (type == typeof(float)) return Number(r);
            if (type == typeof(int)) return r.ReadInt32();
            if (type == typeof(long)) return r.ReadInt64();
            if (type == typeof(bool)) { byte v = r.ReadByte(); if (v > 1) throw new InvalidDataException("网络布尔值无效"); return v != 0; }
            if (type == typeof(string)) return Text(r,512);
            if (type == typeof(Vec2)) return new Vec2(Number(r), Number(r));
            throw new InvalidDataException("不支持的网络字段");
        }
        private static void WriteProps(BinaryWriter w, object obj)
        { foreach (var p in Props(obj.GetType())) Scalar(w, p.PropertyType, p.GetValue(obj)); }
        private static void ReadProps(BinaryReader r, object obj)
        { foreach (var p in Props(obj.GetType())) p.SetValue(obj, Scalar(r, p.PropertyType)); }
        private static FieldInfo[] Fields(Type type) => fields.GetOrAdd(type,t=>t.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f=>f.Name,StringComparer.Ordinal).ToArray());
        public static void WriteFields(BinaryWriter w, object obj)
        { foreach (var f in Fields(obj.GetType())) Scalar(w, f.FieldType, f.GetValue(obj)); }
        public static T ReadFields<T>(BinaryReader r) where T : new()
        { object obj = new T(); foreach (var f in Fields(typeof(T))) f.SetValue(obj, Scalar(r, f.FieldType)); return (T)obj; }
        private static int Count(BinaryReader r, int max)
        { int count = r.ReadInt32(); if (count < 0 || count > max) throw new InvalidDataException("网络列表长度无效"); return count; }
        private static void WriteList<T>(BinaryWriter w, IReadOnlyList<T> list)
        { w.Write(list.Count); foreach (var item in list) WriteProps(w, item); }
        private static void ReadList<T>(BinaryReader r, List<T> list, int max) where T : new()
        { int count = Count(r, max); for (int i = 0; i < count; i++) { var item = new T(); ReadProps(r, item); list.Add(item); } }
        public static byte[] Snapshot(MatchState state, IReadOnlyList<MatchEvent> events = null) => Pack(w =>
        {
            Text(w, Version); WriteProps(w, state); WriteProps(w, state.Ball);
            w.Write(state.Players.Count);
            foreach (var p in state.Players)
            { w.Write((int)p.Stats.Id); WriteProps(w, p); w.Write(p.NavigationPath.Count); foreach (var point in p.NavigationPath) Scalar(w, typeof(Vec2), point); }
            WriteList(w, state.Effects); WriteList(w, state.Projectiles); WriteList(w, state.Bombs);
            var batch = events ?? state.Events; w.Write(batch.Count); foreach (var e in batch) WriteFields(w, e);
        });
        public static void ApplySnapshot(MatchState state, byte[] bytes)
        {
            // Parse and validate a complete temporary state before changing any live object.
            var next = new MatchState();
            Read(bytes, r =>
            {
                if (Text(r) != Version) throw new InvalidDataException("联网版本不一致");
                ReadProps(r, next); ReadProps(r, next.Ball);
                int count = Count(r, 6); if (count != state.Players.Count) throw new InvalidDataException("球员数量不一致");
                for (int i = 0; i < count; i++)
                {
                    int character = r.ReadInt32(); if (character != (int)state.Players[i].Stats.Id) throw new InvalidDataException("角色阵容不一致");
                    var player = new PlayerState { Stats = state.Players[i].Stats }; ReadProps(r, player);
                    if (player.Id != i || player.Team != (i < 3 ? Team.Cyan : Team.Amber) || player.OrderTargetId < -1 || player.OrderTargetId >= count ||
                        Math.Abs(player.Position.X) > 256 || Math.Abs(player.Position.Y) > 256 || player.Health < 0 || player.Health > player.Stats.MaxHealth || player.Energy < 0 || player.Energy > player.Stats.MaxEnergy)
                        throw new InvalidDataException("球员状态无效");
                    int path = Count(r, 256); for (int n = 0; n < path; n++) player.MutablePath.Add((Vec2)Scalar(r, typeof(Vec2)));
                    next.MutablePlayers.Add(player);
                }
                ReadList(r, next.MutableEffects, 64); ReadList(r, next.MutableProjectiles, 256); ReadList(r, next.MutableBombs, 32);
                int events = Count(r, 4096); for (int i = 0; i < events; i++) next.MutableEvents.Add(ReadFields<MatchEvent>(r));
                foreach (int id in new[] { next.Ball.OwnerId, next.Ball.LastKickerId, next.Ball.LastTouchId, next.Ball.DroppedOwnerId, next.Ball.RestartKickerId, next.RestartPlayerId })
                    if (id < -1 || id >= count) throw new InvalidDataException("球权编号无效");
                if (next.Tick < 0 || next.Half < 1 || next.Half > 2 || next.CyanScore < 0 || next.AmberScore < 0 || next.Mode != MatchMode.BotMatch)
                    throw new InvalidDataException("比赛状态无效");
                foreach (var e in next.Events) if (e.ActorId < -1 || e.ActorId >= count || e.TargetId < -1 || e.TargetId >= count) throw new InvalidDataException("事件编号无效");
                foreach (var shot in next.Projectiles) if (shot.ActorId < 0 || shot.ActorId >= count) throw new InvalidDataException("弹体编号无效");
                foreach (var bomb in next.Bombs) if (bomb.ActorId < 0 || bomb.ActorId >= count) throw new InvalidDataException("炸弹编号无效");
            });
            if (next.Tick < state.Tick) return;
            // Clients interpolate between received snapshots, not just the host's last 60 Hz substep.
            foreach (var player in next.Players) player.PreviousPosition = state.Players[player.Id].Position;
            next.Ball.PreviousPosition = state.Ball.Position; next.Ball.PreviousHeight = state.Ball.Height;
            foreach (var p in Props(typeof(MatchState))) p.SetValue(state, p.GetValue(next));
            foreach (var p in Props(typeof(BallState))) p.SetValue(state.Ball, p.GetValue(next.Ball));
            for (int i = 0; i < state.Players.Count; i++)
            {
                foreach (var p in Props(typeof(PlayerState))) p.SetValue(state.Players[i], p.GetValue(next.Players[i]));
                state.Players[i].MutablePath.Clear(); state.Players[i].MutablePath.AddRange(next.Players[i].NavigationPath);
            }
            state.MutableEffects.Clear(); state.MutableEffects.AddRange(next.Effects);
            state.MutableProjectiles.Clear(); state.MutableProjectiles.AddRange(next.Projectiles);
            state.MutableBombs.Clear(); state.MutableBombs.AddRange(next.Bombs);
            state.MutableEvents.Clear(); state.MutableEvents.AddRange(next.Events);
        }
        public static bool ValidCommand(PlayerCommand c) => Geometry.IsFinite(c.Move.X) && Geometry.IsFinite(c.Move.Y) &&
            Geometry.IsFinite(c.Aim.X) && Geometry.IsFinite(c.Aim.Y) && Geometry.IsFinite(c.OrderPosition.X) && Geometry.IsFinite(c.OrderPosition.Y) &&
            c.Move.LengthSquared <= 1.001f && Math.Abs(c.Aim.X) <= 256 && Math.Abs(c.Aim.Y) <= 256 &&
            Math.Abs(c.OrderPosition.X) <= 128 && Math.Abs(c.OrderPosition.Y) <= 128 && c.OrderTargetId >= -1 && c.OrderTargetId < 6;
    }
    public sealed class RemoteCommandBuffer
    {
        private PlayerCommand pending;
        private long sequence = -1;
        private double lastInput;
        private bool timedOut;
        public void Reset() { pending = default; lastInput = 0; timedOut = false; }
        public bool Accept(long seq, PlayerCommand command, double now)
        {
            if (seq <= sequence || !NetworkCodec.ValidCommand(command)) return false;
            command.KickPressed |= pending.KickPressed; command.KickReleased |= pending.KickReleased;
            command.TacklePressed |= pending.TacklePressed; command.QPressed |= pending.QPressed;
            command.EPressed |= pending.EPressed; command.EReleased |= pending.EReleased; command.SpecialPressed |= pending.SpecialPressed;
            command.Cancel |= pending.Cancel; command.StopPressed |= pending.StopPressed;
            if (pending.OrderPressed && !command.OrderPressed)
            { command.OrderPressed = true; command.OrderKind = pending.OrderKind; command.OrderPosition = pending.OrderPosition; command.OrderTargetId = pending.OrderTargetId; command.QueueOrder = pending.QueueOrder; }
            pending = command; sequence = seq; lastInput = now; timedOut = false; return true;
        }
        public PlayerCommand Read(double now)
        {
            if (now - lastInput > .4)
            { pending = default; if (timedOut) return default; timedOut = true; return new PlayerCommand { Cancel = true, StopPressed = true }; }
            var result = pending; pending = pending.WithoutEdges(); return result;
        }
    }
}
