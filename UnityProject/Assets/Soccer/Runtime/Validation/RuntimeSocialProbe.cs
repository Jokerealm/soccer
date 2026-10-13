using System.Collections;
using System.Linq;
using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed partial class RuntimeSmokeProbe
    {
        private IEnumerator SocialLogin(string account)
        {
            app.OpenLogin();app.SignInOnline(app.LoginServerAddress,account,account);
            float deadline=Time.realtimeSinceStartup+10;while((app.LoginBusy || app.Page!=AppPage.Home || app.FriendsBusy) && Time.realtimeSinceStartup<deadline)yield return null;
            Check(app.SocialOnline && app.Account.Id==account,"social player logs in as "+account);
        }
        private IEnumerator SocialWait()
        {float deadline=Time.realtimeSinceStartup+9;while(app.FriendsBusy && Time.realtimeSinceStartup<deadline)yield return null;yield return new WaitForSecondsRealtime(.15f);}
        private IEnumerator CaptureOfflineProfile()
        {
            app.GoHome();yield return Click("profile-button");
            Check(app.Page==AppPage.Profile && app.UI.Root.Q<Label>("profile-matches").text==app.History.Heroes.Sum(h=>h.Matches).ToString(),"own profile shows lifetime career rather than a truncated history count");
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                Screen.SetResolution(size.x,size.y,false);yield return new WaitForSecondsRealtime(.4f);
                Check(PicksButton("profile-back") && PicksButton("profile-history") && PicksButton("profile-hero-Marine") && PicksButton("profile-hero-Thor"),size+" profile controls are accessible");
                yield return Capture("profile-"+size.x+"x"+size.y);
            }
            yield return Click("profile-hero-Thor");Check(app.UI.Root.Q<Label>("profile-recent-title").text.Contains("雷神"),"hero card filters the recent matches");
            yield return Click("profile-history");Check(app.Page==AppPage.History,"profile opens detailed recent history");yield return Click("history-back");Check(app.Page==AppPage.Profile,"history returns to the originating personal profile");
            app.OpenFriends();yield return null;Check(HistoryVisible(app.UI.Root.Q("friends-offline")) && !HistoryVisible(app.UI.Root.Q("friends-online")),"offline social page honestly offers online login");yield return Capture("friends-offline");app.GoHome();Screen.SetResolution(1920,1080,false);yield return null;
        }
        private IEnumerator CaptureOnlineSocial()
        {
            yield return SocialLogin("a");HistoryFixture(2,1,CharacterId.Marine);app.GoHome();yield return Click("profile-button");
            float deadline=Time.realtimeSinceStartup+8;while(app.ProfileStatus.Length>0 && Time.realtimeSinceStartup<deadline)yield return null;
            Check(app.VisibleProfile.Heroes.Sum(h=>h.Matches)>0 && app.VisibleProfile.Recent.Any(m=>m.Result==MatchResult.Victory),"online profile includes settled match and hero career");
            app.OpenFriends();yield return SocialWait();var root=app.UI.Root;
            root.Q<TextField>("friend-account").value="a";yield return Click("friend-search");yield return SocialWait();Check(!root.Q<Button>("friend-add").enabledSelf,"self search does not allow adding yourself");
            root.Q<TextField>("friend-account").value="missing";yield return Click("friend-search");yield return SocialWait();Check(app.SearchPerson==null && app.SocialStatus.Contains("没有找到"),"unknown account search gives a useful empty result");
            root.Q<TextField>("friend-account").value="b";yield return Click("friend-search");yield return SocialWait();Check(app.SearchPerson?.AccountId=="b" && PicksButton("friend-add"),"search returns actual other account and add action");
            yield return Click("friend-add");yield return SocialWait();Check(app.Friends.Outgoing.Any(p=>p.AccountId=="b"),"adding a player sends an outgoing friend request");yield return Capture("friends-outgoing");
            yield return SocialLogin("b");app.OpenFriends();yield return SocialWait();Check(app.Friends.Incoming.Any(p=>p.AccountId=="a"),"other account receives request through the server");
            yield return Click("friend-accept-a");yield return SocialWait();Check(app.Friends.Friends.Any(p=>p.AccountId=="a") && app.Friends.Incoming.Length==0,"accepting request establishes friendship");
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {Screen.SetResolution(size.x,size.y,false);yield return new WaitForSecondsRealtime(.4f);Check(PicksButton("friend-profile-a") && PicksButton("friend-remove-a") && PicksButton("friends-back"),size+" friend list actions remain accessible");yield return Capture("friends-"+size.x+"x"+size.y);}
            yield return Click("friend-profile-a");deadline=Time.realtimeSinceStartup+8;while(app.VisibleProfile==null && Time.realtimeSinceStartup<deadline)yield return null;
            Check(app.Page==AppPage.Profile && !app.OwnProfile && app.VisibleProfile?.AccountId=="a" && app.VisibleProfile.Recent.Length>0,"friend profile exposes actual synchronized hero and recent match data");yield return Capture("friend-profile-a");
            yield return Click("profile-back");yield return SocialWait();yield return Click("friend-remove-a");yield return SocialWait();Check(app.Friends.Friends.Length==0,"friend can be removed in game");
            yield return SocialLogin("a");app.OpenFriends();yield return SocialWait();Check(app.Friends.Friends.Length==0,"removal is bilateral and remains after relogin");
            app.SearchFriend("b");yield return SocialWait();yield return Click("friend-add");yield return SocialWait();
            yield return SocialLogin("b");app.OpenFriends();yield return SocialWait();yield return Click("friend-reject-a");yield return SocialWait();Check(app.Friends.Incoming.Length==0 && app.Friends.Friends.Length==0,"incoming request can be rejected without creating friendship");
            app.OpenLogin();app.EnterOffline();app.GoHome();
        }
    }
}
