# 调试帐号登录服务

客户端 0.18.0 在启动页通过 HTTP 请求此服务验证帐号，成功后进入在线模式；a/a、b/b 是公开试玩帐号。没有服务地址时，可选择“离线进入”，继续训练、AI 对练和局域网房间。

安装 .NET 10 SDK 后，在仓库根目录启动服务：

```sh
dotnet run --project server/Soccer.LoginServer --urls http://0.0.0.0:27862
```

本机客户端填写 `http://127.0.0.1:27862`；同局域网其他客户端填写服务电脑的局域网地址，例如 `http://192.168.1.10:27862`，并放行 TCP 27862。登录地址只保存地址，帐号密码不会写入客户端设置。服务不会记录帐号密码，也没有注册或个人资料功能。

公网部署时发布该 ASP.NET Core 服务并在 HTTPS 域名后运行；客户端填写该 HTTPS 地址。当前仓库提供服务代码与运行步骤，尚未部署公共登录地址。HTTP 明文仅供本机／受控网络的公开调试帐号验证。

```sh
dotnet publish server/Soccer.LoginServer -c Release -o dist/login-server
dotnet dist/login-server/Soccer.LoginServer.dll --urls http://127.0.0.1:27862
```

接口：`GET /health` 返回服务标识；`POST /api/login` 接受 JSON 字段 `account`、`password`，返回经服务校验的 `accountId`、`displayName`、`service`；错误凭据返回 401，请求体上限 1024 字节。

比赛由房主的 TCP 房间承担权威模拟，登录服务负责帐号校验。若需要无需公网 IP 或路由器端口映射的互联网比赛，需要继续部署中继／比赛服务器。局域网无需此服务。

服务检查：

```sh
python tools/test_login_server.py --url http://127.0.0.1:27862
```
