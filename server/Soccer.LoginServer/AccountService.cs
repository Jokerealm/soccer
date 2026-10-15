using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Soccer.Core;

sealed class AccountService
{
    internal sealed class User
    {
        public string Id="",Name="",Salt="",Hash="",SteamId="";
        public int Rating=1000,RankedGames;
    }
    private sealed class Data
    {public Dictionary<string,User> Users=new();public HashSet<string> Settled=new();}
    private readonly object gate=new();private readonly string path;private readonly bool allowDebug;private Data data;
    private static readonly JsonSerializerOptions json=new(){IncludeFields=true};
    internal AccountService(string path,bool allowDebug=true)
    {
        this.path=Path.GetFullPath(path);this.allowDebug=allowDebug;data=File.Exists(path) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(path),json) ?? throw new InvalidDataException("帐号存档无效") : new();
        foreach(string id in allowDebug ? new[]{"a","b"} : Array.Empty<string>())if(!data.Users.ContainsKey(id))data.Users[id]=Create(id,"帐号 "+id,id);
    }
    private static User Create(string id,string name,string password)
    {byte[] salt=RandomNumberGenerator.GetBytes(24);return new User{Id=id,Name=name,Salt=Convert.ToHexString(salt),Hash=Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(password,salt,120000,HashAlgorithmName.SHA256,32))};}
    internal User? Find(string id){lock(gate)return !allowDebug && (id=="a" || id=="b") ? null : data.Users.GetValueOrDefault(id);}
    internal User? Login(string id,string password)
    {
        lock(gate){if(!allowDebug && (id=="a" || id=="b") || password==null || password.Length>64 || !data.Users.TryGetValue(id,out var user) || user.Hash.Length==0)return null;
            var actual=Rfc2898DeriveBytes.Pbkdf2(password,Convert.FromHexString(user.Salt),120000,HashAlgorithmName.SHA256,32);
            return CryptographicOperations.FixedTimeEquals(actual,Convert.FromHexString(user.Hash)) ? user : null;}
    }
    internal User Register(string id,string name,string password)
    {
        id=(id??"").Trim().ToLowerInvariant();name=PlayerNickname.Normalize(name);
        if(!Regex.IsMatch(id,"^[a-z0-9_]{3,16}$") || password==null || password.Length<8 || password.Length>64)throw new ArgumentException("帐号使用 3–16 位小写字母、数字或下划线；密码为 8–64 位。");
        lock(gate){if(data.Users.ContainsKey(id))throw new ArgumentException("帐号已被使用");if(data.Users.Count>=100000)throw new ArgumentException("帐号服务繁忙");
            var user=Create(id,name,password);data.Users.Add(id,user);try{Save();}catch{data.Users.Remove(id);throw;}return user;}
    }
    internal User Steam(string steamId,string name)
    {
        lock(gate){string id="S"+steamId;var found=data.Users.GetValueOrDefault(id);if(found!=null)return found;
            var user=new User{Id=id,Name=PlayerNickname.IsValid(name) ? name : "球员",SteamId=steamId};data.Users[id]=user;try{Save();}catch{data.Users.Remove(id);throw;}return user;}
    }
    internal void Rename(string id,string name)
    {name=PlayerNickname.Normalize(name);lock(gate){var user=data.Users[id];string old=user.Name;user.Name=name;try{Save();}catch{user.Name=old;throw;}}}
    internal Dictionary<string,int> Settle(string matchId,string?[] slots,int cyan,int amber)
    {
        lock(gate)
        {
            if(data.Settled.Contains(matchId))return new();
            var members=slots.Where(id=>id!=null).Select(id=>data.Users[id!]).ToArray();if(members.Length!=6)throw new InvalidOperationException("排位必须六名真人");
            double a=slots.Take(3).Average(id=>data.Users[id!].Rating),b=slots.Skip(3).Average(id=>data.Users[id!].Rating);
            double expected=1/(1+Math.Pow(10,(b-a)/400));double score=cyan==amber ? .5 : cyan>amber ? 1 : 0;
            int change=(int)Math.Round(24*(score-expected));var changes=new Dictionary<string,int>();
            foreach(var user in members){int delta=Array.IndexOf(slots,user.Id)<3 ? change : -change;changes[user.Id]=delta;user.Rating+=delta;user.RankedGames++;}
            data.Settled.Add(matchId);
            try{Save();}catch{foreach(var user in members){user.Rating-=changes[user.Id];user.RankedGames--;}data.Settled.Remove(matchId);throw;}
            return changes;
        }
    }
    private void Save()
    {Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temporary,JsonSerializer.Serialize(data,json));File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
}
