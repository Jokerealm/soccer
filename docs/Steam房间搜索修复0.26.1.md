# Steam 房间搜索修复 0.26.1

2026-10-14。修复不同 Steam 帐号之间按房间号或列表加入时显示“未找到房间”的问题。

## 原因与修复

Steam 房间搜索已经返回有效大厅，旧版 `SteamPlatform.Describe` 却立即把元数据中的房主身份与 `GetLobbyOwner` 比较。Valve 规定该接口只对已加入大厅的帐号可用，未加入时返回 nil（0）；旧代码因此丢弃了其他帐号创建的房间。本机测试只搜索自己创建的大厅，未覆盖这个条件。

0.26.1 在搜索阶段校验游戏标记、协议和房间元数据；成功加入大厅后，再从 Steam 获取真实房主并核对身份，随后建立认证 P2P 连接。密码证明与网络连接身份校验继续生效。日志新增 Steam 搜索返回／接受数量及连接状态和结束原因，不记录房间密码。

参考：[Valve GetLobbyOwner 文档](https://partner.steamgames.com/doc/api/ISteamMatchmaking#GetLobbyOwner)。

## 使用

退出旧游戏并启动 0.26.1。选择 Steam 登录 → 联机对战 → Steam → 加入房间，输入房间号和房间密码。

本次修复未修改房间协议 `SkillSoccer/0.26.0/7`，0.26.1 加入端可连接 0.26.0 房主；两个方向测试推荐都升级 0.26.1。两端仍需使用不同 Steam 帐号。

## 验证

已在本机 Mac 使用真实 Steam 帐号搜索并加入用户保持打开的 Windows 房间 `J2LIX5`。加入前 `GetLobbyOwner` 返回 0，修复后列表和房间号搜索均保留该房间；Steam P2P、正确密码、加入后房主身份校验和两个真人成员复制通过，连接保持正常。报告：`reports/steam-0261-real-join/steam-join.json`。

本次实际验证覆盖跨帐号 Mac → Windows 房间加入；没有操作远端开始比赛，未据此宣称完整比赛或跨公网／SDR 路径已经验证。

另运行既有网络协议与 Steam 登录／本机原生 UDP 检查，并增加实际 Steam 大厅的房主元数据替换拒绝检查。macOS 与 Windows 0.26.1 构建均零错误、零警告。汇总：`reports/steam-0261-validation.json`。

外部房间回归入口：`--soccer-steam-smoke --soccer-steam-join-code <房间号> --soccer-steam-password-file <本地密码文件> --soccer-report-dir <报告目录>`。密码通过本地文件读取，不放入命令行或报告；测试完成会自动离开房间并退出。
