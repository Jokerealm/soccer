using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    public sealed class MatchHistoryStore : IMatchHistoryStore
    {
        private readonly string path;
        public MatchHistoryStore(string directory,string profile)
        {
            using(var hash=SHA256.Create())path=Path.Combine(directory,BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(profile))).Replace("-","").ToLowerInvariant()+".json");
        }
        public MatchHistoryData Load()
        {
            if(!File.Exists(path))return new MatchHistoryData();
            if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("对局记录存档过大");
            var data=JsonUtility.FromJson<MatchHistoryData>(File.ReadAllText(path));MatchHistory.Validate(data);return data;
        }
        public void Save(MatchHistoryData data)
        {
            MatchHistory.Validate(data);Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temporary,JsonUtility.ToJson(data,true));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
