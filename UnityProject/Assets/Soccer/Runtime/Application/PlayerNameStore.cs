using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Soccer.Core;

namespace Soccer.Runtime
{
    public sealed class PlayerNameStore
    {
        private readonly string path;
        public PlayerNameStore(string directory,string account)
        {
            using(var hash=SHA256.Create())path=Path.Combine(directory,BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(account))).Replace("-","").ToLowerInvariant()+".txt");
        }
        public string Load()
        {
            if(!File.Exists(path))return "";
            if(new FileInfo(path).Length>1024)return "";
            var name=File.ReadAllText(path);return PlayerNickname.IsValid(name) ? name : "";
        }
        public void Save(string value)
        {
            value=PlayerNickname.Normalize(value);Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temporary,value);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
