> **0.8 玩法更新（2026-10-09）**：参考星际足球表格，先实现枪兵与雷神。统一 100 生命、100 能量，踢球 6 秒冷却，铲球 5 秒冷却并按推力反比扣生命。具体技能和原型数值见 [枪兵与雷神玩法原型](../docs/枪兵与雷神玩法原型0.8.md)。

# 技能足球 · PC 原型 0.18.0

0.18.0：启动登录／离线进入，多网卡局域网发现与房间通告，1.6 倍移动速度、2 点/秒生命恢复、V 踢球与鼠标技能确认。见 [本轮说明](../docs/联机发现与操作优化0.18.0.md)。

0.17.1：互联网先登录内置调试帐号 a/a 或 b/b，帐号身份由房主校验，入房密码可独立开关。

0.17.0：新增普通模式的局域网与互联网房间，支持局域网自动发现、地址加入、互联网密码开关、选队选角、准备、六人房主权威模拟、AI 补位与断线反馈。互联网为公网地址直连，详见 [联机说明](../docs/局域网与互联网联机0.17.0.md)。

0.16.1：自由球与敌方脚前球均可通过实际碰撞获得球权，无需先按空格；接球后有 0.25 秒自动易手保护，普通队友不会抢走队友球权，持球移速为基础速度的 80%。不使用额外接球距离或普通球速度门槛。推只腾出空间；队友／背后推带球一起位移，敌方正面和侧面受推后球留原地，必须再碰撞足球接球。详见 [0.16.1 更新](../docs/持球碰撞夺球0.16.1.md)。

0.15.0 新增：按体型碰撞、右键持续寻路／追击／跟随、Shift 队列和 S/H 停止，完整球越线出界、界外球／角球／球门球／任意球。正常攻击和推抢仍合法，不做点球。规则说明见 [0.15 更新](../docs/碰撞寻路与定位球0.15.0.md)。

0.14.0：头像和技能栏整体居中，冷却灰度＋单圈进度＋剩余秒数，头顶一体生命／能量双条。AI 对练目前每半场 150 秒，平局进入最多 90 秒金球加时；半场时长为可调原型值。统一设计入口见 [最初设计文档](../docs/游戏设计文档.md)。

当前规则见 [二维规则与技能高球 0.7.1](../docs/二维规则与技能高球0.7.1.md)：常规玩法保持二维，只为未来特定越顶技能保留高度基础；没有通用挑球操作或球门高度判定。UI 和场景保留 [美术升级 0.6](../docs/UI美术升级0.6.md) 的地面图片材质、实时三灯与软阴影、三维球门和足球模型。枪兵与雷神使用可替换的临时立体模型，底栏已改为方形技能图标、冷却遮罩和生命／能量长条；最终外观继续等待用户设定。战斗 UI、动画与粒子见 [0.8.2 更新](../docs/战斗UI与动态反馈0.8.2.md)。前一轮见 [0.8.1 展示更新](../docs/立体角色与战斗HUD0.8.1.md)。

正式玩法工程，固定使用 **团结引擎 1.10.4 / 2022.3.62t16**。入口场景：`Assets/Soccer/Scenes/Prototype.unity`。不要使用另行安装的 Unity 2022.3.62f3c1 打开此工程。

场景新增受光风动草地、立体三层看台、挥手观众、飘动队旗、滚动灯牌与悬浮无人机。进球会联动球网、灯牌、观众与庆祝粒子；暂停同时冻结场边动画。详见 [动态立体球场 0.9](../docs/动态立体球场0.9.md)。

0.9.1 将草地改为低饱和哑光绿，移除原图亮斑并调整补光，补齐角球弧线。开球使用透明光环、数字脉冲与扩散粒子，计时与暂停直接同步比赛状态。详见 [球场与开球特效](../docs/球场与开球特效0.9.1.md)。

0.9.2 开场可移动、转向热身，透明足球防护罩由 Core 阻止进入和接球，正式开球后解除。完整底栏缩小并移至球场下方；悬停技能图标弹出当前角色的描述、消耗和实时状态。详见 [开场热身与技能说明](../docs/开场热身与技能说明0.9.2.md)。

0.10.0 将实际球场扩大到 48×28，中圈与防护罩共用半径 2。镜头平滑跟随角色、限制地图边缘；足球出画时显示方向与距离。完整技能栏拥有底部独立视口，精简界面还原全部屏幕高度。详见 [大球场与跟随镜头](../docs/大球场与跟随镜头0.10.0.md)。

0.11.0 球场扩大到 56×34，开球圈和保护罩直径增至 8.4；重制高清草地材质、纹章中圈、立体包边和动态压草，开场出生点同步外移。详见 [球场材质与中场重制](../docs/球场材质与中场重制0.11.0.md)。

