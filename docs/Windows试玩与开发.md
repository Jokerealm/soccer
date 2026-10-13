# Windows 试玩与开发

当前版本 **0.20.0**，Windows **64 位**，使用团结 **1.10.4 / 2022.3.62t16** 构建。仓库：https://github.com/Jokerealm/soccer。

## 直接试玩

1. 从 [0.20.0 发布页](https://github.com/Jokerealm/soccer/releases/tag/v0.20.0) 下载 `SkillSoccer-0.20.0-Windows-x64.zip`，包含六位房间号、联机、调试帐号与持球碰撞夺球。GitHub 自动生成的 `Source code (zip)` 是源码，不包含可执行游戏。
2. 在 Windows 上将整个压缩包解压，例如 `D:\Games\SkillSoccer-0.20.0-Windows-x64\`。
3. 双击其中的 `SkillSoccer.exe`。保留同目录的 `SkillSoccer_Data`、`MonoBleedingEdge`、`TuanjiePlayer.dll` 和 `TuanjieCrashHandler64.exe`；不能只复制 exe，也不要在压缩包内部直接运行。
4. 启动先选择“联网登录”或“离线进入”，然后在首页选择“训练场”熟悉操作，或“开始比赛”进行本地 3v3 AI 对练。

此方式无需安装 Git、团结引擎或 .NET SDK。构建和 macOS 客户端检查通过；用户已反馈旧版能在 Windows 试玩，新版的 Windows 实机手感仍需复核。普通模式的局域网和公网地址直连房间已接入，排位匹配、Steam 与云中继尚未接入。

联网登录在启动页请求配置的登录服务，调试帐号 **a/a** 或 **b/b**。无网或无服务器地址时选择“离线进入”，仍可局域网联机。步骤见 [0.20.0 更新说明](游戏主菜单0.20.0.md)。帐号密码与房间密码独立。

联机：**联机对战 → 局域网／互联网 → 创建房间／加入房间**。局域网可按房间号、列表或房主 IP 加入，互联网输入房主的 6 位房间号；互联网建房可开启密码，加入时展开“需要密码？”填写。进入房间后可展开“角色与队伍”调整，客人准备后由房主开始。最多 6 人，空位由 AI 补齐。Esc 只打开本机菜单，联机比赛继续进行。无需填写端口，程序自动处理。公共互联网服务尚未部署；开发者直连配置见 [最新联机说明](游戏主菜单0.20.0.md)。

| 操作 | 功能 |
| --- | --- |
| 右键地面 | 持续移动并绕开角色 |
| 右键敌人／队友／足球 | 追击攻击／跟随／追球；有武器且进入射程后攻击敌人 |
| Shift + 右键 | 追加移动／追击指令 |
| S 或 H | 停止移动和持续指令 |
| 鼠标 | 瞄准 |
| 按住 V 后松开 | 蓄力踢球，6 秒冷却 |
| 空格 | 铲球推开角色，5 秒冷却并消耗生命 |
| 左键 | 确认技能目标，不踢球 |
| Q / E | 当前角色技能；雷神 E 再左键确认方向 |
| G | 枪兵提前引爆炸弹 |
| C | 取消动作和路线 |
| Esc | 暂停 |
| 训练 R / F / T | 重置／自己持球／目标持球 |
| 训练 F1 / F2 / F3 | 显示推抢角度／角球练习／任意球练习 |

接球必须实际接触足球，敌方持球时碰到脚前球也可夺球，无需先按空格；接球后 0.25 秒内阻止自动再次易手。普通队友不会抢走持球队友的球，持球移速为基础速度的 80%。推队友或从敌人背后推，球随持球者移动；从敌人正面或侧面推，球留原地，之后接触球才能获得球权。整球越线判出界；界外球、角球、球门球和任意球已实现，点球暂不做。

## 克隆源码并在编辑器中运行

1. 安装 [Git for Windows](https://git-scm.com/install/windows)。也可在 PowerShell 中执行：

   ```powershell
   winget install --id Git.Git -e --source winget
   ```

2. 从 [团结 1.10.4 官方下载页](https://unity.cn/editor1-10-4/release-note) 安装 Windows 编辑器，或在 [团结 Hub](https://unity.cn/tuanjie/releases) 中安装该版本。工程锁定的内部版本是 **2022.3.62t16**，请保持这个版本。
3. 在希望保存工程的目录打开 PowerShell：

   ```powershell
   git clone https://github.com/Jokerealm/soccer.git
   cd soccer
   ```

4. 在团结 Hub 的项目页添加 `soccer\UnityProject` 目录，选择团结 1.10.4 打开。初次导入资源需要等待完成。
5. 在编辑器 Project 窗口打开 `Assets/Soccer/Scenes/Prototype.unity`，点击顶部 Play。

`UnityProject/Assets`、`Packages`、`ProjectSettings` 和资源 `.meta` 都在 Git 中。`Library` 缓存由本机生成；克隆后无需运行美术生成脚本或初始化菜单。

## 构建独立 Windows 游戏

关闭运行中的游戏。在编辑器菜单选择 **技能足球 → 构建 Windows 原型**，构建成功后运行 `UnityProject\Builds\Windows\SkillSoccer.exe`。当前使用 Mono 后端。

也可以先关闭这个工程的编辑器，再从仓库根目录运行 PowerShell 脚本；`EditorPath` 填写实际安装的编辑器 exe 完整路径：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-windows.ps1 -EditorPath "C:\实际安装目录\Editor\Tuanjie.exe"
```

如果安装目录中的编辑器名是 `Unity.exe`，使用它的真实路径即可。脚本也接受环境变量 `SOCCER_UNITY_EDITOR`，构建日志输出到 `reports\project-build-windows.log`。脚本检查退出码和本次构建报告，失败时不会把旧构建当成成功。

## 后续同步

关闭工程后，在仓库根目录执行：

```powershell
git pull --ff-only
```

重新打开团结工程，等待资源导入完成，再 Play 或重新构建。独立试玩包不会因为 `git pull` 自动更新，可下载新的 Release，或从更新后的源码构建。

## 可选：规则测试与试玩日志

只有运行纯 C# 规则测试才需要 .NET 10 SDK；编辑器试玩和独立游戏不需要另装 SDK：

```powershell
dotnet run --project .\tests\Soccer.Core.Tests -- "$PWD\reports\windows-core"
```

独立试玩包若遇到输入或碰撞问题，可以在解压目录打开 PowerShell，运行以下命令，再把生成的 `input.log` 作为排查依据：

```powershell
.\SkillSoccer.exe --soccer-input-probe -logFile "$PWD\input.log"
```

自动执行 0.16.1 持球夺球、接球保护和推抢专项检查：

```powershell
.\SkillSoccer.exe --soccer-smoke --soccer-contact-quick --soccer-report-dir "$PWD\Smoke" -logFile "$PWD\Smoke.log"
```

检查过程会自动操作游戏并退出，结果保存在 `Smoke\runtime-smoke.json`。一般试玩直接双击 exe 即可。
