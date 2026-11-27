# Sokoban Adventure

一款用 Unity 2022.3.51f1 和 C# 制作的推箱子解谜游戏，带完整的关卡编辑器。

## 特色

- 世界由多个房间组成：仓库、工厂、森林，房间之间可以跨房推箱、恢复装置归位。
- 游戏内编辑器：编辑世界、房间、自定义模型导入导出、普通物件属性、验证与试玩。
- Unity Editor 内创作：行为图（节点图）、Lua 表达式与脚本，游戏内可完整加载、保存并执行。
- 恢复装置：叉车、机械臂、树木等装置带有恢复演出，可编辑时长、道具文字与脚本参数。

## 操作

- WASD 移动，Q / E 转向，R 启动本房恢复装置，滚轮切换道具，Esc 暂停。

## 目录结构

- `Assets/Sokoban/Core/`：纯 C# 规则与数据层（房间草稿、命令与撤销、画刷、校验、文件编解码）。
- `Assets/Sokoban/Resources/`：预制体、材质、shader、音频与 Diorama 美术资源。
- `Assets/Sokoban/Editor/`：Unity 编辑器窗口（房间布局、行为图、Lua 编辑）与验证脚本。
- `Assets/Sokoban/ThirdParty/`：MoonSharp（Lua 解释器）。
- `Assets/Scenes/`：主菜单与运行时编辑器场景。
- `Assets/StreamingAssets/World/`：默认世界与三个房间的数据文件。
- `Tests/Core.Tests/`：Core 层的 dotnet 单元测试。
- `Tools/`：辅助脚本。

## 开发环境

- Unity 2022.3.51f1（URP）。
- 大文件（字体、模型、贴图、音频、dll）使用 Git LFS，克隆前请先安装 Git LFS。
- Core 层测试：在 `Tests/Core.Tests` 下运行 `dotnet test`。

## 下载试玩

在 Releases 页面下载 Windows 压缩包，解压后运行 `Sokoban.exe`。
