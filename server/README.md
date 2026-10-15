# 技能足球游戏服务 0.30.0

服务端使用 .NET 10，提供独立游戏帐号、好友、私聊、三人队伍、好友房间、普通／排位匹配，以及权威对局模拟。运行、持久化、Steam 票据及公网配置见 [部署说明](DEPLOY.md)。可使用 `tools/python tools/package_server.py` 生成不含帐号数据或凭据的便携服务包。

当前 Mac 已作为暂用服务器运行，地址 `http://192.168.31.29:27862`，客户端包已预设此地址。其他电脑需在同一局域网；公网服务尚未部署。可用 `--soccer-auth-url` 覆盖地址，玩家填写游戏帐号与密码。Steam 原生邀请与局域网房间聊天不依赖此服务。

## 接口

除注册／登录外，接口携带 `Authorization: Bearer <accessToken>`。会话有效期六小时，重启后失效。

| 接口 | 行为 |
| --- | --- |
| `GET /health` | 服务标识及版本 |
| `POST /api/register` | 创建独立游戏帐号，返回会话 |
| `POST /api/login` | 验证帐号密码，返回会话 |
| `POST /api/auth/steam` | 服务端通过 Valve 验证票据后建立游戏帐号；须配置 Web API Key |
| `GET /api/friends` | 好友、收到和发出的申请 |
| `POST /api/friends/{account}/request`、`accept`、`reject` | 申请、接受、拒绝 |
| `DELETE /api/friends/{account}` | 移除好友或撤回申请 |
| `GET /api/profiles/{account}` | 查看名字、累计角色战绩和最近 50 场 |
| `POST /api/profile/matches` | 上传本机完成的战绩；按 MatchId 去重，不能修改排位分 |
| `GET /api/community/state` | 队伍、邀请、房间、匹配、在线好友、私聊摘要和服务器排位分 |
| `POST /api/community/action` | 组队、邀请、准备、开房、加入、排队、取消及离开 |
| `GET /api/chat?channel=private&target=好友ID&after=消息ID` | 读取私聊；也支持 party、room 频道 |
| `POST /api/chat` | 发送私聊、队伍或房间消息；服务端绑定发送身份与频道权限 |
| `WS /api/matches/{matchId}/socket` | 已分配玩家的选人、输入、快照、队内／全场聊天和战术信号 |

WebSocket 还需 `X-SkillSoccer-Protocol` 与当前客户端协议一致。比赛的运动、球权、进球、结束和排位结算均由服务端执行；玩家只能提交自己的输入。普通匹配和排位等待六名真人，完整队伍保持同队；好友房间可由房主手动补 AI。队内聊天与信号只发给本队。

帐号、好友与主页、最近私聊分别原子保存至 `data/accounts.json`、`data/social.json`、`data/chat.json`；目录默认位于服务程序旁，部署时必须持久化。队伍、房间、在线状态、邀请、排队和进行中比赛为内存状态，重启会清除。当前为单实例原型服务。

## 原有互联网直连房间目录

目录接口继续保留：`POST /api/rooms`、`GET /api/rooms/{code}`、`PUT /api/rooms/{code}`、`DELETE /api/rooms/{code}`。目录只负责六位号码的发布、解析与租约，TCP 房间仍由房主运行，与上述权威匹配服务分开。

房间发布字段为 `code`、`roomId`、`name`、`host`、`port`、`players`、`running`、`requiresPassword`。密码不传给目录。房间由创建会话维护，其他帐号或同帐号的不同会话无法覆盖。默认租期 30 秒，每 10 秒续租；失效号码保留 10 分钟。目录不提供 NAT 穿透，互联网直连需要房主地址可达。新注册帐号使用游戏服务内的好友房间和匹配。

## 本地验证

`a/a`、`b/b` 仅在 `Accounts__AllowDebug=true` 或 Development 环境开放；生产默认禁用，旧存档中的这两个帐号也不能登录或被搜索。不要将调试凭据用于公开服务。

```sh
dotnet build server/Soccer.LoginServer
Accounts__AllowDebug=true dotnet run --no-build --project server/Soccer.LoginServer -- --urls http://127.0.0.1:27862
python tools/test_login_server.py --url http://127.0.0.1:27862
Accounts__AllowDebug=true python tools/test_room_directory.py --start-server
Accounts__AllowDebug=true python tools/test_social_server.py
dotnet run --project tests/Soccer.Community.Tests -- http://127.0.0.1:27862 reports/community-service
```

六人对局测试建议配置 `Gameplay__MatchSeconds=14`、`Gameplay__OvertimeSeconds=3`，使用独立的帐号／好友／聊天数据目录。测试只向本地生成的测试帐号发送消息与邀请，不操作真实 Steam 好友。
