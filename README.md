# 技能足球

合作乱斗足球游戏，Unity 体系的团结引擎 + C# 开发，Windows / Steam 优先，后续适配手机。常规规则使用二维球场，三维足球／球门、图片地面和实时灯光用于显示；只有未来特定越顶技能启用球的高度。

当前阶段：**模式、选人布阵与新手教程 0.22.0**。“开始游戏”先选模式；房间玩家准备后进入每局的轮流选人和初始位置选择，再开球。暂不加入 ban，枪兵与雷神允许重复。新增 13 步可操作的新手教程，覆盖移动、接球夺球、传射、推抢、武器、角色技能、恢复、复活与比赛规则。按键设置与可环绕的 3D 角色模型继续保留。

项目仓库：[Jokerealm/soccer](https://github.com/Jokerealm/soccer)。[下载 Windows 0.22.0 试玩包](https://github.com/Jokerealm/soccer/releases/tag/v0.22.0)，完整解压后运行 `SkillSoccer.exe`。操作和局域网步骤见 [本次更新说明](docs/模式选人与新手教程0.22.0.md)。登录服务地址由部署配置提供，服务源码及步骤见 [登录服务](server/README.md)；公共登录服务和比赛中继尚未部署。互联网房间按号码解析后连接直连房主，房间密码可独立设置。

体型碰撞、右键寻路和定位球继续保留：角色间按实际半径阻挡并绕行，球半径与模型一致；右键地面移动、右键敌人追击攻击、右键队友跟随，Shift 追加路线，S/H 停止。整球出界后按最后触球判界外球、角球或球门球；任意球已加入，正常武器、技能和推抢都允许进攻，点球暂不做。

上一轮：居中 HUD 与半场规则 0.14.0。头像和技能栏整体居中，冷却图标使用真实灰度、顺时针环形进度和剩余秒数；场上只保留柔和的生命、能量双条。AI 比赛分上下半场，原型各 2 分 30 秒，平局最多 90 秒金球加时。当前完整方案已汇总回最初的 [游戏设计文档](docs/游戏设计文档.md)，旧稿保留历史副本。

枪兵与雷神使用可替换的临时立体造型，统一 100 生命、100 能量；V 键踢球冷却 6 秒，空格铲球冷却 5 秒并扣生命，推力越大扣血越少。新手默认显示半透明完整技能栏，老手可切换为左上角头像与生命、能量数值，设置自动保存。生命低于 25% 时屏幕最外围出现半透明红色渐变。常规二维比赛规则和 **5 秒原地满状态复活**保留。

- [模式、选人布阵与新手教程 0.22.0](docs/模式选人与新手教程0.22.0.md)
- [按键设置与三维选角 0.21.0](docs/按键设置与三维选角0.21.0.md)
- [游戏主菜单 0.20.0](docs/游戏主菜单0.20.0.md)
- [房间界面精简 0.19.1](docs/房间界面精简0.19.1.md)
- [六位房间号联机 0.19.0](docs/六位房间号联机0.19.0.md)
- [自动端口与房间邀请 0.18.1](docs/自动端口与房间邀请0.18.1.md)
- [联机发现与操作优化 0.18.0](docs/联机发现与操作优化0.18.0.md)
- [调试帐号与 Windows 联机验证 0.17.1](docs/调试帐号与Windows联机验证0.17.1.md)
- [局域网与互联网联机 0.17.0](docs/局域网与互联网联机0.17.0.md)
- [持球碰撞夺球 0.16.1](docs/持球碰撞夺球0.16.1.md)
- [碰撞接球与推抢 0.16.0](docs/碰撞接球与推抢0.16.0.md)
- [碰撞、寻路与定位球 0.15.0](docs/碰撞寻路与定位球0.15.0.md)
- [居中 HUD 与半场规则 0.14.0](docs/居中HUD与半场规则0.14.0.md)
- [全屏球场 0.13.1](docs/全屏球场0.13.1.md)
- [全场头像小地图 0.13.0](docs/全场头像小地图0.13.0.md)
- [踢球与进球特效 0.12.0](docs/踢球与进球特效0.12.0.md)
- [球场材质与中场重制 0.11.0](docs/球场材质与中场重制0.11.0.md)
- [大球场与跟随镜头 0.10.0](docs/大球场与跟随镜头0.10.0.md)
- [开场热身与技能说明 0.9.2](docs/开场热身与技能说明0.9.2.md)
- [球场与开球特效 0.9.1](docs/球场与开球特效0.9.1.md)
- [动态立体球场 0.9](docs/动态立体球场0.9.md)
- [低血量屏幕边缘提示 0.8.4](docs/低血量屏幕边缘提示0.8.4.md)
- [完整与精简 HUD 0.8.3](docs/完整与精简HUD0.8.3.md)
- [战斗 UI 与动态反馈 0.8.2](docs/战斗UI与动态反馈0.8.2.md)
- [立体角色与战斗 HUD 0.8.1](docs/立体角色与战斗HUD0.8.1.md)
- [枪兵与雷神玩法原型 0.8](docs/枪兵与雷神玩法原型0.8.md)
- [二维规则与技能高球基础 0.7.1](docs/二维规则与技能高球0.7.1.md)
- [UI、球场光照与三维足球升级 0.6](docs/UI美术升级0.6.md)
- [角色趣味与打击感原型](docs/角色趣味与打击感.md)
- [合作乱斗玩法原型与下一轮打磨重点](docs/合作乱斗玩法原型.md)
- [启动封面与三种模式 UI 0.3](docs/启动与模式选择UI.md)
- [操作手感与音效 0.5](docs/操作手感与音效.md)
- [正式工程与试玩说明](UnityProject/README.md)
- [项目架构与开发指南](docs/项目架构与开发指南.md)
- [UI 界面与交互说明](docs/UI界面与交互说明.md)
- [原型验收与后续计划](docs/原型验收与后续计划.md)

用 **团结 1.10.4 / 2022.3.62t16** 打开 `UnityProject/`，打开 `Assets/Soccer/Scenes/Prototype.unity`，点击 Play。本机可直接运行 `UnityProject/Builds/macOS/SkillSoccer.app`；Windows 构建为 `UnityProject/Builds/Windows/SkillSoccer.exe`，Windows 实机运行待验证。

0.17.1 验证：217 项规则、26 项网络、60 项双客户端联机检查及 320 项完整客户端回归通过，macOS／Windows 构建均零错误、零警告。Windows 双机与跨公网运行由实际试玩复核，详情见 `reports/debug-accounts-0171-validation.json`。

0.18.0 验证：223 项规则、28 项网络、9 项登录服务、62 项双客户端检查及 331 项完整客户端回归通过。macOS／Windows 构建均零错误、零警告。汇总：`reports/usability-018-validation.json`；Windows 双机和跨公网环境待实际试玩。

0.18.1 验证：223 项规则、33 项网络、109 项双客户端检查及 331 项完整客户端回归通过。测试故意占用默认端口，覆盖局域网列表、只输入 IP 与互联网密码邀请加入。macOS／Windows 构建均零错误、零警告。汇总：`reports/automatic-ports-0181-validation.json`；Windows 实机及跨公网环境待试玩。

0.19.0 验证：223 项规则、35 项网络、9 项登录服务、36 项房间目录、160 项双客户端及 331 项完整客户端回归通过。房间目录覆盖并发重号、所有权、续租、失效与关闭；实际客户端覆盖互联网按号与密码入房、离线局域网按号／列表／IP 加入。macOS／Windows 构建均零错误、零警告，汇总：`reports/room-codes-019-validation.json`。

0.19.1 验证：237 项双客户端检查与 331 项完整客户端回归通过，覆盖默认折叠、可选密码、设置展开及 1024×768／1280×720 窗口的按钮命中。macOS／Windows 构建均零错误、零警告，汇总：`reports/simple-rooms-0191-validation.json`。

0.20.0 验证：349 项完整客户端检查与 123 项双客户端检查通过，包含方向键／Enter、主菜单直达入口、1024×768／1280×720／宽屏布局和菜单／比赛相机切换。macOS／Windows 构建均零错误、零警告，汇总：`reports/game-menu-020-validation.json`。联网代码和规则未改动，不重复运行上一版核心与服务测试。

0.21.0 验证：391 项完整客户端检查与 123 项双客户端联机检查通过。覆盖修改／保存／恢复按键、冲突拦截、重映射动作采样、提示同步、模型拖动／缩放／自动旋转、独立镜头关闭与小窗口布局。macOS／Windows 构建均零错误、零警告，汇总：`reports/keys-model-021-validation.json`。

0.22.0 验证：230 项核心规则、39 项网络、484 项完整客户端与 135 项双客户端检查全部通过。覆盖先选模式、轮次与位置校验、开局阵容／位置同步、重新选人、13 步实际教程及小窗口布局；双客户端覆盖本地服务的互联网房间号和无登录服务的局域网路径。macOS／Windows 构建均零错误、零警告，汇总：`reports/draft-tutorial-022-validation.json`。

0.18.0 操作：右键移动／追击／跟随、Shift 追加、S/H 停止，鼠标瞄准；V 键踢球（6 秒冷却），空格铲球（5 秒冷却、扣生命），右键敌人在射程内自动武器普攻，Q/E 角色技能，枪兵 G 提前引爆炸弹，C 取消动作与路线，Esc 暂停。开场 3 秒可移动热身，开球后接球与战斗开放。训练 F2 角球、F3 任意球；界外球复用脚下踢球流程。悬停底部图标查看技能说明。比赛右上角“精简界面／完整界面”切换 HUD；设置页也有“完整技能栏”开关。

- [游戏设计文档](docs/游戏设计文档.md)
- [关键设计确认单](docs/关键设计确认单.md)
- [开发环境、安装状态与手动安装步骤](docs/开发环境与平台清单.md)
- [美术资源准备](docs/美术资源准备.md)
- [资源图库](previews/art-gallery.html)
- [六角动作播放器](previews/animation-player.html)
- [动画资源准备与切帧说明](docs/动画资源准备.md)
- [动画资源包 v2（14 帧跑步、切帧 JSON、播放器与说明）](assets/packages/soccer-animation-keyframes-v2.zip)
- [资源索引](assets/manifest.json)

## 本机工具

已安装 Git LFS、.NET 10 SDK、Microsoft Unity / C# / C# Dev Kit 扩展。已有 Git、VS Code、Tuanjie Cowork、团结引擎 1.10.4、Unity 2022.3.62f3c1、Steam 和 Xcode Command Line Tools。

```sh
tools/python tools/doctor.py
tools/python tools/verify_assets.py
tools/python tools/pack_animation_resources.py
tools/git status --short
tools/dotnet --info
```

`doctor.py` 输出到 `reports/environment-check.json`；现已检测到团结引擎和 Windows Mono 模块，基础工具检查退出码为 0。C# SDK 独立编译运行检查已通过，见 `reports/csharp-sdk-validation.json`。团结引擎临时空工程的启动、C# 编译、PNG 精灵导入和 Windows 64 位 Mono 构建也已通过，见 `reports/tuanjie-environment-validation.json`；Windows 实机运行与 VS Code 调试连接尚未验证。

已确认使用团结引擎；当前环境为 Apple Silicon 版 1.10.4（内部版本 2022.3.62t16），对应 Windows Build Support (Mono) 已通过官方 Hub 补装。模块入口和后续设置见环境清单。正式工程已建立于 `UnityProject/`，引擎和包版本由工程锁定。

```sh
tools/dotnet run --project tests/Soccer.Core.Tests -- "$PWD/reports"
tools/tuanjie -projectPath "$PWD/UnityProject"
```

## 美术资源

新版 v2 已准备六角形象总览、风梭／铁卫／弧光三张透明静态稿、带场边厚度的球场底图，以及投影标线、立体球门、足球、独立阴影和队伍标记，共 11 项。图库支持新旧切换、图层开关和主体 64px／96px 缩略检查。

六人的跑步已从 8 帧补至 **14 帧**，重画收腿、过身、蹬地与腾空，默认 18 fps 保持一轮约 0.78 秒。转身、踢球各 8 帧，当前 18 套动作共 **180 帧、19 张在用透明 PNG**。[动作播放器](previews/animation-player.html) 支持 8／14 帧对比、行进／原地检查、单人放大、暂停、逐帧和击球示意；[动画索引 v2](assets/animation-manifest-v2.json) 提供源矩形、脚点、方向和帧率。三角原型已导入风梭、铁卫、弧光的 9 张图集、90 帧；仍为动画样稿，跑步／踢球仅右前 SE，其他方向暂用八向姿态，细节与锁脚需精修。**这些角色与动画作为历史样稿保留；0.5 试玩使用玩法棋子，角色参考按最新无厘头方向调整，形象素材尚未接入。** 旧版索引和 [v1 资源包](assets/packages/soccer-animation-keyframes-v1.zip) 保留；相机规范见 [美术规格 v2](assets/art-direction-v2.json)。

`assets/` 保留原创 PNG、SVG、Kenney CC0 素材及 Noto 中文字体；本轮只将选用素材复制到 Unity 的 `Assets/Soccer/`，随引擎产生的 `.meta` 一起提交，来源记录见 `UnityProject/Assets/Soccer/Art/provenance.json`。

图库可直接打开，也可运行：

```sh
tools/python -m http.server 8765 --bind 127.0.0.1
```

访问 [本机资源图库](http://127.0.0.1:8765/previews/art-gallery.html)。

动作演示：[本机六角动作播放器](http://127.0.0.1:8765/previews/animation-player.html)。

```sh
tools/python tools/prepare_resources.py
tools/python tools/create_vector_assets.py
tools/python tools/create_oblique_assets.py
tools/python tools/build_animation_preview.py
tools/python tools/build_resource_preview.py
tools/python tools/verify_assets.py
```

资源准备脚本默认只处理共用美术资源；图库脚本默认只重建 HTML。原始 PNG 与 v1／v2 生成提示词保存在 `assets/original/`；v2 投影图层由 `create_oblique_assets.py` 根据共享美术规格重建。

## 历史预览

此前 Godot 静态预览已归档到 `previews/godot-legacy/`，旧环境报告保存在 `reports/legacy/`。`reports/build-validation.json` 及现有 `builds/` 属于历史 Godot 素材验证，不是 Unity 游戏构建。已安装的 Godot 和下载缓存保留。

Git 保留完整团结工程、源美术、设计文档、工具及精简验证报告；引擎缓存、平台构建、测试日志和连续截图留在本机。当前 PNG/SVG 使用普通 Git，分层源稿预留 LFS。Windows 运行包独立通过 Releases 分发。
