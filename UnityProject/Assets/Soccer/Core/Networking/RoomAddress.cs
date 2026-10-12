using System;
using System.Linq;
using System.Text;

namespace Soccer.Core.Networking
{
    /// <summary>Player invitations carry the endpoint and room identity without exposing port setup.</summary>
    internal sealed class RoomAddress
    {
        internal const int DefaultPort = 27860;
        internal string Host, RoomId;
        internal int Port;
        internal bool HasPort;
        internal NetworkRoute? Route;
        internal static RoomAddress Parse(string text)
        {
            text = (text ?? "").Trim();
            if (text.StartsWith("SS1-", StringComparison.Ordinal))
            {
                try
                {
                    string token = text.Substring(4);
                    if (token.Length == 0 || token.Length > 1400 || token.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_'))) throw new FormatException();
                    token = token.Replace('-', '+').Replace('_', '/');
                    token = token.PadRight((token.Length + 3) / 4 * 4, '=');
                    var fields = Encoding.UTF8.GetString(Convert.FromBase64String(token)).Split('\n');
                    if (fields.Length != 4 || !Guid.TryParseExact(fields[3], "N", out _) || !int.TryParse(fields[2], out int route) || !Enum.IsDefined(typeof(NetworkRoute), route)) throw new FormatException();
                    var result = Parse(fields[0] + ":" + fields[1]);
                    result.Route = (NetworkRoute)route; result.RoomId = fields[3]; return result;
                }
                catch (Exception e) when (e is FormatException || e is ArgumentException)
                { throw new ArgumentException("房间邀请无效，请重新复制完整邀请"); }
            }
            if (text.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase)) text = text.Substring(6);
            int split = text.LastIndexOf(':'); string host = split < 0 ? text : text.Substring(0, split); int port = DefaultPort;
            if (split >= 0 && !int.TryParse(text.Substring(split + 1), out port)) throw new ArgumentException("请输入房主 IP、域名或完整房间邀请");
            if (host.Length < 1 || host.Length > 253 || host.Any(c => !(char.IsLetterOrDigit(c) || c == '.' || c == '-')) || port < 1 || port > 65535)
                throw new ArgumentException("请输入房主 IP、域名或完整房间邀请");
            return new RoomAddress { Host = host, Port = port, HasPort = split >= 0 };
        }
        internal static string Invite(string host, int port, NetworkRoute route, string roomId)
        {
            var endpoint = Parse(host);
            if (endpoint.Route.HasValue || port < 1 || port > 65535 || !Enum.IsDefined(typeof(NetworkRoute), route) || !Guid.TryParseExact(roomId, "N", out _)) throw new ArgumentException("邀请地址无效");
            string content = endpoint.Host + "\n" + port + "\n" + (int)route + "\n" + roomId;
            return "SS1-" + Convert.ToBase64String(Encoding.UTF8.GetBytes(content)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
