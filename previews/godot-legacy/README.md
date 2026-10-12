# 历史 Godot 美术预览

这是在团结引擎路线确认前制作的静态美术预览，不包含游戏或联网逻辑。项目当前使用团结引擎 + C#。

三个 Godot 配置文件归档保留；`assets` 符号链接指向项目已有素材。历史构建报告在 `reports/build-validation.json`，不能作为 Unity／团结引擎打包结果。

若需要重现历史预览，可在项目根目录运行：

```sh
tools/python tools/build_resource_preview.py --legacy-godot
tools/godot --path previews/godot-legacy --editor
```

当前环境准备只需要 HTML 图库与 Unity／团结引擎安装清单。
