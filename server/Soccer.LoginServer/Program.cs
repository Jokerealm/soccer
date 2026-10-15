using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16384);
builder.Services.ConfigureHttpJsonOptions(options=>{options.SerializerOptions.IncludeFields=true;options.SerializerOptions.PropertyNamingPolicy=null;});
builder.Services.AddRateLimiter(options=>{
    options.RejectionStatusCode=429;
    options.AddPolicy("identity",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=60,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
});
var app = builder.Build();app.UseRateLimiter();
var directory = new RoomDirectory(builder.Configuration.GetValue("RoomDirectory:LeaseSeconds", 30));
var social=new SocialService(builder.Configuration["Social:DataPath"] ?? Path.Combine(AppContext.BaseDirectory,"data","social.json"));
var accounts=new AccountService(builder.Configuration["Accounts:DataPath"] ?? Path.Combine(AppContext.BaseDirectory,"data","accounts.json"),builder.Configuration.GetValue("Accounts:AllowDebug",builder.Environment.IsDevelopment()));
social.KnownAccount=id=>accounts.Find(id)!=null;
var community=new CommunityService(accounts,social,builder.Configuration);
app.Lifetime.ApplicationStopping.Register(community.Dispose);
app.MapGet("/health", () => Results.Ok(new { service = "SkillSoccer.Login", version = "0.30.0" }));
IResult Fail(int code,string error)=>Results.Json(new{error},statusCode:code);
IResult AuthResponse(AccountService.User user)
{
    social.EnsureAccount(user.Id,user.Name);string? token=directory.Login(user.Id);
    return token==null ? Results.StatusCode(503) : Results.Ok(new{accountId=user.Id,displayName=user.Name,service="SkillSoccer.Login",accessToken=token,expiresInSeconds=21600});
}
IResult Guard(HttpContext context,Func<string,IResult> action)
{
    string? id=directory.Account(context);if(id==null)return Fail(401,"请重新登录");
    try{return action(id);}catch(ArgumentException e){return Fail(400,e.Message);}catch(IOException){return Fail(503,"暂时无法保存，请稍后重试");}
}
app.MapPost("/api/login",(LoginRequest request)=>
{
    var user=accounts.Login((request.Account??"").Trim(),request.Password??"");return user==null ? Fail(401,"帐号或密码错误") : AuthResponse(user);
}).RequireRateLimiting("identity");
app.MapPost("/api/register",(RegisterRequest request)=>
{
    try{return AuthResponse(accounts.Register(request.Account??"",request.Name??"",request.Password??""));}
    catch(ArgumentException e){return Fail(400,e.Message);}catch(IOException){return Fail(503,"帐号暂时无法保存，请稍后重试");}
}).RequireRateLimiting("identity");
app.MapPost("/api/auth/steam",async (SteamLoginRequest request)=>
{
    string key=builder.Configuration["Steam:WebApiKey"]??"";uint appId=builder.Configuration.GetValue("Steam:AppId",480u);
    if(key.Length==0)return Fail(503,"Steam 游戏好友服务尚未配置，可使用游戏帐号登录或 Steam 房间邀请");
    if(request.Ticket==null || request.Ticket.Length<16 || request.Ticket.Length>5120 || request.Ticket.Any(c=>!Uri.IsHexDigit(c)))return Fail(400,"Steam票据无效");
    try
    {
        using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(8)};
        string endpoint="https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/?key="+Uri.EscapeDataString(key)+"&appid="+appId+"&identity=skillsoccer-community&ticket="+Uri.EscapeDataString(request.Ticket);
        using var response=await client.GetAsync(endpoint);using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var parameters=doc.RootElement.GetProperty("response").GetProperty("params");
        if(!response.IsSuccessStatusCode || parameters.GetProperty("result").GetString()!="OK" || !ulong.TryParse(parameters.GetProperty("steamid").GetString(),out var steamId) || steamId==0)return Fail(401,"Steam身份验证失败");
        return AuthResponse(accounts.Steam(steamId.ToString(),request.Name??"Steam球员"));
    }
    catch(Exception e) when(e is HttpRequestException || e is TaskCanceledException || e is JsonException || e is KeyNotFoundException){return Fail(503,"Steam身份服务暂时不可用，请稍后重试");}
}).RequireRateLimiting("identity");
app.MapGet("/api/community/state",(HttpContext context)=>Guard(context,id=>Results.Ok(community.State(id))));
app.MapPost("/api/community/action",(HttpContext context,Soccer.Core.CommunityAction request)=>Guard(context,id=>Results.Ok(community.Action(id,request))));
app.MapGet("/api/chat",(HttpContext context,string channel,string? target,long? after)=>Guard(context,id=>Results.Ok(community.Chat(id,channel,target??"",Math.Max(0,after??0)))));
app.MapPost("/api/chat",(HttpContext context,Soccer.Core.ChatRequest request)=>Guard(context,id=>Results.Ok(community.Send(id,request))));
app.UseWebSockets(new WebSocketOptions{KeepAliveInterval=TimeSpan.FromSeconds(15)});
app.Map("/api/matches/{matchId}/socket",async (HttpContext context,string matchId)=>
{
    string? id=directory.Account(context);if(id==null){context.Response.StatusCode=401;return;}
    var match=community.Match(id,matchId);if(match==null){context.Response.StatusCode=403;return;}
    if(!context.WebSockets.IsWebSocketRequest){context.Response.StatusCode=400;return;}
    if(context.Request.Headers["X-SkillSoccer-Protocol"]!=Soccer.Core.Networking.NetworkCodec.Version){context.Response.StatusCode=426;return;}
    using var socket=await context.WebSockets.AcceptWebSocketAsync();await match.Connect(id,socket,context.RequestAborted);
});
app.MapPost("/api/rooms", (HttpContext context, RoomPublication request) => directory.Create(context, request));
app.MapGet("/api/rooms/{code}", (HttpContext context, string code) => directory.Find(context, code));
app.MapPut("/api/rooms/{code}", (HttpContext context, string code, RoomPublication request) => directory.Renew(context, code, request));
app.MapDelete("/api/rooms/{code}", (HttpContext context, string code) => directory.Remove(context, code));
app.MapGet("/api/profiles/{account}",(HttpContext context,string account)=>social.Profile(directory.Account(context),account));
app.MapPost("/api/profile/matches",(HttpContext context,Soccer.Core.MatchRecord match)=>social.AddMatch(directory.Account(context),match));
app.MapGet("/api/friends",(HttpContext context)=>social.List(directory.Account(context)));
app.MapPost("/api/friends/{account}/request",(HttpContext context,string account)=>social.Change(directory.Account(context),account,"request"));
app.MapPost("/api/friends/{account}/accept",(HttpContext context,string account)=>social.Change(directory.Account(context),account,"accept"));
app.MapPost("/api/friends/{account}/reject",(HttpContext context,string account)=>social.Change(directory.Account(context),account,"reject"));
app.MapDelete("/api/friends/{account}",(HttpContext context,string account)=>social.Change(directory.Account(context),account,"remove"));
app.Run();
record LoginRequest(string? Account, string? Password);

record RegisterRequest(string? Account,string? Name,string? Password);
record SteamLoginRequest(string? Ticket,string? Name);
