using System;
using System.Globalization;
using System.Linq;

namespace Soccer.Core
{
    public static class PlayerNickname
    {
        public const int MaxCharacters=6,MaxWireLength=128;
        public static int Count(string value)=>new StringInfo(value ?? "").LengthInTextElements;
        public static string Normalize(string value)
        {
            value=(value ?? "").Trim();
            if(value.Length==0 || value.Length>MaxWireLength || value.Any(char.IsControl))throw new ArgumentException("请输入 1–6 个字的名字");
            for(int i=0;i<value.Length;i++)
                if(char.IsSurrogate(value[i]))
                {if(!char.IsHighSurrogate(value[i]) || i+1>=value.Length || !char.IsLowSurrogate(value[++i]))throw new ArgumentException("名字包含无效字符");}
            if(Count(value)>MaxCharacters)throw new ArgumentException("名字最多 6 个字");
            return value;
        }
        public static bool IsValid(string value)
        {try{return Normalize(value)==value;}catch(ArgumentException){return false;}}
    }
}
