using System;
using System.Collections;
using System.Linq;
using System.Text;
using Soccer.Core.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace Soccer.Runtime
{
    public sealed partial class AppController
    {
        [Serializable] private sealed class RoomPublication
        { public string code, roomId, name, host; public int port, players; public bool running, requiresPassword; }
        [Serializable] private sealed class DirectoryRoom
        { public string code, roomId, name, host; public int port, players, expiresInSeconds; public bool running, requiresPassword; }
        [Serializable] private sealed class DirectoryError { public string error; }
        public bool NetworkLookupBusy { get; private set; }
        public bool RoomCodeReady => Room?.Connected == true && (Room.Lobby.Route == NetworkRoute.LAN || !Room.IsHost ||
            publishedRoom == Room && publishedCode == Room.Lobby.Code && Time.realtimeSinceStartup < directoryExpiresAt);
        private Coroutine publicationRoutine, lookupRoutine;
        private UnityWebRequest publicationRequest, lookupRequest;
        private NetworkRoom publishedRoom;
        private string publishedCode = "";
        private float directoryExpiresAt;
        private bool applicationQuitting, publicationDirty;
        private RoomOptions pendingLookupOptions;
        private UnityWebRequest ServiceRequest(string method, string path, string token, string payload = null)
        {
            var request = new UnityWebRequest(LoginServerAddress + path, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = 5 };
            request.SetRequestHeader("Authorization", "Bearer " + token);
            if (payload != null) { request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload)); request.SetRequestHeader("Content-Type", "application/json"); }
            return request;
        }
        private string Publication(NetworkRoom room)
        {
            string host = ""; var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--soccer-room-host");
            if (at >= 0 && at + 1 < args.Length) host = args[at + 1];
            return JsonUtility.ToJson(new RoomPublication { code = room.Lobby.Code, roomId = room.Lobby.Id, name = room.Lobby.Name, host = host,
                port = room.Port, players = room.Lobby.Members.Length, running = room.Running, requiresPassword = room.Lobby.RequiresPassword });
        }
        private static bool ReadDirectoryRoom(UnityWebRequest request, out DirectoryRoom room)
        {
            room = null;
            if (request.result != UnityWebRequest.Result.Success) return false;
            try
            {
                room = JsonUtility.FromJson<DirectoryRoom>(request.downloadHandler.text);
                if (room == null || !RoomCode.IsValid(room.code) || !Guid.TryParseExact(room.roomId, "N", out _) || room.expiresInSeconds < 3 || room.expiresInSeconds > 120 || room.players < 1 || room.players > 6) return false;
                NetworkRoom.ParseAddress(room.host + ":" + room.port, out _, out _); return true;
            }
            catch (Exception) { return false; }
        }
        private static string DirectoryFailure(UnityWebRequest request)
        {
            if (request.responseCode == 401) return "登录已失效，请返回首页重新登录";
            if (request.responseCode == 404) return "房间不存在或已关闭，请确认房间号";
            if (request.responseCode == 409)
            {
                try { var body = JsonUtility.FromJson<DirectoryError>(request.downloadHandler.text); if (body?.error == "房间已满") return "房间已满"; } catch (Exception) { }
                return "比赛已经开始，请等待房主返回房间";
            }
            return "房间号服务暂时不可用，请稍后重试";
        }
        private void StartRoomPublication(NetworkRoom room)
        {
            publishedRoom = room; NetworkStatus = "正在生成互联网房间号…"; UI.RefreshNetwork();
            publicationRoutine = StartCoroutine(PublishRoom(room));
        }
        private IEnumerator PublishRoom(NetworkRoom room)
        {
            bool registered = false; int initialFailures = 0;
            while (publishedRoom == room && Room == room && room.Connected)
            {
                bool create = publishedCode.Length == 0; float delay = 1; string payload = Publication(room);
                using (var request = ServiceRequest(create ? "POST" : "PUT", create ? "/api/rooms" : "/api/rooms/" + publishedCode, serviceAccessToken, payload))
                {
                    publicationRequest = request; yield return request.SendWebRequest(); publicationRequest = null;
                    if (publishedRoom != room || Room != room) yield break;
                    if (ReadDirectoryRoom(request, out var response) && response.roomId == room.Lobby.Id)
                    {
                        registered = true; initialFailures = 0; publishedCode = response.code;
                        directoryExpiresAt = Time.realtimeSinceStartup + response.expiresInSeconds;
                        if (room.Lobby.Code != response.code) room.SetRoomCode(response.code);
                        publicationDirty = Publication(room) != payload;
                        NetworkStatus = "房间号 " + response.code + " · " + (room.Running ? "比赛中" : "等待球员加入"); UI.RefreshNetwork();
                        delay = Mathf.Max(1, response.expiresInSeconds / 3f);
                    }
                    else if (request.responseCode == 404 && !create) { publishedCode = ""; directoryExpiresAt = 0; delay = 0; }
                    else
                    {
                        NetworkStatus = DirectoryFailure(request); UI.RefreshNetwork();
                        if (request.responseCode == 401) { directoryExpiresAt = 0; publicationRoutine = null; yield break; }
                        if (!registered && ++initialFailures >= 3)
                        {
                            room.Dispose(); Room = null; publishedRoom = null; publicationRoutine = null;
                            Page = AppPage.NetworkMenu; UI.ShowPage(Page); yield break;
                        }
                        publicationDirty = false;
                    }
                }
                float next = Time.realtimeSinceStartup + delay;
                while (publishedRoom == room && Room == room && Time.realtimeSinceStartup < next && !publicationDirty) yield return null;
            }
            publicationRoutine = null;
        }
        private void StartRoomLookup(string code, RoomOptions options, bool online)
        {
            NetworkLookupBusy = true; pendingLookupOptions = options; NetworkStatus = "正在查找房间 " + code + "…"; UI.RefreshNetwork();
            lookupRoutine = StartCoroutine(FindRoom(code, options, online));
        }
        private IEnumerator FindRoom(string code, RoomOptions options, bool online)
        {
            string invite = null;
            if (online)
            {
                using (var request = ServiceRequest("GET", "/api/rooms/" + code, serviceAccessToken))
                {
                    lookupRequest = request; yield return request.SendWebRequest(); lookupRequest = null;
                    if (ReadDirectoryRoom(request, out var response) && response.code == code && !response.running && response.players < 6)
                        invite = NetworkRoom.CreateInvitation(response.host, new RoomLobby { Id = response.roomId, Code = code, Route = NetworkRoute.Internet, Port = response.port });
                    else NetworkStatus = DirectoryFailure(request);
                }
            }
            else
            {
                float deadline = Time.realtimeSinceStartup + 2.5f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    var matches = Discovery?.Rooms.Where(r => r.Lobby.Code == code).ToArray() ?? new DiscoveredRoom[0];
                    if (matches.Length > 1) { NetworkStatus = "房间号重复，请从房间列表选择具体房间"; break; }
                    if (matches.Length == 1)
                    {
                        var found = matches[0]; NetworkRoom.ParseAddress(found.Address, out string host, out _);
                        invite = NetworkRoom.CreateInvitation(host, found.Lobby); break;
                    }
                    yield return null;
                }
                if (invite == null && NetworkStatus.StartsWith("正在查找")) NetworkStatus = "没有找到这个局域网房间，请确认房间号，或从列表选择";
            }
            NetworkLookupBusy = false; lookupRoutine = null; pendingLookupOptions = null;
            if (invite != null) ConnectNetworkRoom(invite, options); else { options.Password = ""; UI.RefreshNetwork(); }
        }
        private void StopRoomDirectory()
        {
            string code = publishedCode, token = serviceAccessToken;
            if (publicationRequest != null) { publicationRequest.Abort(); publicationRequest.Dispose(); publicationRequest = null; }
            if (lookupRequest != null) { lookupRequest.Abort(); lookupRequest.Dispose(); lookupRequest = null; }
            if (publicationRoutine != null) StopCoroutine(publicationRoutine);
            if (lookupRoutine != null) StopCoroutine(lookupRoutine);
            publicationRoutine = lookupRoutine = null; publishedRoom = null; publishedCode = ""; directoryExpiresAt = 0; NetworkLookupBusy = false;
            if (pendingLookupOptions != null) pendingLookupOptions.Password = ""; pendingLookupOptions = null;
            if (!applicationQuitting && code.Length > 0 && token.Length > 0) StartCoroutine(RemoveRoom(code, token, LoginServerAddress));
        }
        private IEnumerator RemoveRoom(string code, string token, string address)
        {
            using (var request = new UnityWebRequest(address + "/api/rooms/" + code, "DELETE") { downloadHandler = new DownloadHandlerBuffer(), timeout = 3 })
            { request.SetRequestHeader("Authorization", "Bearer " + token); yield return request.SendWebRequest(); }
        }
    }
}
