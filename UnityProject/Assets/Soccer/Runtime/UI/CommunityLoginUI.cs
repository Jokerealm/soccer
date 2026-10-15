using System;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed class CommunityLoginUI
    {
        private readonly AppController app;private readonly VisualElement modal;private readonly TextField account,password,name;
        private readonly Label status;private readonly Button submit,toggle;private bool register;
        public bool Visible {get;private set;}
        public CommunityLoginUI(AppController app,VisualElement root)
        {
            this.app=app;modal=new VisualElement{name="community-login-dialog"};modal.AddToClassList("community-login-dialog");
            var card=new VisualElement();card.AddToClassList("community-login-card");modal.Add(card);
            card.Add(new Label("连接游戏帐号"));card.Add(new Label("Steam 登录与邀请继续可用；游戏好友、私聊与组队使用此帐号。"));
            account=new TextField("帐号"){name="community-account",maxLength=16};password=new TextField("密码"){name="community-password",maxLength=64,isPasswordField=true};name=new TextField("游戏名"){name="community-name",maxLength=12};
            card.Add(account);card.Add(password);card.Add(name);name.style.display=DisplayStyle.None;
            status=new Label{name="community-login-status"};card.Add(status);
            submit=new Button(()=>{app.ConnectGameSocial(account.value,name.value,password.value,register);password.SetValueWithoutNotify("");}){name="community-login-submit",text="连接"};card.Add(submit);
            toggle=new Button(()=>{register=!register;name.style.display=register ? DisplayStyle.Flex : DisplayStyle.None;toggle.text=register ? "已有帐号，返回登录" : "创建新游戏帐号";submit.text=register ? "创建并连接" : "连接";}){name="community-login-register",text="创建新游戏帐号"};card.Add(toggle);
            card.Add(new Button(Hide){name="community-login-cancel",text="取消"});root.Q("app").Add(modal);Hide();
        }
        public void Show(){Visible=true;modal.style.display=DisplayStyle.Flex;status.text="";account.Focus();}
        public void Hide(){Visible=false;modal.style.display=DisplayStyle.None;password.SetValueWithoutNotify("");}
        public void Refresh(){if(!Visible)return;status.text=app.CommunityStatus;submit.SetEnabled(!app.CommunityBusy);toggle.SetEnabled(!app.CommunityBusy);}
    }
}