0.12.0 增加蓄力能量圈、踢球接触爆发与渐细轨迹；进球在实际球门处展开彩旗、彩纸与卷曲飘带，镜头短暂看向球门，开球后恢复角色跟随。暂停冻结、重置清理，完整和精简 HUD 均可看到球门庆祝。详见 [踢球与进球特效](../docs/踢球与进球特效0.12.0.md)。

0.13.0 在左下角增加半透明全场地图：双方角色实时显示对应头像，青色为队友、橙色为敌人，自己暖金高亮；包含足球、持球标记和镜头范围。完整／精简 HUD 均保留，击倒／复活、暂停、重置和换角色自动同步。详见 [全场头像小地图](../docs/全场头像小地图0.13.0.md)。

0.13.1 球场铺满整个窗口，取消完整 HUD 底部的黑色预留区；技能栏和小地图保持半透明，叠在球场上。完整／精简界面切换不改变球场视口，镜头继续跟随角色。详见 [全屏球场](../docs/全屏球场0.13.1.md)。

## 打开和试玩

Windows 的成品下载、源码同步和构建步骤见 [Windows 试玩与开发](../docs/Windows试玩与开发.md)。成品包完整解压后直接运行 `SkillSoccer.exe`；Git 克隆不会包含 `Builds/`。

在团结 Hub 中添加本目录，打开入口场景，点击 Play。启动加载封面后进入普通 / 排位 / 娱乐模式选择。普通模式可进入本地 AI 对练；三个模式均可进入训练场。普通模式的房间联机已开放；排位和娱乐特殊规则尚未开放，页面明确显示可用状态。首页工具栏也可直接进入训练场、AI 对练和设置。详见 [启动与模式选择 UI](../docs/启动与模式选择UI.md)。

- 右键地面移动／敌人追击攻击／队友跟随／足球追球，Shift 追加路线，S/H 停止，鼠标瞄准；左键按住蓄力、松开踢球（6 秒冷却），进入射程自动武器普攻，C 取消动作与路线。
- 空格铲球（5 秒冷却、扣生命），Q/E 技能，枪兵 G 手动引爆（10 能量），Esc 暂停。
- 比赛右上角切换“精简界面／完整界面”，设置页可开关完整技能栏，自动保存。精简模式在左上角显示头像和生命、能量数值；底部技能栏与提示隐藏。完整底栏的背景已增加透明度，取消底部伤害数字。见 [0.8.3 HUD 更新](../docs/完整与精简HUD0.8.3.md)。
- 生命低于 25% 时，屏幕最外围持续显示半透明红色。恢复到 25% 或以上、被击倒或重置后消退；完整／精简界面均可见。见 [0.8.4 低血量提示](../docs/低血量屏幕边缘提示0.8.4.md)。
- 训练中 R 重置球场和冷却，F 自己持球，T 目标持球，F1 切换角度显示，F2 练角球、F3 练任意球。场地上方训练工具栏提供同样功能。
- AI 对练为本地 3v3：你控制一人，其他五人为机器人。5 分钟有效时间，平局最多 90 秒金球加时。

已有构建（不纳入 Git）：`Builds/macOS/SkillSoccer.app`、`Builds/Windows/SkillSoccer.exe`。Windows 构建需在 Windows 设备上进行实际运行验收。

规则测试已有 217 项，包含碰撞接球、队友推送、背后带球位移、敌方正面／侧面原地落球、体型碰撞、高速连续接触、右键持续指令、动态绕行、出界归属、定位球与原有玩法回归。当前保留走位、普攻储备、瞄准／蓄力／命中反馈，以及 31 个原创音频素材和独立音乐／音效设置。详见 [操作手感与音效](../docs/操作手感与音效.md)。

0.17.0 的 217 项 Core、22 项网络、44 项双进程联机检查及 320 项完整客户端回归通过，两平台构建均零错误、零警告。汇总：`../reports/network-017-validation.json`；Windows 包：`../dist/SkillSoccer-0.17.0-Windows-x64.zip`。

0.16.1 的 217 项 Core 测试和 320 项完整客户端检查通过，包含敌方持球碰撞夺球、右键追球夺球、接球保护，以及原有推抢和 UI 回归。两平台构建均零错误、零警告，汇总为 `../reports/held-contact-0161-validation.json`，本机 Windows 包为 `../dist/SkillSoccer-0.16.1-Windows-x64.zip`。

0.16.0 的 27 项最终专项客户端检查通过，报告和实际截图见 `../reports/runtime-possession-contact-016-quick-final/`，包含侧推原地落球、接触后接球、背后／队友带球位移及暂停后坐标冻结。两平台构建均零错误、零警告，汇总为 `../reports/possession-contact-016-validation.json`。

0.15.0 macOS 客户端 306 项检查通过，覆盖原有规则、界面、动态球场、开场保护、踢球和进球反馈，以及全场头像地图、双方真实移动、球权、击倒／复活、暂停、重置、换角色、镜头范围和多尺寸布局；证据见 `../reports/runtime-collision-rts-015-final/runtime-smoke.json`。实体键鼠的操作感仍需人工试玩复核。排查输入问题时，可传入 `--soccer-input-probe -logFile /绝对路径/input.log`，记录原生键鼠与 UI 指针事件。

