using System;
using System.Collections.Generic;

namespace Soccer.Core
{
    [Serializable]
    public sealed class WalletData
    {
        public int Version = 1;
        public long Coins;
        public List<string> RewardedMatches = new List<string>();
    }
    public interface IWalletStore
    {
        WalletData Load();
        void Save(WalletData data);
    }
    /// <summary>Local prototype wallet. Persist first, then commit; each completed winning match pays once.</summary>
    public sealed class VictoryWallet
    {
        public const int CoinsPerVictory = 100;
        private readonly IWalletStore store;
        private WalletData data;
        public long Coins => data.Coins;
        public VictoryWallet(IWalletStore storage)
        { store=storage ?? throw new ArgumentNullException(nameof(storage));data=store.Load() ?? new WalletData();Validate(data); }
        public static void Validate(WalletData value)
        {
            if(value==null || value.Version!=1 || value.Coins<0 || value.RewardedMatches==null || value.RewardedMatches.Count>100000)
                throw new ArgumentException("金币存档无效");
            var unique=new HashSet<string>(StringComparer.Ordinal);
            foreach(string id in value.RewardedMatches)
                if(string.IsNullOrWhiteSpace(id) || id.Length>128 || !unique.Add(id))throw new ArgumentException("奖励记录无效");
        }
        public static bool IsVictory(MatchState state,int playerId)
        {
            if(state==null || state.Mode!=MatchMode.BotMatch || state.Phase!=MatchPhase.Finished || playerId<0 || playerId>=state.Players.Count)return false;
            return state.Players[playerId].Team==Team.Cyan ? state.CyanScore>state.AmberScore : state.AmberScore>state.CyanScore;
        }
        public int AwardVictory(string matchId,MatchState state,int playerId)
        {
            if(!IsVictory(state,playerId))return 0;
            if(string.IsNullOrWhiteSpace(matchId) || matchId.Length>128)throw new ArgumentException("比赛奖励编号无效");
            if(data.RewardedMatches.Contains(matchId))return 0;
            var next=new WalletData {Coins=checked(data.Coins+CoinsPerVictory),RewardedMatches=new List<string>(data.RewardedMatches)};
            next.RewardedMatches.Add(matchId);Validate(next);store.Save(next);data=next;return CoinsPerVictory;
        }
    }
}
