using System;
using System.Linq;
using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed class CommunityUI
    {
        private readonly AppController app;private readonly VisualElement root;
        private string signature="";
        public CommunityUI(AppController app,VisualElement root)
        {
            this.app=app;this.root=root;
            Bind("party-back",app.GoHome);Bind("party-login",()=>{if(app.SteamEntry)app.UI.ShowCommunityLogin();else app.OpenLogin();});
            Bind("party-friends",app.OpenFriends);Bind("party-chat",()=>app.UI.Chat.Open("party"));
            Bind("party-ready",()=>Act("ready",ready:!(app.Community.Party?.Members.FirstOrDefault(m=>m.AccountId==app.SocialAccountId)?.Ready??false)));
            Bind("party-normal",()=>Act("queue",mode:"normal"));Bind("party-ranked",()=>Act("queue",mode:"ranked"));Bind("party-cancel",()=>Act("cancelQueue"));
            Bind("party-leave",()=>Act("leaveParty"));Bind("party-create-room",()=>Act("createRoom"));
            Bind("party-room-ready",()=>Act("roomReady",ready:!(app.Community.Room?.Slots.FirstOrDefault(m=>m!=null && m.AccountId==app.SocialAccountId)?.Ready??false)));
            Bind("party-room-leave",()=>Act("leaveRoom"));Bind("party-room-copy",()=>GUIUtility.systemCopyBuffer=app.Community.Room?.Code??"");
            Bind("party-room-chat",()=>app.UI.Chat.Open("room"));
            Bind("party-room-bots",()=>Act("roomBots",ready:true));Bind("party-room-clear",()=>Act("roomBots",ready:false));
            Bind("party-room-join",()=>app.CommunityCommand(new CommunityAction{Action="joinRoom",Code=root.Q<TextField>("party-room-code").value}));
            Bind("party-reconnect",app.ReconnectServiceMatch);
            var difficulty=root.Q<DropdownField>("party-room-difficulty");difficulty.choices=BotDifficulties.Names.ToList();difficulty.SetValueWithoutNotify(BotDifficulties.Names[1]);
            difficulty.RegisterValueChangedCallback(e=>{if(app.Community.Room!=null)Act("roomBots",ready:app.Community.Room.Bots.Length>0);});
        }
        private void Bind(string id,Action action){root.Q<Button>(id).clicked+=()=>{app.Audio.PlayClick();action();};}
        private void Act(string action,bool ready=false,string mode="")=>app.CommunityCommand(new CommunityAction{Action=action,Ready=ready,Mode=mode,Difficulty=(BotDifficulty)Math.Max(0,Array.IndexOf(BotDifficulties.Names,root.Q<DropdownField>("party-room-difficulty").value))});
        private void Show(string id,bool value)=>root.Q(id).EnableInClassList("hidden",!value);
        private void Text(string id,string text)=>root.Q<Label>(id).text=text;
        public void Refresh()
        {
            int invitations=app.Community.Invites.Length;
            root.Q<Button>("home-party").text=invitations>0 ? "组队邀请 ("+invitations+")" : "组队 / 匹配";
            root.Q<Button>("play-party").text=invitations>0 ? "收到组队 / 房间邀请 ("+invitations+")" : "好友组队 · 普通匹配 / 排位";
            if(app.Page!=AppPage.Party)return;
            var state=app.Community;var party=state.Party;var room=state.Room;bool online=app.SocialOnline,leader=party==null || party.LeaderId==app.SocialAccountId,queued=!string.IsNullOrEmpty(party?.Queue);
            Text("party-identity",online ? app.PlayerName+" · 游戏 ID "+app.SocialAccountId+" · 排位分 "+state.Rating+" / "+state.RankedGames+" 场" : "游戏好友、队伍与匹配需要游戏服务连接");
            Text("party-status",app.CommunityStatus);Show("party-login",!online);Show("party-room",room!=null);
            Show("party-queue-actions",room==null);Show("party-reconnect",state.Match!=null && state.Match.Phase!="已结束" && app.ServiceMatch==null);
            bool ready=party?.Members.FirstOrDefault(m=>m.AccountId==app.SocialAccountId)?.Ready??false;
            root.Q<Button>("party-ready").text=ready ? "取消准备" : "准备";
            foreach(var id in new[]{"party-normal","party-ranked","party-create-room"})root.Q(id).SetEnabled(online && leader && !queued && !app.CommunityBusy);
            root.Q("party-ready").SetEnabled(online && !queued && !app.CommunityBusy);root.Q("party-leave").SetEnabled(online && !queued && !app.CommunityBusy);
            Show("party-cancel",queued);root.Q("party-cancel").SetEnabled(leader && !app.CommunityBusy);
            Text("party-queue-status",queued ? "正在寻找"+(party.Queue=="ranked" ? "排位" : "普通")+"对局 · "+Math.Max(0,DateTimeOffset.UtcNow.ToUnixTimeSeconds()-party.QueuedAt)+" 秒 · 排队玩家 "+state.QueuedPlayers+"\n保持队员同队，满六名真人后开局。" : "最多三人组队；全员准备后由队长开始普通匹配或排位。排位由服务器运行与结算。");
            // JsonUtility.ToJson can materialize null inline slots on the source DTO.
            // Revisions already identify every server snapshot without serializing it again.
            var members=root.Q("party-members");string next=state.Revision+"|"+app.CommunityBusy;
            if(next!=signature)
            {
                signature=next;members.Clear();
                foreach(var person in party?.Members??new PartyMember[0])
                {
                    var row=new VisualElement();row.AddToClassList("party-member");row.Add(new Label(person.Name+(person.AccountId==party.LeaderId ? " · 队长" : "")+"\n"+(person.Online ? "在线" : "离线")+" · "+(person.Ready ? "已准备" : "未准备")+" · 排位 "+person.Rating));
                    if(person.AccountId!=app.SocialAccountId){string target=person.AccountId;var chat=new Button(()=>app.UI.Chat.OpenPrivate(target,person.Name)){text="私聊"};row.Add(chat);
                        if(leader){var kick=new Button(()=>app.CommunityCommand(new CommunityAction{Action="kick",Target=target})){text="移出"};kick.SetEnabled(!queued && !app.CommunityBusy);row.Add(kick);}}
                    members.Add(row);
                }
                var invites=root.Q<ScrollView>("party-invites");invites.Clear();
                foreach(var invite in state.Invites)
                {
                    var captured=invite;var row=new VisualElement();row.AddToClassList("party-invite");row.Add(new Label(invite.FromName+" 邀请你"+(invite.Kind=="room" ? "加入房间 "+invite.Code : "组队")));
                    var accept=new Button(()=>app.CommunityCommand(new CommunityAction{Action="accept",Id=captured.Id})){text="接受"};var reject=new Button(()=>app.CommunityCommand(new CommunityAction{Action="reject",Id=captured.Id})){text="拒绝"};accept.SetEnabled(!app.CommunityBusy);reject.SetEnabled(!app.CommunityBusy);row.Add(accept);row.Add(reject);invites.Add(row);
                }
                Show("party-invites-empty",state.Invites.Length==0);
            }
            if(room!=null)
            {
                Text("party-room-title","好友房间 · "+room.Code+(room.Countdown>0 ? " · "+Mathf.CeilToInt(room.Countdown)+" 秒后选人" : ""));
                root.Q<DropdownField>("party-room-difficulty").SetValueWithoutNotify(BotDifficulties.Names[(int)room.Difficulty]);
                root.Q("party-room-difficulty").SetEnabled(room.OwnerId==app.SocialAccountId && !app.CommunityBusy);
                root.Q("party-room-bots").SetEnabled(room.OwnerId==app.SocialAccountId && !app.CommunityBusy);root.Q("party-room-clear").SetEnabled(room.OwnerId==app.SocialAccountId && !app.CommunityBusy);
                var slots=root.Q("party-room-slots");slots.Clear();for(int i=0;i<6;i++){var person=room.Slots[i];var label=new Label((i<3 ? "青" : "橙")+(i%3+1)+" · "+(person==null ? room.Bots.Contains(i) ? BotDifficulties.Names[(int)room.Difficulty]+" AI" : "空位" : person.Name+" · "+(person.Ready ? "已准备" : "未准备")));label.AddToClassList("party-room-slot");slots.Add(label);}
                root.Q<Button>("party-room-ready").text=room.Slots.Any(m=>m!=null && m.AccountId==app.SocialAccountId && m.Ready) ? "取消准备" : "准备";
            }
        }
    }
}
