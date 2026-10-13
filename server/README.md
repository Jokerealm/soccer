# 登录与房间号服务

客户端 0.19.0 在启动页通过 HTTP 验证帐号，并用同一服务登记和查询 6 位互联网房间号。a/a、b/b 是公开试玩帐号。公共服务尚未部署；离线进入仍能训练、AI 对练和局域网联机，局域网房间号不依赖此服务。

安装 .NET 10 SDK 后，在仓库根目录运行：

```sh
dotnet run --project server/Soccer.LoginServer --urls http://0.0.0.0:27862
```

开发者启动客户端时通过 `--soccer-auth-url http://127.0.0.1:27862` 配置本机服务。测试另一电脑时可使用服务电脑的 IPv4 地址，例如 `--soccer-auth-url http://192.168.1.10:27862`。服务电脑需要允许 TCP 27862。玩家界面只填写帐号、密码和房间号；地址和端口属于程序与部署配置。

登录返回内存中的 6 小时 Bearer 会话令牌，帐号密码与令牌不写入客户端设置，也不写入服务日志。每个房间由创建时的会话维护，不按共享调试帐号授权修改。房间目录只接收是否需要密码的标记，房间密码在房主的实际游戏连接中验证。

接口：

| 接口 | 行为 |
| --- | --- |
| `GET /health` | 服务标识及版本 |
| `POST /api/login` | 验证 `account`、`password`，返回 `accountId`、`service`、`accessToken`、`expiresInSeconds` |
| `POST /api/rooms` | 登录会话创建或幂等注册自己的房间，返回唯一 6 位号码 |
| `GET /api/rooms/{code}` | 登录后解析号码；大小写不敏感；已满或正在比赛返回 409，已失效返回 404 |
| `PUT /api/rooms/{code}` | 房主会话续租，并更新人数与比赛状态 |
| `DELETE /api/rooms/{code}` | 房主会话关闭房间 |

房间接口携带 `Authorization: Bearer <accessToken>`。注册／续租 JSON 字段为 `code`、`roomId`、`name`、`host`、`port`、`players`、`running`、`requiresPassword`。客户端自动填写。`host` 留空时使用服务观察到的客户端 IPv4 地址，IPv4 映射 IPv6 地址会转为 IPv4；原生 IPv6 房主连接暂未支持。地址覆盖使用客户端开发参数 `--soccer-room-host`。部署在代理后时需要提供正确的房主公网地址；当前服务不盲目信任请求中的转发头。

默认租期 30 秒，客户端每 10 秒续租，并在状态改变时提前更新。异常退出后自动过期，正常离开主动删除；失效号码短期保留 10 分钟。可通过 `--RoomDirectory:LeaseSeconds=30` 调整租期，范围 3—120 秒。会话与目录均存于内存，服务重启后需要重新登录和建房；实现限制了会话、活动房间与保留号码数量。

公网部署应使用 HTTPS；HTTP 仅供本机／受控网络的公开调试帐号验证。发布：

```sh
dotnet publish server/Soccer.LoginServer -c Release -o dist/login-server
dotnet dist/login-server/Soccer.LoginServer.dll --urls http://127.0.0.1:27862
```

房间目录只负责查号，比赛仍由房主的 TCP 房间执行权威模拟。房间号不会提供 NAT 穿透；无公网可达房主时，需要继续部署中继／比赛服务器。

检查：

```sh
python tools/test_login_server.py --url http://127.0.0.1:27862 --report reports/room-codes-019-login-service.json
python tools/test_room_directory.py --start-server
```

第二条命令自行选取空闲本机端口、启动短租期测试服务并关闭，不会操作已有服务器。
