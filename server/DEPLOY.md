# 0.30 游戏帐号、社交与对局服务

服务端与客户端一起升级至 0.30.0，使用 .NET 10。服务运行帐号注册、游戏好友、聊天、三人队伍、普通／排位队列，以及每局权威模拟。客户端不再作为这些比赛的权威房主。

## 本机暂用服务器

当前 Mac 已运行 `http://192.168.31.29:27862`，监听 `0.0.0.0:27862`，通过用户 LaunchAgent 自动启动。新版客户端默认读取包内 `StreamingAssets/game-service.json` 连接此地址。局域网用户可注册独立帐号，调试帐号默认关闭。

管理命令：`tools/python tools/local_server.py status|stop|start|uninstall`。更新服务及地址使用 `tools/python tools/local_server.py install`。存档位于 `~/Library/Application Support/SkillSoccer/Server/data`，不会因更新发布目录而丢失。此配置只声明局域网可访问，尚未配置公网域名及反向代理。

## 运行

从源码：

```sh
dotnet run --project server/Soccer.LoginServer -- --urls http://127.0.0.1:8080
```

或解压 `SkillSoccer-0.30.0-Server.zip`，在安装了 ASP.NET Core Runtime 10 的服务器上运行：

```sh
dotnet Soccer.LoginServer.dll --urls http://127.0.0.1:8080
```

对公网使用 HTTPS 反向代理，代理须允许 WebSocket Upgrade，并将请求转发至上述地址。客户端以启动参数配置地址：

```text
SkillSoccer.exe --soccer-auth-url https://你的游戏服务域名
```

配置由开发／部署提供，不要求普通玩家填写房主 IP 或服务密钥。本次客户端预设的是当前 Mac 的局域网地址；公网部署时修改该配置或使用启动参数覆盖。

## 存储与可选配置

环境变量（ASP.NET 的双下划线配置格式）：

| 变量 | 含义 |
| --- | --- |
| `Accounts__DataPath` | 帐号及排位存档，默认 `data/accounts.json` |
| `Social__DataPath` | 好友与个人主页存档，默认 `data/social.json` |
| `Chat__DataPath` | 最近 5000 条私聊，默认 `data/chat.json` |
| `Accounts__AllowDebug` | 是否开放 a/a、b/b；默认仅 Development 环境开启，公开部署保持 false |
| `Steam__AppId` | 正式取得权限的游戏 AppID；当前开发客户端默认 480 |
| `Steam__WebApiKey` | Steam 票据验证所需的服务端 Web API Key；不下发客户端、不写入包 |
| `Gameplay__MatchSeconds` | 服务端控制的总有效比赛秒数，默认 300；测试可缩短 |
| `Gameplay__OvertimeSeconds` | 金球加时秒数，默认 90 |

数据目录需可写，升级前备份这三个 JSON 文件。保存采用临时文件原子替换，写入失败不会部分更新排位分。服务当前为单实例；不要让多个进程共享同一组 JSON 文件。队伍、房间、邀请、排队、在线状态和进行中的比赛为内存状态，重启会清除；帐号、好友、私聊和已结算排位保留。

注册及登录接口有每 IP 的频率限制；不要在日志中开启请求体或 Authorization 头记录。密码只保存随机盐和 PBKDF2 摘要，服务令牌六小时到期。WebSocket 只接受属于本场的已认证玩家、相同协议版本及有界输入。

未配置 Steam Web API Key 时，Steam 房间邀请不受影响；Steam 玩家可通过游戏内“使用游戏帐号连接 / 注册”使用游戏好友、私聊和匹配，不需要假定 SteamID 已被游戏服务验证。

## 验证

测试使用独立的临时数据目录，可将 `Accounts__AllowDebug=true` 用于历史调试帐号回归。0.30 的主要集成测试创建六个独立帐号：

```sh
dotnet run --project tests/Soccer.Community.Tests -- http://127.0.0.1:8080 reports/community-029-service
```

客户端专项：

```sh
SkillSoccer.exe --soccer-community-smoke --soccer-auth-url http://127.0.0.1:8080 --soccer-report-dir reports/community-client
```

测试发送给本地生成的测试帐号，不向真实 Steam 好友发送邀请或聊天。六人服务端流程会执行真实选人、运动输入、进球、比赛结束及排名结算；客户端专项检查真实 Unity 界面、聊天焦点与不同窗口布局。公网延迟、容量压测、正式 Steam 凭据和 Windows 实机仍需单独验收。
