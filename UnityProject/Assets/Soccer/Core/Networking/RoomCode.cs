using System;
using System.Security.Cryptography;

namespace Soccer.Core.Networking
{
    public static class RoomCode
    {
        public const int Length = 6;
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        public static bool TryNormalize(string value, out string code)
        {
            code = (value ?? "").Trim().ToUpperInvariant();
            if (code.Length != Length) return false;
            foreach (char c in code) if (Alphabet.IndexOf(c) < 0) return false;
            return true;
        }
        public static bool IsValid(string value) => TryNormalize(value, out string code) && code == value;
        public static string Generate()
        {
            using (var random = RandomNumberGenerator.Create())
            {
                var bytes = new byte[32]; var chars = new char[Length];
                while (true)
                {
                    int filled = 0; bool letter = false, digit = false;
                    while (filled < Length)
                    {
                        random.GetBytes(bytes);
                        foreach (byte value in bytes)
                        {
                            if (value >= 252) continue; // Avoid modulo bias across 36 symbols.
                            char c = Alphabet[value % Alphabet.Length]; chars[filled++] = c;
                            letter |= c >= 'A' && c <= 'Z'; digit |= c >= '0' && c <= '9';
                            if (filled == Length) break;
                        }
                    }
                    if (letter && digit) return new string(chars);
                }
            }
        }
    }
}