## 开发入口

| 想改什么 | 打开哪里 |
| --- | --- |
| 页面布局 | `Assets/Soccer/UI/Game.uxml`，可使用 UI Builder |
| 颜色、字号、间距 | `Assets/Soccer/UI/Game.uss` |
| UI 按钮绑定、HUD 数据 | `Assets/Soccer/Runtime/UI/GameUI.cs` |
| 开球数字、光环与粒子 | `Assets/Soccer/Runtime/UI/CountdownPresentation.cs` |
| 悬停技能说明 | `Assets/Soccer/Runtime/UI/SkillTooltip.cs` |
| 中场纹章与立体场边 | `Assets/Soccer/Runtime/Presentation/ArenaDetailView.cs` |
| 高清草地与动态压草 | `Assets/Soccer/Resources/SceneSurface/LivingTurf.shader`，`Assets/Soccer/Runtime/Presentation/DynamicStadiumView.cs` |
| 踢球蓄力、爆发与尾迹 | `Assets/Soccer/Runtime/Presentation/KickEffectsView.cs` |
| 进球彩旗、纸片与飘带 | `Assets/Soccer/Runtime/Presentation/GoalCelebrationView.cs` |
| 局部跟随镜头 | `Assets/Soccer/Runtime/Presentation/ArenaCamera.cs` |
| 全场头像小地图 | `Assets/Soccer/Runtime/UI/MatchMiniMap.cs` |
| 画外足球方向与距离 | `Assets/Soccer/Runtime/UI/BallLocator.cs` |
| 足球防护罩 | `Assets/Soccer/Core/MovementSystem.cs`，`Assets/Soccer/Runtime/Presentation/OpeningShieldView.cs` |
| 三种模式文案、连接限制 | `Assets/Soccer/Runtime/Application/GameModeCatalog.cs` |
| 启动封面与模式图标 | `Assets/Soccer/Resources/FrontEnd/` |
| 页面切换、开局、暂停 | `Assets/Soccer/Runtime/Application/AppController.cs` |
| 球场、比赛时长、抢断角度 | `Assets/Soccer/Data/PrototypeRules.asset` |
| 角色数值、技能说明、资源 | `Assets/Soccer/Data/{marine,thor}.asset` |
| 音乐与比赛音效 | `Assets/Soccer/Runtime/Presentation/AudioFeedback.cs`，`Assets/Soccer/Resources/SoccerAudio/` |
| 动作输入缓冲 | `Assets/Soccer/Core/PlayerActionBuffer.cs` |
| 比赛规则实现 | `Assets/Soccer/Core/`，无 Unity 引用 |
| 角色动画与球场显示 | `Assets/Soccer/Runtime/Presentation/` |
| 原图与切帧来源 | 上级 `assets/`；工程内来源索引 `Assets/Soccer/Art/provenance.json` |

完整说明在上级目录的 [项目架构与开发指南](../docs/项目架构与开发指南.md)、[UI 界面与交互说明](../docs/UI界面与交互说明.md)、[原型验收与后续计划](../docs/原型验收与后续计划.md)。

## 复现

从仓库根目录运行：

```sh
tools/dotnet run --project tests/Soccer.Core.Tests -- "$PWD/reports"
tools/tuanjie -batchmode -nographics -quit -projectPath "$PWD/UnityProject" \
  -executeMethod Soccer.Editor.ProjectSetup.BuildMac -logFile "$PWD/reports/project-build-macos.log"
tools/tuanjie -batchmode -nographics -quit -projectPath "$PWD/UnityProject" \
  -executeMethod Soccer.Editor.ProjectSetup.BuildWindows -logFile "$PWD/reports/project-build-windows.log"
```

普通资源改动由编辑器导入。重新从源稿准备选用素材时运行 `tools/python tools/prepare_unity_assets.py`；它会复制 PNG、字体及授权音效，并将既有 SVG 栅格化，不重画原图。该转换使用 Node.js 和 sharp，可用 `SOCCER_NODE_BIN` / `SOCCER_NODE_MODULES` 指定安装位置。

编辑器菜单 **技能足球 → 初始化资源与场景（保留已有配置）** 会更新动画切帧和绑定，保留已有数值资产与场景编辑。首次工程建立后，日常开发直接编辑配置和场景；无需每次重新初始化。

原型规则来自设计稿建议，尚未冻结。联机、Steam、排位、手机与后三名角色安排在后续阶段；本次本地模拟不能直接当作线上权威服务器。

0.17.1 调试帐号与 Windows 公网试玩步骤见 [测试指南](../docs/调试帐号与Windows联机验证0.17.1.md)，Windows 包从 [发布页](https://github.com/Jokerealm/soccer/releases/tag/v0.17.1) 下载。
