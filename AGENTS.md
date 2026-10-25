# AGENTS.md — BallBattle-4

## 一句话
Unity 2022.3.62f3c1 客户端 + SpacetimeDB v2.2.0（C#→WASM）服务端，多人球球对战，服务端权威。

## 开工必读（只看这两个，不要全库模糊搜索）
- 本文件
- `.workbuddy/memory/MEMORY.md` — 架构约定 / 物理插值 / MCP 规则 / UI 约定

## 仓库地图
| 路径 | 说明 |
|------|------|
| `server-csharp/spacetimedb/Lib.cs` | 全部服务端逻辑，唯一手写文件（~1454 行）；头部常量区 = 平衡参数 |
| `Client-unity/Assets/Scripts/` | 客户端脚本（~20 个）。核心：`GameManager`(918) / `CircleController`(555) / `SpacetimeDBNetworkManager`(281) / `PrefabsManager`(96) |
| `Client-unity/Assets/Scripts/GameSession/` | 双实现：`GameSession.cs`（接口 + 在线）/ `LocalGameSession.cs` / `LocalGameWorld.cs`（1564，单机模拟 + bot AI） |
| `Client-unity/Assets/autogen/` | SpacetimeDB 生成代码，**禁止手改** |
| `GDD.md` / `联机部署指南.md` / `手机端部署指南.md` | 设计 / 部署 |
| `handoff-*.md` | 历史交接记录，按需查 |

## 命令
- 起服：双击 `启动服务器.bat`（内部执行 `spacetime start -l 0.0.0.0:3000 --in-memory` + publish）
- 发布服务端：`cd server-csharp/spacetimedb && spacetime publish -s http://127.0.0.1:3000 ballbattle4`
- 重生成客户端代码（仅接口变更时）：`spacetime generate --lang csharp --out-dir Assets/autogen`

## 关键架构事实
- 在线 / 单机共用 `IGameSession` 接口；渲染层订阅 `GameTables` 事件，不感知当前模式
- 服务端 tick 25Hz（`SERVER_DELTA = 0.04`）；客户端 SmoothDamp 插值，**不做客户端预测**
- 单机 bot = 普通 `Player` + `isBot` 标记；`LocalGameWorld.UpdateBotDecisions()` 每 200ms 决策一次，默认 5 个
- 权威在服务端：客户端只发输入（方向 / 射击 / 分裂），不做圆-圆物理、不做吃球判定

## 铁律
- 改 `Lib.cs` 逻辑 → 只 publish；改表 / Reducer 签名 → publish + generate；只改客户端脚本 → 都不用
- 平衡数值只在 `Lib.cs` 头部常量区 / `LocalGameWorld` 常量区改，不得散落
- Unity 资产（`.unity`/`.prefab`/`.asset`/`.mat`）优先走 unityMCP，禁止手改序列化文件
- 客户端圆-圆一律穿透（`IgnoreCollision`），吃球 / 合并全由服务端判定
- 子弹命中特效用每帧数学距离判定，**不用物理 `OnTriggerEnter2D`**

## 工作纪律（省 token / 防返工）
- 只读上面地图里的文件；读大文件先给行号范围，不整文件读
- 一次只做一个任务；改动前列出「白名单文件」，先给 5 行内计划再动手
- 只产出最小 diff，禁止重写整个文件
- 报错只贴第一条 + `文件:行号`，不贴整段日志
