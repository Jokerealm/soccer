using System;
using System.Collections;
using System.IO;
using System.Text;
using Soccer.Core.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace Soccer.Runtime
{
    public enum GameEntryMode { NotEntered, Offline, Online, Steam }
    public sealed partial class AppController
    {
        public GameEntryMode EntryMode { get; private set; }
        public bool LoginBusy { get; private set; }
        public string LoginStatus { get; private set; } = "";
        public string LoginServerAddress { get; private set; } = "";
        private static bool ValidServiceToken(string token)
        {
            if (token == null || token.Length != 64) return false;
            foreach (char c in token) if (!(c >= '0' && c <= '9' || c >= 'A' && c <= 'F')) return false;
            return true;
        }
        private Coroutine loginRoutine;
        private string serviceAccessToken = "";
        [Serializable] private sealed class LoginRequest { public string account, password; }
        [Serializable] private sealed class LoginResponse { public string accountId, service, accessToken; }
        [Serializable] private sealed class ServiceEndpoint {public string ServiceUrl;}
        private void InitializeLoginAddress()
        {
            LoginServerAddress = PlayerPrefs.GetString("soccer.loginServer", "");
            try
            {
                string path=Path.Combine(Application.streamingAssetsPath,"game-service.json");
                if(File.Exists(path))
                {
                    string address=JsonUtility.FromJson<ServiceEndpoint>(File.ReadAllText(path))?.ServiceUrl?.Trim().TrimEnd('/');
                    if(Uri.TryCreate(address,UriKind.Absolute,out var endpoint) && (endpoint.Scheme=="http" || endpoint.Scheme=="https") && endpoint.UserInfo.Length==0 && endpoint.Query.Length==0 && endpoint.Fragment.Length==0)LoginServerAddress=address;
                }
            }
            catch(Exception e) when(e is IOException || e is ArgumentException || e is UnauthorizedAccessException){Debug.LogWarning("游戏服务配置暂时无法读取");}
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--soccer-auth-url");
            if (index >= 0 && index + 1 < args.Length) LoginServerAddress = args[index + 1];
        }
        public void OpenLogin()
        {
            if (loginRoutine != null) StopCoroutine(loginRoutine); loginRoutine = null; LoginBusy = false;
            CloseNetworking(); Simulation = null; localInput?.Reset(); View.Clear(); Paused = false;
            Account?.Dispose(); Account = null; serviceAccessToken = ""; EntryMode = GameEntryMode.NotEntered;
            ResetSocial();
            Page = AppPage.Login; UI.ShowPage(Page); Audio.SetContext(Page, false);
        }
        public void EnterOffline()
        {
            if (!StartupReady) return;
            if (loginRoutine != null) StopCoroutine(loginRoutine); loginRoutine = null; LoginBusy = false;
            Account?.Dispose(); Account = null; serviceAccessToken = ""; EntryMode = GameEntryMode.Offline; Route = ConnectionRoute.LAN;
            ResetSocial();
            LoginStatus = "";ReloadWallet(); GoHome();
        }
        public void SignInOnline(string serverAddress, string account, string password)
        {
            if (LoginBusy || !StartupReady || Page != AppPage.Login) return;
            string address = (serverAddress ?? "").Trim().TrimEnd('/');
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https") ||
                uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || address.Length > 512)
            { LoginStatus = "联网登录服务暂未开放，请选择离线进入"; UI.RefreshLogin(); return; }
            string id = (account ?? "").Trim();
            if (id.Length < 1 || id.Length > 16 || password == null || password.Length < 1 || password.Length > 64)
            { LoginStatus = "请输入帐号和密码"; UI.RefreshLogin(); return; }
            LoginServerAddress = address;
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--soccer-smoke") < 0 && Array.IndexOf(args, "--soccer-network-smoke") < 0) PlayerPrefs.SetString("soccer.loginServer", address);
            LoginBusy = true; LoginStatus = "正在连接登录服务器…"; UI.RefreshLogin();
            loginRoutine = StartCoroutine(RequestLogin(address, id, password));
        }
        private IEnumerator RequestLogin(string address, string id, string password)
        {
            var payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new LoginRequest { account = id, password = password }));
            using (var request = new UnityWebRequest(address + "/api/login", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(payload); request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json"); request.timeout = 7;
                yield return request.SendWebRequest(); LoginBusy = false; loginRoutine = null;
                if (request.responseCode == 401) LoginStatus = "帐号或密码错误";
                else if (request.result != UnityWebRequest.Result.Success) LoginStatus = "暂时无法联网登录，请稍后重试，或离线进入";
                else
                {
                    DebugAccountSession session = null;
                    try
                    {
                        var response = JsonUtility.FromJson<LoginResponse>(request.downloadHandler.text);
                        if (response != null && response.service == "SkillSoccer.Login" && response.accountId == id && ValidServiceToken(response.accessToken))
                        { session = DebugAccounts.IsKnown(id) ? DebugAccounts.Login(id,password) : DebugAccounts.FromService(id); if (session != null) serviceAccessToken = response.accessToken; }
                    }
                    catch (Exception) { }
                    if (session != null)
                    {
                        Account?.Dispose(); Account = session; EntryMode = GameEntryMode.Online;
                        LoginStatus = ""; Route = ConnectionRoute.Internet;ReloadWallet(); if(HasPlayerName)GoHome();else OpenNickname(true);
                        ResetSocial();SyncSocialProfile();RefreshFriends();
                    }
                    else LoginStatus = "登录服务器返回无效的帐号信息";
                }
                UI.RefreshLogin();
            }
            Array.Clear(payload, 0, payload.Length);
        }
        private bool EnsureEntered()
        { if (EntryMode != GameEntryMode.NotEntered){if((SteamEntry || EntryMode==GameEntryMode.Online) && !HasPlayerName){OpenNickname(true);return false;}return true;} OpenLogin(); return false; }
    }
}
