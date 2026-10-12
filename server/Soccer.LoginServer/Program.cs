using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024);
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { service = "SkillSoccer.Login", version = "0.18.0" }));
app.MapPost("/api/login", (LoginRequest request) =>
{
    string account = (request.Account ?? "").Trim();
    if (account != "a" && account != "b" || request.Password == null || request.Password.Length > 64)
        return Results.Json(new { error = "帐号或密码错误" }, statusCode: 401);
    byte[] supplied = SHA256.HashData(Encoding.UTF8.GetBytes(request.Password));
    byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(account));
    if (!CryptographicOperations.FixedTimeEquals(supplied, expected))
        return Results.Json(new { error = "帐号或密码错误" }, statusCode: 401);
    return Results.Ok(new { accountId = account, displayName = "帐号 " + account, service = "SkillSoccer.Login" });
});
app.Run();
record LoginRequest(string? Account, string? Password);
