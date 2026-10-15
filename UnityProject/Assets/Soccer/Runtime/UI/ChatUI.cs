using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Soccer.Core;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed class ChatUI
    {
        private readonly AppController app;private readonly VisualElement root,panel;
        private readonly ScrollView list;private readonly TextField input;private readonly DropdownField channel;
        private readonly Label status;private readonly Button badge;
        private readonly Dictionary<string,List<ChatMessage>> logs=new Dictionary<string,List<ChatMessage>>();
        private readonly Dictionary<string,long> read=new Dictionary<string,long>();
        private string scope="party",target="",privateName="",context="",signature="";
        private int enterFrame=-1;
        private float nextPoll,nextSend;private bool polling,sending,pendingFocus,consumed;
        public bool IsOpen {get;private set;}
        public bool BlocksGameplay=>consumed || IsOpen && (pendingFocus || input.panel?.focusController?.focusedElement is TextField || input.Contains(input.panel?.focusController?.focusedElement as VisualElement));
        public ChatUI(AppController app,VisualElement root)
        {
            this.app=app;this.root=root;var host=root.Q("app");
            badge=new Button(()=>{
                if(IsOpen){Close();return;}
                var unread=app.Community.Inbox.FirstOrDefault(m=>!read.TryGetValue("private:"+m.SenderId,out long seen) || seen<m.Id);
                if(unread!=null)OpenPrivate(unread.SenderId,unread.SenderName);else Open(DefaultScope());
            }){name="chat-toggle",text="聊天  Enter"};badge.AddToClassList("chat-toggle");host.Add(badge);
            panel=new VisualElement{name="chat-panel"};panel.AddToClassList("chat-panel");host.Add(panel);
            var heading=new VisualElement();heading.AddToClassList("row");channel=new DropdownField{name="chat-channel"};channel.AddToClassList("grow");heading.Add(channel);
            var close=new Button(Close){name="chat-close",text="×"};heading.Add(close);panel.Add(heading);
            list=new ScrollView{name="chat-messages"};list.AddToClassList("chat-messages");panel.Add(list);
            status=new Label{name="chat-status"};status.AddToClassList("chat-status");panel.Add(status);
            var line=new VisualElement();line.AddToClassList("row");input=new TextField{name="chat-input",maxLength=ChatText.MaxLength};input.AddToClassList("grow");line.Add(input);
            var send=new Button(Send){name="chat-send",text="发送"};line.Add(send);panel.Add(line);
            input.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Return || e.keyCode==KeyCode.KeypadEnter || e.keyCode==KeyCode.Escape)e.StopPropagation();});
            channel.RegisterValueChangedCallback(e=>{scope=FromLabel(e.newValue);Switch();});Close();
            foreach(var field in new VisualElement[]{channel,input}){field.style.minWidth=0;field.style.flexBasis=0;field.style.flexShrink=1;}
        }
        public void Reset(){Close();logs.Clear();read.Clear();signature="";context="";polling=sending=false;nextPoll=nextSend=0;}
        public void OpenPrivate(string id,string name){target=id;privateName=name;Open("private");}
        public void Open(string selected)
        {
            scope=selected;IsOpen=true;panel.style.display=DisplayStyle.Flex;SetChoices();Switch();pendingFocus=true;input.schedule.Execute(()=>{if(IsOpen){input.Focus();pendingFocus=false;}});
        }
        public void Close(){IsOpen=false;pendingFocus=false;panel.style.display=DisplayStyle.None;input?.Blur();}
        private string DefaultScope()=>app.IsServiceMatch || app.IsNetworkMatch ? "team" : app.Room!=null || app.Community.Room!=null ? "room" : "party";
        private static string Label(string value)=>value=="private" ? "私聊" : value=="party" ? "队伍" : value=="room" ? "房间" : value=="team" ? "队内" : "全场";
        private static string FromLabel(string value)=>value.StartsWith("私聊") ? "private" : value=="队伍" ? "party" : value=="房间" ? "room" : value=="队内" ? "team" : "all";
        private void SetChoices()
        {
            var choices=new List<string>();if(target.Length>0)choices.Add("私聊 · "+privateName);
            if(app.SocialOnline && app.Community.Party!=null)choices.Add("队伍");
            if(app.Room!=null || app.Community.Room!=null)choices.Add("房间");
            if(app.IsServiceMatch || app.IsNetworkMatch){choices.Add("队内");choices.Add("全场");}
            if(choices.Count==0)choices.Add(target.Length>0 ? "私聊 · "+privateName : "队伍");
            channel.choices=choices;string wanted=scope=="private" ? "私聊 · "+privateName : Label(scope);
            if(!choices.Contains(wanted)){wanted=choices[0];scope=FromLabel(wanted);}channel.SetValueWithoutNotify(wanted);
        }
        private string Context()
        {
            if(scope=="private")return "private:"+target;
            if(scope=="party")return "party:"+(app.Community.Party?.Id??"");
            if(app.ServiceMatch!=null)return "match:"+app.ServiceMatch.Assignment.Id+"|"+scope;
            if(app.Room!=null)return "room:"+app.Room.Lobby.Id+"|"+scope;
            return "service-room:"+(app.Community.Room?.Code??"");
        }
        private void Switch(){context=Context();signature="";nextPoll=0;status.text="";Render();}
        public bool HandleEnter(bool all=false)
        {
            if(app.EntryMode==GameEntryMode.NotEntered || app.Page==AppPage.Loading || app.Page==AppPage.Login || app.Page==AppPage.Nickname)return false;
            if(enterFrame==Time.frameCount)return true;
            var focused=root.panel?.focusController?.focusedElement as VisualElement;
            for(var field=focused;field!=null;field=field.parent)
                if(field is TextField && field!=input)return false;
            if(!IsOpen && app.UI.BlocksKeyNavigation)return false;
            enterFrame=Time.frameCount;consumed=true;
            if(!IsOpen)Open(all && app.Page==AppPage.Match ? "all" : DefaultScope());
            else if(Input.compositionString.Length==0){if(string.IsNullOrWhiteSpace(input.value))Close();else Send();}
            return true;
        }
        public void Tick()
        {
            consumed=enterFrame==Time.frameCount;
            bool visible=app.EntryMode!=GameEntryMode.NotEntered && app.Page!=AppPage.Loading && app.Page!=AppPage.Login && app.Page!=AppPage.Nickname;
            badge.EnableInClassList("chat-in-match",app.Page==AppPage.Match);
            badge.style.display=visible ? DisplayStyle.Flex : DisplayStyle.None;if(!visible){Close();return;}
            bool enter=Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            if(IsOpen && Input.GetKeyDown(KeyCode.Escape)){consumed=true;Close();return;}
            if(enter)HandleEnter(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            if(!IsOpen)return;
            SetChoices();if(context!=Context())Switch();Render();
            bool service=scope=="private" || scope=="party" || scope=="room" && app.Room==null;
            if(service && app.SocialOnline && !polling && Time.realtimeSinceStartup>=nextPoll)app.StartChatPoll(scope,target,context,logs.TryGetValue(context,out var history) ? history.LastOrDefault()?.Id??0 : 0);
        }
        public void Send()
        {
            if(sending || Time.realtimeSinceStartup<nextSend)return;
            string text;try{text=ChatText.Normalize(input.value);}catch(ArgumentException e){status.text=e.Message;return;}
            bool network=scope=="room" || scope=="team" || scope=="all";
            if(network && app.ServiceMatch!=null && (scope=="team" || scope=="all"))
            {if(app.ServiceMatch.SendChat(scope,text)){input.SetValueWithoutNotify("");nextSend=Time.realtimeSinceStartup+1;}return;}
            if(network && app.Room!=null)
            {if(app.Room.SendChat(scope,text)){input.SetValueWithoutNotify("");nextSend=Time.realtimeSinceStartup+1;}else status.text="发送太快或频道不可用";return;}
            if(!app.SocialOnline){status.text="请先连接游戏好友服务";return;}
            sending=true;app.StartChatSend(new ChatRequest{Channel=scope,Target=target,Text=text},context);
        }
        public void Receive(ChatMessage message,string source)
        {Add(source+"|"+message.Channel,message);if(IsOpen)Render();}
        private void Add(string key,ChatMessage message)
        {
            if(!logs.TryGetValue(key,out var history)){history=new List<ChatMessage>();logs[key]=history;}
            if(history.Any(m=>m.Id==message.Id))return;history.Add(message);history.Sort((a,b)=>a.Id.CompareTo(b.Id));if(history.Count>100)history.RemoveRange(0,history.Count-100);
            if(IsOpen && context==key)read[key]=message.Id;
        }
        internal void PollStarted(){polling=true;}
        internal void Polled(string key,ChatHistory history,string error)
        {polling=false;nextPoll=Time.realtimeSinceStartup+1;foreach(var message in history?.Messages??new ChatMessage[0])Add(key,message);if(context==key && error.Length>0)status.text=error;Render();}
        internal void Sent(string key,ChatMessage message,string error)
        {sending=false;if(message!=null){Add(key,message);if(input.value==message.Text)input.SetValueWithoutNotify("");nextSend=Time.realtimeSinceStartup+.7f;}status.text=error;Render();}
        public void RefreshInbox()
        {
            int unread=app.Community.Inbox.Count(m=>!read.TryGetValue("private:"+m.SenderId,out long seen) || seen<m.Id);
            badge.text=unread>0 ? "聊天 ("+unread+")" : "聊天  Enter";
        }
        private void Render()
        {
            if(!IsOpen)return;logs.TryGetValue(context,out var history);history=history??new List<ChatMessage>();
            if(history.Count>0)read[context]=history[history.Count-1].Id;
            string next=context+":"+history.Count+":"+(history.LastOrDefault()?.Id??0);if(next==signature)return;signature=next;list.Clear();
            foreach(var message in history)
            {var label=new Label(message.SenderName+"："+message.Text){enableRichText=false};label.AddToClassList("chat-message");list.Add(label);}
            if(history.Count==0)list.Add(new Label("还没有消息。Enter 发送，Esc 收起。"));
            if(list.childCount>0){var last=list.ElementAt(list.childCount-1);list.schedule.Execute(()=>{if(IsOpen)list.ScrollTo(last);});}
        }
    }
}
