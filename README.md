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

## 世界与房间文件（JSON）

游戏用两种 JSON 描述关卡，格式号都是 `formatVersion: 1`。默认世界在 `Assets/StreamingAssets/World/`，打包后随游戏放在 `Sokoban_Data/StreamingAssets/World/`：

- `main.world.json`：世界文件（`kind: "world"`），决定有哪些房间、各自放在哪、房间之间怎么连。
- `warehouse.room.json`、`factory.room.json`、`forest.room.json`：房间文件（`kind: "room"`），每个文件描述一个独立房间。

推荐用游戏内编辑器或 Unity 编辑器创建和修改，保存前会做校验；手写 JSON 时请照着下面的字段，并在编辑器里打开确认。

### 世界文件 main.world.json

| 字段 | 含义 |
|---|---|
| `worldId`、`bounds` | 世界 ID，以及含两端的整数边界 `min`/`max`。 |
| `rooms` | 每项含 `levelId`（与房间文件里的 `id` 一致）、`file`（相对世界文件的房间文件名）、`origin`（房间左下角的世界格坐标）、`forwardRequiresSolved`（进入该房前必须已解开的房间 ID 列表）。 |
| `connections` | 房间之间的门连接。每项含唯一 `id`、`mode`（`direct` 相邻直连，`teleport` 同图远距离传送）、`forwardFrom` 与 `forwardTo`（各含 `levelId` 和 `doorId`）、`allowReturn`（是否允许返回）。 |
| `start` | 起点：`levelId` 和 `spawnId`。 |
| `finish` | 终点：`endpoint`（某房间的某扇门）和 `requiresSolved`（通关须已解开的房间）。 |

```json
{
  "kind": "world", "formatVersion": 1, "worldId": "main",
  "bounds": { "min": [0, 0], "max": [63, 31] },
  "rooms": [
    { "levelId": "warehouse", "file": "warehouse.room.json", "origin": [0, 1], "forwardRequiresSolved": [] },
    { "levelId": "factory", "file": "factory.room.json", "origin": [9, 1], "forwardRequiresSolved": ["warehouse"] }
  ],
  "connections": [
    { "id": "link-1", "mode": "direct",
      "forwardFrom": { "levelId": "warehouse", "doorId": "exit" },
      "forwardTo": { "levelId": "factory", "doorId": "entry" },
      "allowReturn": true }
  ],
  "start": { "levelId": "warehouse", "spawnId": "spawn" },
  "finish": { "endpoint": { "levelId": "forest", "doorId": "exit" }, "requiresSolved": ["warehouse", "factory", "forest"] }
}
```

以上是 `main.world.json` 的节选，完整文件还包含森林房间及第二条连接。房间文件不保存世界位置，位置只写在世界文件的 `origin` 里。直连要求两扇门在世界格上正交相邻且朝向相反。

### 房间文件 *.room.json

| 字段 | 含义 |
|---|---|
| `id`、`name`、`themeId` | 房间 ID、显示名、主题（`warehouse`、`factory`、`forest`）。 |
| `width`、`height` | 房间宽高（格）。 |
| `terrain` | 地形网格，按行主序，共 `width × height` 项，从 y=0 那一行开始。每项 `category` 取 `void`、`floor`、`wall`、`door`、`openable`；门格用 `entity` 填门 ID，可开合地形（如藤门）带 `entity` 和 `state`。 |
| `objects` | 对象，每格最多一个：`box`（箱子）、`target`（目标）、`spawn`（出生点），含 `id`、`x`、`y`。 |
| `doors` | 门：`id`、`x`、`y`、`outX`/`outY`（朝向房外的单位向量）、`role`（`entry` 或 `exit`）。`primaryEntranceId` 指向主入口。 |
| `recoveryDevice` | 每房一个恢复装置：`kind`（`forklift`、`arm`、`tree`）、墙外坐标 `x`/`y`、`rotation`、`script`、`duration`，以及道具文字 `itemName`、`itemDescription`、`itemAcquiredHint`。 |
| `objectStates` | 可选的对象状态（森林里箱子在树与原木之间切换）。 |
| `decorations`、`assets`、`graphs`、`templates`、`instances` | 墙外装饰、导入的模型资源、行为图等，不用时留空数组即可。 |

坐标以房间左下角为 (0,0)，y 向北增大。仓库房间的对象与门节选：

```json
"objects": [
  { "id": "box-1", "category": "box", "x": 4, "y": 2 },
  { "id": "spawn", "category": "spawn", "x": 2, "y": 2 },
  { "id": "target-1", "category": "target", "x": 6, "y": 2 }
],
"doors": [
  { "id": "entry", "x": 0, "y": 3, "outX": -1, "outY": 0, "role": "entry" },
  { "id": "exit", "x": 8, "y": 3, "outX": 1, "outY": 0, "role": "exit" }
],
"primaryEntranceId": "entry"
```

常见约束：

- 箱子、目标、出生点只能放在地板格，箱子与目标不能同格，也不能放在主入口内侧的缓冲格上。
- 门朝向房外的一格必须是空格，朝向房内的一格必须是地板。
- 保存、导出和试玩要求有出生点、主入口和封闭的边界。
- 校验只检查结构与边界，不判断关卡是否有解，是否可解请自己试玩确认。

## 下载试玩

在 Releases 页面下载 Windows 压缩包，解压后运行 `Sokoban.exe`。
