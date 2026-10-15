using System;

namespace Soccer.Core.Networking
{
    public static class SteamLaunch
    {
        public static uint AppId(string[] args,string steamAppId)
        {
            int at=Array.IndexOf(args,"--soccer-steam-app-id");
            if(at>=0){if(at+1>=args.Length || !uint.TryParse(args[at+1],out uint explicitId) || explicitId==0)throw new ArgumentException("Steam AppID 配置无效");return explicitId;}
            return uint.TryParse(steamAppId,out uint id) && id>0 ? id : 480;
        }
        public static ulong Invitation(string[] args)
        {
            int at=Array.IndexOf(args,"+connect_lobby");
            return at>=0 && at+1<args.Length && ulong.TryParse(args[at+1],out ulong id) ? id : 0;
        }
        public static bool FromClient(string steamAppId,string steamGameId)
            =>uint.TryParse(steamAppId,out uint id) && id>0 && ulong.TryParse(steamGameId,out ulong game) && game==id;
    }
}
