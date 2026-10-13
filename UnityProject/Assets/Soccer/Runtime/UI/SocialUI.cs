using System;
using System.Linq;
using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed class SocialUI
    {
        private readonly AppController app;private readonly VisualElement root;private CharacterId? hero;
        public SocialUI(AppController app,VisualElement root)
        {
            this.app=app;this.root=root;
            Button("profile-back",app.BackFromProfile);Button("profile-friends",app.OpenFriends);Button("profile-history",app.OpenHistory);
            Button("profile-all",()=>{hero=null;RefreshProfile();});Button("friends-back",app.GoHome);Button("friends-login",app.OpenLogin);
            Button("friends-refresh",app.RefreshFriends);Button("friend-search",()=>app.SearchFriend(root.Q<TextField>("friend-account").value));
            root.Q<TextField>("friend-account").maxLength=16;
        }
        private void Button(string name,Action action){root.Q<Button>(name).clicked+=()=>{app.Audio.PlayClick();action();};}
        private Button ActionButton(string text,string name,Action action)
        {var button=new Button(()=>{app.Audio.PlayClick();action();}){text=text,name=name};button.AddToClassList("social-action");return button;}
        private void Show(string name,bool show)=>root.Q(name).EnableInClassList("hidden",!show);
        private void Text(string name,string value)=>root.Q<Label>(name).text=value;
        public void OpenProfile(){hero=null;RefreshProfile();}
        public void Refresh()
        {
            root.Q<Button>("home-friends").text=app.Friends.Incoming.Length>0 ? "好友 ("+app.Friends.Incoming.Length+")" : "好友";
            if(app.Page==AppPage.Profile)RefreshProfile();if(app.Page==AppPage.Friends)RefreshFriends();
        }
        private void RefreshProfile()
        {
            var data=app.VisibleProfile;Text("profile-name",data?.DisplayName ?? "玩家主页");Text("profile-id",data==null ? "" : data.AccountId.Length==0 ? "离线档案" : "帐号 ID  "+data.AccountId);
            Text("profile-status",app.ProfileStatus);Show("profile-friends",app.OwnProfile);Show("profile-history",app.OwnProfile);
            var heroes=root.Q("profile-heroes");heroes.Clear();var recent=root.Q<ScrollView>("profile-recent");recent.Clear();
            if(data==null){Text("profile-matches","—");Text("profile-winrate","—");Text("profile-goals","—");Show("profile-empty",false);return;}
            int matches=data.Heroes.Sum(h=>h.Matches),wins=data.Heroes.Sum(h=>h.Wins);
            Text("profile-matches",matches.ToString());Text("profile-winrate",(matches==0 ? 0 : 100f*wins/matches).ToString("0.#")+"%");Text("profile-goals",data.Heroes.Sum(h=>h.Goals).ToString());
            foreach(var stats in data.Heroes)
            {
                var id=stats.Character;var card=ActionButton("","profile-hero-"+id,()=>{hero=hero==id ? (CharacterId?)null : id;RefreshProfile();});card.AddToClassList("profile-hero");card.EnableInClassList("profile-hero-selected",hero==id);
                var portrait=new VisualElement();portrait.AddToClassList("profile-portrait");portrait.style.backgroundImage=Resources.Load<Texture2D>("FrontEnd/portrait-"+(id==CharacterId.Marine ? "marine" : "thor")+"-v2");card.Add(portrait);
                var content=new VisualElement();content.AddToClassList("grow");var name=new Label(HistoryUI.Hero(id));name.AddToClassList("profile-hero-name");content.Add(name);
                var summary=new Label(stats.Matches+" 场  ·  胜率 "+stats.WinRate.ToString("0.#")+"%");summary.AddToClassList("social-copy");content.Add(summary);
                var detail=new Label(stats.Wins+" 胜 / "+stats.Losses+" 负 / "+stats.Draws+" 平");detail.AddToClassList("social-muted");content.Add(detail);card.Add(content);heroes.Add(card);
            }
            Text("profile-recent-title",hero.HasValue ? HistoryUI.Hero(hero.Value)+" · 最近战绩" : "最近战绩");
            var records=data.Recent.Where(m=>!hero.HasValue || m.Character==hero.Value).ToArray();Show("profile-empty",records.Length==0);
            int index=0;foreach(var match in records)
            {
                var row=new VisualElement {name="profile-recent-"+(index++)};row.AddToClassList("profile-match");row.AddToClassList("row");
                var result=new Label(HistoryUI.Result(match.Result));result.AddToClassList("history-result");result.AddToClassList("history-"+match.Result.ToString().ToLowerInvariant());row.Add(result);
                var meta=new Label(HistoryUI.Hero(match.Character)+" · "+HistoryUI.Source(match.Source));meta.AddToClassList("grow");meta.AddToClassList("social-copy");row.Add(meta);
                var score=new Label(match.OwnScore+" : "+match.OpponentScore);score.AddToClassList("profile-score");row.Add(score);
                var date=new Label(DateTimeOffset.FromUnixTimeSeconds(match.FinishedUnixSeconds).ToLocalTime().ToString("MM-dd HH:mm"));date.AddToClassList("social-muted");row.Add(date);recent.Add(row);
            }
        }
        private void RefreshFriends()
        {
            bool online=app.SocialOnline;Show("friends-online",online);Show("friends-offline",!online);Text("friends-status",app.SocialStatus);
            root.Q<Button>("friend-search").SetEnabled(online && !app.FriendsBusy);root.Q<Button>("friends-refresh").SetEnabled(online && !app.FriendsBusy);
            var result=root.Q("friend-result");result.Clear();var person=app.SearchPerson;
            if(person!=null)
            {
                var row=PersonRow(person.AccountId,person.DisplayName,"search-profile");
                bool own=person.AccountId==app.Account.Id,friend=app.Friends.Friends.Any(p=>p.AccountId==person.AccountId),incoming=app.Friends.Incoming.Any(p=>p.AccountId==person.AccountId),pending=app.Friends.Outgoing.Any(p=>p.AccountId==person.AccountId);
                var add=ActionButton(own ? "你自己" : friend ? "已是好友" : incoming ? "接受申请" : pending ? "已申请" : "添加好友","friend-add",()=>app.FriendAction(person.AccountId,incoming ? "accept" : "request"));
                add.SetEnabled(!own && !friend && !pending && !app.FriendsBusy);row.Add(add);result.Add(row);
            }
            var friends=root.Q<ScrollView>("friends-list");friends.Clear();Text("friends-count","好友 · "+app.Friends.Friends.Length);Show("friends-empty",app.Friends.Friends.Length==0);
            foreach(var personData in app.Friends.Friends)
            {var p=personData;var row=PersonRow(p.AccountId,p.DisplayName,"friend-profile-"+p.AccountId);var remove=ActionButton("移除","friend-remove-"+p.AccountId,()=>app.FriendAction(p.AccountId,"remove"));remove.SetEnabled(!app.FriendsBusy);row.Add(remove);friends.Add(row);}
            var requests=root.Q<ScrollView>("friends-requests");requests.Clear();Text("friends-requests-title","申请 · "+app.Friends.Incoming.Length);
            foreach(var personData in app.Friends.Incoming)
            {var p=personData;var row=PersonRow(p.AccountId,p.DisplayName,"request-profile-"+p.AccountId);var accept=ActionButton("接受","friend-accept-"+p.AccountId,()=>app.FriendAction(p.AccountId,"accept"));var reject=ActionButton("拒绝","friend-reject-"+p.AccountId,()=>app.FriendAction(p.AccountId,"reject"));accept.SetEnabled(!app.FriendsBusy);reject.SetEnabled(!app.FriendsBusy);row.Add(accept);row.Add(reject);requests.Add(row);}
            foreach(var personData in app.Friends.Outgoing)
            {var p=personData;var row=PersonRow(p.AccountId,p.DisplayName,"outgoing-profile-"+p.AccountId);var label=new Label("等待通过");label.AddToClassList("social-muted");row.Add(label);var cancel=ActionButton("撤回","friend-cancel-"+p.AccountId,()=>app.FriendAction(p.AccountId,"remove"));cancel.SetEnabled(!app.FriendsBusy);row.Add(cancel);requests.Add(row);}
        }
        private VisualElement PersonRow(string account,string name,string buttonName)
        {
            var row=new VisualElement();row.AddToClassList("social-person");row.AddToClassList("row");
            var button=ActionButton(name+"\nID  "+account,buttonName,()=>app.OpenProfile(account));button.AddToClassList("social-person-name");button.AddToClassList("grow");row.Add(button);return row;
        }
    }
}
