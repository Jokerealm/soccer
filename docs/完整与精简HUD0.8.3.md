# 完整与精简 HUD 0.8.3

新手默认开启完整技能栏。熟悉操作后，可在比赛右上角点“精简界面”，或在设置页关闭“完整技能栏”。选择自动保存，下次启动、换角色和重新开局继续使用；比赛中可随时切回完整界面。

完整模式保留头像、技能、冷却、生命／能量条和操作提示。底栏的背景单独设为 64% 不透明度，让球场透出来；图标、头像和数值保持清晰。取消“受到多少伤害”、命中伤害的底部弹窗，以及血条旁的扣血数字。受击的头像闪动、短暂屏幕边缘反馈、血条缓降和场内命中特效保留。

精简模式隐藏底栏、底部消息和操作提示，只在左上角显示当前角色头像及生命、能量数值。数值直接读取比赛状态，生命消耗、攻击、恢复和原地复活都会同步。头像仍能提示持球、低血量与击倒；隐藏的技能图标不会在球场上留下粒子。比分、时间、暂停和训练工具照常可用。

角色规则、技能数值、踢球／铲球冷却和 5 秒复活没有改动。本轮继续使用 0.8.2 的高清头像与技能插画，没有新增或替换人物美术。

## 实现入口

- `UnityProject/Assets/Soccer/UI/Game.uxml`：双模式层级、左上角信息卡和两处切换按钮。
- `UnityProject/Assets/Soccer/UI/Game.uss`：独立半透明背景、紧凑头像与数值布局。
- `UnityProject/Assets/Soccer/Runtime/UI/GameUI.cs`：保存与读取 `soccer.fullHud` 偏好、切换界面、同步实时数值。
- `UnityProject/Assets/Soccer/Runtime/UI/HudMotion.cs`：移除伤害数字、将头像反馈迁移到可见位置、停止隐藏技能的粒子。

## 验收证据

实际 macOS 客户端的检查及截图保存于 `reports/runtime-ui-083/`。新增检查覆盖默认／保存偏好、设置和比赛内切换、受击后底部数字消失、背景透明但图标清晰、精简模式实时资源、切回后的数值和冷却，以及 4:3 的左上角卡片和顶部按钮布局。

- 完整界面：`reports/runtime-ui-083/18-translucent-full-hud.png`
- 精简界面：`reports/runtime-ui-083/19-compact-player-hud.png`
- 4:3 精简界面：`reports/runtime-ui-083/09c-compact-match-4x3.png`
- 客户端报告：`reports/runtime-ui-083/runtime-smoke.json`

构建与运行结论见 `reports/ui-083-validation.json`。Windows 构建仍需在 Windows 设备上运行验收。
