# Steam 联机 0.26.0

本版新增 Steam 登录、Steam 房间发现、六位房间号加入和 Steam Networking Sockets 玩家传输。局域网与原有互联网直连继续保留，Steam 联机不依赖项目自己的登录／房间目录服务器。

## Mac 与 Windows 双机测试

1. 两台电脑启动 Steam 并登录**不同的 Steam 帐号**，使用同一版 0.26.0 游戏。完整解压 Windows 试玩包，保留原有文件及 `steam_appid.txt`。
2. 游戏启动页选择 **Steam 登录**，进入“联机对战”或“开始游戏 → 普通对战”，选择 **Steam**。
3. Mac 创建房间，按需开启密码。Windows 选择加入房间，输入 Mac 的六位房间号；也可刷新 Steam 房间列表后点击加入。有密码的房间需要展开密码栏。
4. 房主按需要添加机器人补齐空位，双方点击“准备”。六个位置占满且所有真人准备后，倒计时 3 秒进入轮流选人、初始位置选择，再开始比赛。取消准备或更改阵容会取消倒计时。
5. 测试双方移动、推球、踢球、技能、进球和结算；结束后检查金币与最近战绩。再交换房主重复一次。有条件时两台设备分别连接不同网络，以检验跨公网路径。
6. 房间内的“邀请 Steam 好友”可打开 Steam 邀请界面。直接启动测试包时可能没有 Steam Overlay，此时按钮复制房间号，可在 Steam 中手动分享。接受邀请时两端游戏应已运行；有密码的房间仍需在游戏内填写密码。

**测试 AppID 为 480（Valve Spacewar 示例）**，Steam 可能显示正在运行 Spacewar，这是开发测试身份。当前尚未配置本游戏正式 AppID、Steam 商店／构建分发或正式云存档。不要修改测试包中的 DLL；正式接入时由开发者将 AppID 改为已取得权限的游戏 AppID。

Steam 登录后的金币、个人主页和战绩使用 SteamID 对应的本地独立档案，暂不与 a/b 调试帐号合并或跨设备同步。“好友”显示 Steam 好友，可打开 Steam 好友管理与个人主页；项目自建帐号的好友申请仍使用原登录服务。Steam 好友的本游戏远程战绩后续接入。

## 故障信息

- 无法 Steam 登录：确认 Steam 在线、帐号已登录，退出游戏后重开。离线进入仍可局域网联机。
- 搜索不到房间：核对两端游戏版本、选择的联机方式、房间号，以及房间是否已满／正在倒计时、选人或比赛。Steam 房间列表使用游戏标记和协议版本过滤，不显示其他 Spacewar 测试项目的房间。
- 搜索到但连接失败：保留两端日志，核对两台设备是否使用不同 Steam 帐号。房主退出后房间关闭，当前不支持迁移房主。
- Windows 日志：`%USERPROFILE%\AppData\LocalLow\Future City League\技能足球\Player.log`（以实际引擎生成路径为准）；也可通过启动参数 `-logFile steam-test.log` 指定日志。

玩家无需手动填写端口、转发路由器或填写 SteamID。Steam 网络路径由 SDK 处理；实际是否采用中继需要双机连接数据确认。

## 开发实现与验证范围

嵌入固定版本 **Steamworks.NET 2025.164.1**，保留 upstream 原始包及 MIT 许可。macOS 使用包内通用 arm64／x64 `steam_api.bundle`，Windows x64 使用 `steam_api64.dll`。`SteamPlatform` 在 Unity 主线程初始化 SDK、运行回调、管理 Valve Lobby 和 P2P 连接，`SteamSocketPeer` 将 Steam 消息边界映射到既有 `IRoomPeer` 协议。

实际 Steam 连接身份来自 SDK 的连接信息。房间握手核对玩家声明身份与连接身份；客人同时核对已认证的房主，禁止同一 Steam 帐号重复占位，房间密码继续使用带随机挑战的证明。Steam 房间不会借用 a/b 调试凭据，也不会以普通 TCP 伪装 Steam 身份。

原房主权威比赛规则、输入、快照、准备、倒计时、选人和结算共享同一协议 `SkillSoccer/0.26.0/7`。本版先使用可靠消息，后续可依据实测延迟优化快照策略。协议版本升级，两端必须同时更新。

专用 `--soccer-steam-smoke` 检查真实 Steam 登录、Valve Lobby 创建和按号搜索，并通过 SDK 的加密 UDP socket pair 验证现有房间协议、准备、倒计时、选人、输入与比分快照。Socket pair 两端的原生连接使用当前真实帐号，房间协议中的第二身份由隔离的测试适配器提供，不能证明第二个真实帐号认证、跨公网 P2P 或 SDR 中继已通过。汇总见 `reports/steam-026-validation.json`，Windows 实机与不同 Steam 帐号双机测试仍需按上面的步骤完成。

正式 AppID 测试可以使用 `--soccer-steam-app-id <AppID>` 覆盖默认 480，两端需有相应访问权限。测试构建旁的 `steam_appid.txt` 仅服务直接启动的开发包；正式 Steam 分发时移除，由 Steam 启动环境提供 AppID。当前不处理游戏尚未启动时的 Steam 自动启动邀请。

参考：[Valve 网络功能](https://partner.steamgames.com/doc/features/multiplayer/networking)、[ISteamNetworkingSockets](https://partner.steamgames.com/doc/api/ISteamNetworkingSockets)、[Valve Lobby](https://partner.steamgames.com/doc/features/multiplayer/matchmaking)、[Spacewar 示例](https://partner.steamgames.com/doc/sdk/api/example)、[Steamworks.NET 安装](https://steamworks.github.io/installation/)。
