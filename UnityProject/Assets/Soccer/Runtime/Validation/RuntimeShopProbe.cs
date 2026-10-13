using System;
using System.Collections;
using System.IO;
using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed partial class RuntimeSmokeProbe
    {
        private void NavigationMove(Vector2 direction)
        {using(var evt=NavigationMoveEvent.GetPooled(direction)){evt.target=app.UI.Root;app.UI.Root.SendEvent(evt);}}
        private bool FocusIs(string name)=>app.UI.Root.panel.focusController.focusedElement==app.UI.Root.Q<Button>(name);
        private IEnumerator CaptureShopAndNavigation()
        {
            int width=Screen.width,height=Screen.height;var mode=Screen.fullScreenMode;
            app.GoHome();yield return new WaitForSecondsRealtime(.4f);app.UI.Root.Focus();yield return null;
            NavigationMove(Vector2.down);Check(FocusIs("bots-button"),"native navigation recovers home selection when root has focus");yield return null;
            string[] names={"bots-button","normal-mode","train-button","shop-button","settings-button"};
            for(int i=1;i<names.Length;i++){NavigationMove(Vector2.down);Check(FocusIs(names[i]),"native down selects home action: "+names[i]);yield return null;}
            NavigationMove(Vector2.down);Check(FocusIs("bots-button"),"native navigation wraps down from last home action");yield return null;
            NavigationMove(Vector2.up);Check(FocusIs("settings-button"),"native navigation wraps up from first home action");yield return null;
            var input=new ProbeInput();app.UI.MenuInputOverride=input;app.UI.Root.Focus();yield return null;
            input.Pressed=KeyCode.DownArrow;yield return null;input.Clear();
            Check(FocusIs("bots-button"),"legacy input fallback restores selection without an event target");yield return null;
            input.Pressed=KeyCode.DownArrow;app.UI.SendMessage("Update");NavigationMove(Vector2.down);input.Clear();
            Check(FocusIs("normal-mode"),"legacy and native events in one frame advance only once");yield return null;
            input.Pressed=KeyCode.UpArrow;yield return null;input.Clear();Check(FocusIs("bots-button"),"legacy Up moves selection back");yield return null;
            input.Pressed=KeyCode.Return;yield return null;input.Clear();
            Check(app.Page==AppPage.PlayMenu,"legacy Enter opens the selected primary action");app.UI.MenuInputOverride=null;
            yield return new WaitForSecondsRealtime(.35f);NavigationMove(Vector2.down);yield return null;
            Check(FocusIs("play-ai"),"mode selection also supports native directional navigation");
            NavigationMove(Vector2.down);yield return null;Check(FocusIs("play-tutorial"),"mode Down reaches tutorial");
            NavigationMove(Vector2.up);yield return null;Check(FocusIs("play-ai"),"mode Up returns to AI option");
            using(var evt=KeyDownEvent.GetPooled('\0',KeyCode.Return,EventModifiers.None)){evt.target=app.UI.Root;app.UI.Root.SendEvent(evt);}
            yield return null;Check(app.InDraft,"native KeyDown Enter dispatches a queued button submit exactly once");app.BackFromSelection();
            app.GoHome();yield return new WaitForSecondsRealtime(.35f);
            yield return Click("shop-button");Check(app.Page==AppPage.Shop && app.Wallet!=null,"mouse shop entry opens the current player's actual wallet");
            Check(app.UI.Root.Q<Label>("shop-coins").text==app.Wallet.Coins.ToString("N0") && app.UI.Root.Q<Label>("shop-profile").text=="离线玩家","shop balance and profile reflect persisted offline wallet");
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                Screen.SetResolution(size.x,size.y,false);yield return new WaitForSecondsRealtime(.5f);
                foreach(string name in new[]{"shop-back","shop-heroes-tab","shop-skins-tab"})Check(PicksButton(name),size+" shop action receives pointer: "+name);
                var rect=app.UI.Root.Q("shop-heroes").worldBound;var bounds=app.UI.Root.worldBound;
                Check(bounds.Contains(rect.min) && bounds.Contains(rect.max),size+" hero cards fit the shop viewport");
                yield return Capture("shop-heroes-"+size.x+"x"+size.y);
                yield return Click("shop-skins-tab");Check(app.UI.Root.Q("shop-skins").resolvedStyle.display!=DisplayStyle.None && app.UI.Root.Q("shop-heroes").resolvedStyle.display==DisplayStyle.None,"skin tab shows an upcoming category without an unavailable purchase");
                yield return Capture("shop-skins-"+size.x+"x"+size.y);yield return Click("shop-heroes-tab");
                yield return Click("shop-back");yield return new WaitForSecondsRealtime(.35f);
                foreach(string name in names)Check(PicksButton(name),size+" five home actions remain accessible: "+name);
                yield return Capture("shop-home-"+size.x+"x"+size.y);yield return Click("shop-button");
            }
            app.GoHome();long before=app.Wallet.Coins;
            app.BeginLocalDraft();app.BackFromSelection();Check(app.Wallet.Coins==before,"abandoning draft does not award victory coins");
            app.OpenSelection(MatchMode.BotMatch);app.StartMatch();app.GoHome();Check(app.Wallet.Coins==before,"abandoning active game does not award victory coins");
            app.OpenSelection(MatchMode.BotMatch);app.StartMatch();app.SetPaused(true);
            var state=app.Simulation.State;typeof(MatchState).GetProperty(nameof(MatchState.CyanScore)).SetValue(state,2);typeof(MatchState).GetProperty(nameof(MatchState.AmberScore)).SetValue(state,1);
            app.EndSession();yield return new WaitForSecondsRealtime(.4f);
            Check(app.Page==AppPage.Results && app.MatchCoins==100 && app.Wallet.Coins==before+100,"completed AI victory pays exactly 100 coins to the winning local player");
            Check(app.UI.Root.Q<Label>("result-reward").text.Contains("+100") && app.UI.Root.Q<Label>("result-coins").text.Contains(app.Wallet.Coins.ToString("N0")),"results show earned coins and updated balance");
            yield return Capture("shop-victory-reward");app.EndSession();app.GoHome();
            Check(app.Wallet.Coins==before+100,"reopening results and returning home do not pay twice");
            Check(new VictoryWallet(new WalletStore(Path.Combine(output,"wallets"),"offline")).Coins==before+100,"actual JSON wallet reload preserves victory coins");
            app.OpenSelection(MatchMode.BotMatch);app.StartMatch();app.SetPaused(true);
            typeof(MatchState).GetProperty(nameof(MatchState.AmberScore)).SetValue(app.Simulation.State,1);app.EndSession();
            Check(app.MatchCoins==0 && app.Wallet.Coins==before+100,"AI defeat awards no coins");app.GoHome();
            app.OpenSelection(MatchMode.BotMatch);app.StartMatch();app.SetPaused(true);app.EndSession();
            Check(app.MatchCoins==0 && app.Wallet.Coins==before+100,"AI draw awards no coins");app.GoHome();
            app.StartTutorial();typeof(MatchState).GetProperty(nameof(MatchState.CyanScore)).SetValue(app.Simulation.State,10);app.EndSession();
            Check(app.MatchCoins==0 && app.Wallet.Coins==before+100,"tutorial remains ineligible for victory currency despite a fixture score");app.GoHome();
            var otherStore=new WalletStore(Path.Combine(output,"wallets"),"account:a");var otherWallet=new VictoryWallet(otherStore);long otherBefore=otherWallet.Coins;
            app.OpenSelection(MatchMode.BotMatch);app.StartMatch();app.SetPaused(true);typeof(MatchState).GetProperty(nameof(MatchState.CyanScore)).SetValue(app.Simulation.State,1);app.Simulation.Finish();
            otherWallet.AwardVictory("probe:"+Guid.NewGuid().ToString("N"),app.Simulation.State,0);
            Check(app.Wallet.Coins==before+100 && new VictoryWallet(otherStore).Coins==otherBefore+100 && new VictoryWallet(new WalletStore(Path.Combine(output,"wallets"),"account:b")).Coins==0,"account A, account B and offline balances use separate persisted profiles");
            app.EndSession();app.GoHome();Check(app.Wallet.Coins==before+200,"a subsequent victory pays again and atomically replaces the existing wallet file");
            yield return Click("shop-button");Check(app.UI.Root.Q<Label>("shop-coins").text==app.Wallet.Coins.ToString("N0"),"shop immediately reflects all settled victories");
            yield return Capture("shop-earned-balance");app.GoHome();Screen.SetResolution(width,height,mode);yield return new WaitForSecondsRealtime(.5f);
        }
    }
}
