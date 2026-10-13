using System.Net;
using System.Security.Cryptography;
using Soccer.Core.Networking;

// Rooms are leased, and ownership belongs to the login session rather than a shared debug account.
sealed class RoomDirectory
{
    private sealed record Session(DateTimeOffset Expires,string Account);
    private sealed class Room
    {
        internal required string Code, Owner;
        internal required RoomPublication Data;
        internal DateTimeOffset Expires;
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Session> sessions = new();
    private readonly Dictionary<string, Room> rooms = new();
    private readonly Dictionary<string, DateTimeOffset> retired = new();
    private readonly int leaseSeconds;
    internal RoomDirectory(int leaseSeconds) { this.leaseSeconds = Math.Clamp(leaseSeconds, 3, 120); }
    internal string? Login(string account)
    {
        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var key in sessions.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToArray()) sessions.Remove(key);
            if (sessions.Count >= 1024) return null;
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            sessions[token] = new Session(now.AddHours(6),account); return token;
        }
    }
    private string? Authorize(HttpContext context)
    {
        string header = context.Request.Headers["Authorization"].ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || header.Length > 128) return null;
        string token = header.Substring(7);
        return sessions.TryGetValue(token, out var session) && session.Expires > DateTimeOffset.UtcNow ? token : null;
    }
    internal string? Account(HttpContext context)
    {lock(gate){var token=Authorize(context);return token==null ? null : sessions[token].Account;}}
    private void Purge()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in rooms.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToArray()) { rooms.Remove(key); retired[key] = now.AddMinutes(10); }
        foreach (var key in retired.Where(p => p.Value <= now).Select(p => p.Key).ToArray()) retired.Remove(key);
    }
    private static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);
    private static RoomPublication? Validate(RoomPublication data, HttpContext context)
    {
        string? host = data.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            var ip = context.Connection.RemoteIpAddress;
            host = ip?.IsIPv4MappedToIPv6 == true ? ip.MapToIPv4().ToString() : ip?.ToString();
        }
        host = (host ?? "").Trim(); string name = (data.Name ?? "").Trim();
        if (!Guid.TryParseExact(data.RoomId, "N", out _) || !RoomCode.IsValid(data.Code ?? "") || !data.Code!.Any(c => c >= 'A' && c <= 'Z') || !data.Code!.Any(c => c >= '0' && c <= '9') || data.Port < 1 || data.Port > 65535 || data.Players < 1 || data.Players > 6 ||
            name.Length < 1 || name.Length > 32 || name.Any(char.IsControl) || host.Length < 1 || host.Length > 253 || host.Any(c => !(char.IsLetterOrDigit(c) || c == '.' || c == '-'))) return null;
        return data with { Host = host, Name = name };
    }
    private object Describe(Room room) => new { code = room.Code, roomId = room.Data.RoomId, name = room.Data.Name, host = room.Data.Host,
        port = room.Data.Port, players = room.Data.Players, running = room.Data.Running, requiresPassword = room.Data.RequiresPassword, expiresInSeconds = leaseSeconds };
    internal IResult Create(HttpContext context, RoomPublication data)
    {
        lock (gate)
        {
            string? owner = Authorize(context); if (owner == null) return Error(401, "请重新登录");
            var valid = Validate(data, context); if (valid == null) return Error(400, "房间信息无效");
            Purge(); var existing = rooms.Values.FirstOrDefault(r => r.Owner == owner && r.Data.RoomId == valid.RoomId);
            if (existing != null) { existing.Data = valid; existing.Expires = DateTimeOffset.UtcNow.AddSeconds(leaseSeconds); return Results.Ok(Describe(existing)); }
            if (rooms.Count >= 4096 || retired.Count >= 16384 || rooms.Values.Count(r => r.Owner == owner) >= 64) return Error(429, "房间服务繁忙，请稍后重试");
            string code = valid.Code!;
            while (rooms.ContainsKey(code) || retired.ContainsKey(code)) code = RoomCode.Generate();
            var room = new Room { Code = code, Owner = owner, Data = valid, Expires = DateTimeOffset.UtcNow.AddSeconds(leaseSeconds) };
            rooms.Add(code, room); return Results.Json(Describe(room), statusCode: 201);
        }
    }
    internal IResult Find(HttpContext context, string value)
    {
        lock (gate)
        {
            if (Authorize(context) == null) return Error(401, "请重新登录");
            if (!RoomCode.TryNormalize(value, out string code)) return Error(400, "请输入 6 位大写字母和数字组成的房间号");
            Purge(); if (!rooms.TryGetValue(code, out var room)) return Error(404, "房间不存在或已关闭，请确认房间号");
            if (room.Data.Running) return Error(409, "比赛已经开始，请等待房主返回房间");
            if (room.Data.Players >= 6) return Error(409, "房间已满");
            return Results.Ok(Describe(room));
        }
    }
    internal IResult Renew(HttpContext context, string value, RoomPublication data)
    {
        lock (gate)
        {
            string? owner = Authorize(context); if (owner == null) return Error(401, "请重新登录");
            if (!RoomCode.TryNormalize(value, out string code)) return Error(400, "房间号无效");
            Purge(); if (!rooms.TryGetValue(code, out var room)) return Error(404, "房间已过期");
            if (room.Owner != owner) return Error(403, "只有房主可以更新房间");
            var valid = Validate(data, context); if (valid == null || valid.RoomId != room.Data.RoomId) return Error(400, "房间信息无效");
            room.Data = valid; room.Expires = DateTimeOffset.UtcNow.AddSeconds(leaseSeconds); return Results.Ok(Describe(room));
        }
    }
    internal IResult Remove(HttpContext context, string value)
    {
        lock (gate)
        {
            string? owner = Authorize(context); if (owner == null) return Error(401, "请重新登录");
            if (!RoomCode.TryNormalize(value, out string code)) return Error(400, "房间号无效");
            Purge(); if (!rooms.TryGetValue(code, out var room)) return Results.NoContent();
            if (room.Owner != owner) return Error(403, "只有房主可以关闭房间");
            rooms.Remove(code); retired[code] = DateTimeOffset.UtcNow.AddMinutes(10); return Results.NoContent();
        }
    }
}
record RoomPublication(string? Code, string? RoomId, string? Name, string? Host, int Port, int Players, bool Running, bool RequiresPassword);
