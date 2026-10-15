using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed class LoginUI
    {
        private readonly AppController app;private bool registering;
        private readonly VisualElement root;
        private TextField Field(string name) => root.Q<TextField>(name);
        public LoginUI(AppController controller, VisualElement ui)
        {
            app = controller; root = ui;
            Field("login-name").maxLength = 16; Field("login-password").maxLength = 64; Field("login-password").isPasswordField = true;
            root.Q<Button>("login-submit").clicked += () =>
            {
                app.Audio.PlayClick();if(registering)app.RegisterAccount(Field("login-name").value,Field("register-name").value,Field("login-password").value);else app.SignInOnline(app.LoginServerAddress, Field("login-name").value, Field("login-password").value);
                Field("login-password").SetValueWithoutNotify("");
            };
            root.Q<Button>("register-toggle").clicked+=()=>{registering=!registering;root.Q("register-name").EnableInClassList("hidden",!registering);root.Q<Button>("register-toggle").text=registering ? "已有帐号，返回登录" : "没有游戏帐号？注册";Refresh();};
            Field("register-name").maxLength=12;
            root.Q<Button>("login-offline").clicked += () => { app.Audio.PlayClick(); Field("login-password").SetValueWithoutNotify(""); app.EnterOffline(); };
        }
        public void Refresh()
        {
            root.Q<Label>("login-status").text = app.LoginStatus;
            foreach (string name in new[] { "login-name", "login-password", "login-submit", "login-steam", "register-toggle", "register-name" }) root.Q(name).SetEnabled(!app.LoginBusy);
            root.Q<Button>("login-submit").text = app.LoginBusy ? "处理中…" : registering ? "创建游戏帐号    →" : "联网登录    →";
            root.Q<Label>("connection-status").text = app.SteamEntry ? "● Steam · "+app.PlayerName : app.EntryMode == GameEntryMode.Online ? "● 已登录 · " + app.PlayerName : "● 离线 · 局域网可用";
            root.Q<Button>("home-account").text = app.EntryMode == GameEntryMode.Online || app.SteamEntry ? "切换帐号" : "联网登录";
        }
    }
}
