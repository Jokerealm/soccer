using System;
using System.IO;
using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    public sealed partial class AppController
    {
        public VictoryWallet Wallet { get; private set; }
        public string WalletStatus { get; private set; }="";
        public string WalletProfile => EntryMode==GameEntryMode.Online && Account!=null ? "帐号 "+Account.Id : "离线玩家";
        public int MatchCoins { get; private set; }
        public string MatchRewardText { get; private set; }="";
        private string walletDirectory,walletProfileKey,rewardMatchId;
        private VictoryWallet rewardWallet;
        private bool rewardSettled;
        private int rewardPlayerId;
        private void InitializeEconomy()
        {
            walletDirectory=Path.Combine(Application.persistentDataPath,"wallets");
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--soccer-report-dir");
            if(index>=0 && index+1<args.Length && (Array.IndexOf(args,"--soccer-smoke")>=0 || Array.IndexOf(args,"--soccer-network-smoke")>=0))
                walletDirectory=Path.Combine(args[index+1],"wallets");
            ReloadWallet();
        }
        private void ReloadWallet()
        {
            string key=EntryMode==GameEntryMode.Online && Account!=null ? "account:"+Account.Id : "offline";
            ReloadHistory(key);
            if(key==walletProfileKey && Wallet!=null)return;
            walletProfileKey=key;WalletStatus="";
            try {Wallet=new VictoryWallet(new WalletStore(walletDirectory,key));}
            catch(Exception) {Wallet=null;WalletStatus="金币存档暂时无法读取";}
        }
        public void OpenShop()
        {
            if(!EnsureEntered())return;
            ReloadWallet();Page=AppPage.Shop;UI.ShowPage(Page);Audio.SetContext(Page,false);
        }
        private void BeginRewardSession(string matchId=null)
        {
            ReloadWallet();rewardWallet=Wallet;rewardMatchId=matchId ?? "local:"+Guid.NewGuid().ToString("N");rewardPlayerId=LocalPlayerId;
            rewardSettled=false;MatchCoins=0;MatchRewardText="";BeginHistorySession();
        }
        private void SettleMatchReward() {AwardMatchCoins();RecordMatchHistory();}
        private void AwardMatchCoins()
        {
            if(rewardSettled || Simulation?.State.Phase!=MatchPhase.Finished)return;
            rewardSettled=true;var state=Simulation.State;
            bool victory=VictoryWallet.IsVictory(state,rewardPlayerId);
            MatchRewardText=state.Mode==MatchMode.Training ? "练习不计胜利奖励" : victory ? "本局奖励已领取" : "本局未获得胜利金币";
            if(!victory)return;
            if(rewardWallet==null){MatchRewardText="金币存档暂时无法读取，奖励未保存";return;}
            try
            {
                MatchCoins=rewardWallet.AwardVictory(rewardMatchId,state,rewardPlayerId);
                if(MatchCoins>0)MatchRewardText="胜利奖励  +"+MatchCoins+" 金币";
            }
            catch(Exception) {MatchRewardText="胜利奖励保存失败";}
        }
    }
}
