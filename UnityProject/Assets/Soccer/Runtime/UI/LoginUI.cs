using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed class LoginUI
    {
        private readonly AppController app;
        private readonly VisualElement root;
        private TextField Field(string name) => root.Q<TextField>(name);
        public LoginUI(AppController controller, VisualElement ui)
        {
            app = controller; root = ui;
            Field("login-name").maxLength = 16; Field("login-password").maxLength = 64; Field("login-password").isPasswordField = true;
            root.Q<Button>("login-submit").clicked += () =>
            {
                app.Audio.PlayClick(); app.SignInOnline(app.LoginServerAddress, Field("login-name").value, Field("login-password").value);
                Field("login-password").SetValueWithoutNotify("");
            };
            root.Q<Button>("login-offline").clicked += () => { app.Audio.PlayClick(); Field("login-password").SetValueWithoutNotify(""); app.EnterOffline(); };
        }
        public void Refresh()
        {
            root.Q<Label>("login-status").text = app.LoginStatus;
            foreach (string name in new[] { "login-name", "login-password", "login-submit" }) root.Q(name).SetEnabled(!app.LoginBusy);
            root.Q<Button>("login-submit").text = app.LoginBusy ? "登录中…" : "联网登录    →";
            root.Q<Label>("connection-status").text = app.EntryMode == GameEntryMode.Online ? "● 已登录 · 帐号 " + app.Account?.Id : "● 离线 · 局域网可用";
            root.Q<Button>("home-account").text = app.EntryMode == GameEntryMode.Online ? "切换帐号" : "联网登录";
        }
    }
}
