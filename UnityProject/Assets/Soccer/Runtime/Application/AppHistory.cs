using System;
using System.IO;
using Soccer.Core;

namespace Soccer.Runtime
{
    public sealed partial class AppController
    {
        public MatchHistory History {get;private set;}
        public string HistoryStatus {get;private set;}="";
        private string historyProfileKey;private MatchHistory sessionHistory;private bool historyRecorded;
        private MatchSource historySource;private AppPage historyOrigin;
        private void ReloadHistory(string key)
        {
            if(key==historyProfileKey && History!=null)return;historyProfileKey=key;HistoryStatus="";
            try{History=new MatchHistory(new MatchHistoryStore(Path.Combine(Path.GetDirectoryName(walletDirectory),"history"),key));}
            catch(Exception){History=null;HistoryStatus="对局记录暂时无法读取";}
        }
        private void BeginHistorySession()
        {
            sessionHistory=History;historyRecorded=false;
            historySource=ServiceMatch!=null ? MatchSource.Internet : Room==null ? MatchSource.AI : Room.Lobby.Route==Soccer.Core.Networking.NetworkRoute.LAN ? MatchSource.LAN : Room.Lobby.Route==Soccer.Core.Networking.NetworkRoute.Steam ? MatchSource.Steam : MatchSource.Internet;
        }
        private void RecordMatchHistory()
        {
            if(historyRecorded || Simulation?.State.Phase!=MatchPhase.Finished)return;
            historyRecorded=true;if(Simulation.State.Mode!=MatchMode.BotMatch || sessionHistory==null)return;
            try{sessionHistory.Add(MatchRecord.FromFinishedMatch(Simulation.State,rewardPlayerId,rewardMatchId,historySource,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),MatchCoins));SyncSocialProfile();}
            catch(Exception){HistoryStatus="本局记录暂时无法保存";}
        }
        public void OpenHistory()
        {
            if(!EnsureEntered())return;SettleMatchReward();ReloadWallet();historyOrigin=Page==AppPage.Results ? AppPage.Results : AppPage.Home;
            if(Page==AppPage.Profile)historyOrigin=AppPage.Profile;Page=AppPage.History;UI.ShowPage(Page);Audio.SetContext(Page,false);
        }
        public void BackFromHistory()
        {
            if(historyOrigin==AppPage.Results && Simulation!=null){Page=AppPage.Results;UI.ShowPage(Page);Audio.SetContext(Page,false);}else if(historyOrigin==AppPage.Profile)OpenProfile();else GoHome();
        }
    }
}
