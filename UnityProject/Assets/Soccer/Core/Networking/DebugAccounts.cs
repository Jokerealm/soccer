using System;
using System.Security.Cryptography;
using System.Text;

namespace Soccer.Core.Networking
{
    /// <summary>Explicit public test identities for the debug build; no registration or cloud account service.</summary>
    public static class DebugAccounts
    {
        public static DebugAccountSession FromService(string id)
        {
            if(string.IsNullOrEmpty(id) || id.Length>32 || System.Array.Exists(id.ToCharArray(),c=>!char.IsLetterOrDigit(c) && c!='_'))throw new ArgumentException("帐号无效");
            return new DebugAccountSession(id,new byte[32]);
        }
        public static bool IsKnown(string id) => id == "a" || id == "b";
        private static byte[] Key(string id, string password)
        { using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes("SkillSoccer/debug-account/1|" + id + "|" + password)); }
        public static DebugAccountSession Login(string account, string password)
        {
            string id = (account ?? "").Trim();
            if (!IsKnown(id) || password == null || password.Length > 64) return null;
            var candidate = Key(id, password); var expected = Key(id, id);
            bool valid = RoomPassword.Equal(candidate, expected); Array.Clear(expected, 0, expected.Length);
            if (valid) return new DebugAccountSession(id, candidate);
            Array.Clear(candidate, 0, candidate.Length); return null;
        }
        public static bool Verify(string id, byte[] nonce, string identity, byte[] proof)
        {
            if (!IsKnown(id) || nonce == null || nonce.Length != 32) return false;
            var key = Key(id, id);
            try { return RoomPassword.Equal(RoomPassword.Proof(key, nonce, "account|" + identity), proof); }
            finally { Array.Clear(key, 0, key.Length); }
        }
    }
    public sealed class DebugAccountSession : IDisposable
    {
        public string Id { get; }
        public string DisplayName => "帐号 " + Id;
        private byte[] key;
        internal DebugAccountSession(string id, byte[] derivedKey) { Id = id; key = derivedKey; }
        public byte[] Proof(byte[] nonce, string identity)
        { if (key == null) throw new ObjectDisposedException(nameof(DebugAccountSession)); return RoomPassword.Proof(key, nonce, "account|" + identity); }
        public void Dispose() { if (key != null) Array.Clear(key, 0, key.Length); key = null; }
    }
}
