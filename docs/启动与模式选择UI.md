# 技能足球 · 启动与模式选择 UI 0.3

2026-10-09 · 团结引擎 1.10.4 / 2022.3.62t16

## 页面流程

启动加载封面 → 普通 / 排位 / 娱乐模式选择 → 模式详情与连接方式。

启动页使用三名现有角色的原创足球场插画，叠加真实文字、资源加载进度和球场提示。准备完成后自动进入模式选择。进度来自 25 个音频资源的实际预加载；封面最少展示 1.6 秒用于阅读，不模拟 Steam 连接或服务器进度。

主页面用青绿、金色、紫色区分三个模式，提供角色插画、原有模式图标、玩法摘要、悬停/键盘焦点反馈和完整卡片点击区域。底部保留训练场、AI 对练、角色图鉴和设置。当前离线状态有明确标记。

## 三种玩法与联机边界

| 模式 | 页面定位 | 当前可执行 | 联机规划 |
| --- | --- | --- | --- |
| 普通 | 标准竞技规则、自由对战、不影响段位 | 本地 3v3 AI 对练、训练场 | 互联网、局域网房间 |
| 排位 | 与普通共用竞技规则、公平竞技 | 查看玩法说明、进入训练 | 互联网对战、赛季段位与战绩 |
| 娱乐 | 强化技能、球场事件、欢乐房间 | 查看玩法说明、进入训练 | 互联网、局域网房间 |

这是前端 UI 迭代，没有实现 Steam SDK、身份、好友邀请、网络传输、匹配、房间发现或排位结算。联网操作按钮禁用并显示“即将开放”，模式详情说明“尚未开放”。局域网标签在排位模式禁用。娱乐的规则描述明确为规划，不把标准 AI 比赛冒充娱乐比赛，也不制造段位、在线人数或成功连接状态。

三种前端模式使用 `GamePlaylist`，互联网/局域网使用 `ConnectionRoute`。既有 Core 的 `MatchMode.Training` / `BotMatch` 仍只描述本地模拟。未来网络实现应接到前端能力状态与服务入口，再根据实际能力启用联机按钮；不要仅把 `SetEnabled(false)` 改为 `true`。

进入模式详情后点击本地对练或训练，会进入现有选角。选角“返回”回到来源模式；从首页工具进入的选角回到首页。Esc 可从详情/设置返回首页，从选角返回来源页。比赛中的 Esc 仍用于暂停。

## 素材与维护入口

| 内容 | 文件 |
| --- | --- |
| 页面层级、控件命名 | `UnityProject/Assets/Soccer/UI/Game.uxml` |
| 布局、配色、响应式规则 | `UnityProject/Assets/Soccer/UI/Game.uss` |
| 页面、连接标签与能力状态绑定 | `UnityProject/Assets/Soccer/Runtime/UI/GameUI.cs` |
| 启动预加载、导航、选角返回 | `UnityProject/Assets/Soccer/Runtime/Application/AppController.cs` |
| 三种玩法文案及 LAN 约束 | `UnityProject/Assets/Soccer/Runtime/Application/GameModeCatalog.cs` |
| 启动封面运行时素材 | [launch-cover-v1.png](../UnityProject/Assets/Soccer/Resources/FrontEnd/launch-cover-v1.png) |
| 封面原图 | [launch-cover-v1.png](../assets/original/ui/launch-cover-v1.png) |
| 完整生成提示词与参考 | [generation-prompt.json](../assets/original/ui/generation-prompt.json) |
| 运行时素材来源 | `UnityProject/Assets/Soccer/Resources/FrontEnd/provenance.json` |
| 原图复制、已有 SVG 图标栅格化 | `tools/prepare_frontend_assets.cjs` |

封面由内置 `image_gen` 生成，以风梭、铁卫、弧光三张现有静态稿为身份和画风参考；没有生成带按钮或文字的整页 UI 图，界面文字和操作都是真实 UI Toolkit 控件。图标由既有 `mode-standard-v1.svg` / `mode-ranked-v1.svg` / `mode-party-v1.svg` 转换，原始 SVG 保留。没有覆盖以前的角色或球场素材。

UI 按 1600×900 参考尺寸缩放。高度较小时使用紧凑布局，窄窗口隐藏次要页脚说明并缩小连接面板；实际截图检查覆盖 16:9、4:3 和超宽窗口。

## 验证

57 项实际 macOS Player 检查通过，无客户端错误；普通、排位、娱乐详情与加载封面截图已检查。16:9、4:3 和超宽窗口中的卡片、底部工具与详情操作均可点击。macOS / Windows 0.3 试玩构建已更新，Windows 实机仍待验证。汇总见 [frontend-ui-validation.json](../reports/frontend-ui-validation.json)。

## 复现

在仓库根目录运行：

```sh
"${SOCCER_NODE_BIN:-$HOME/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node}" tools/prepare_frontend_assets.cjs
tools/tuanjie -batchmode -nographics -quit -projectPath "$PWD/UnityProject" \
  -executeMethod Soccer.Editor.ProjectSetup.BuildMac -logFile "$PWD/reports/project-build-macos.log"
UnityProject/Builds/macOS/SkillSoccer.app/Contents/MacOS/技能足球 \
  --soccer-smoke --soccer-report-dir "$PWD/reports/runtime-ui-smoke" \
  -screen-width 1280 -screen-height 720 -screen-fullscreen 0 \
  -logFile "$PWD/reports/project-ui-runtime.log"
```

普通试玩构建使用非 Development 选项，不带开发版水印。编辑器菜单另保留“构建 macOS / Windows 调试版”，输出到独立的 `macOS-dev` / `Windows-dev` 目录。

正常启动不带 `--soccer-smoke`。客户端验证会保存启动、首页、三种模式详情、互联网/LAN 状态、选角、设置、比赛，以及 4:3 / 超宽布局的实际 Player 截图，并复查音频、蓄力和暂停生命周期。
