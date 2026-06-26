# BallBattle-4 项目记忆

## 项目概况
- 类型: 多人实时球球大作战变体（类 Agar.io + 射击元素）
- 技术栈: Unity 2022.3.62f3c1 + SpacetimeDB v2.2.0 (C#→WASM 服务端)
- 网络模型: 服务端权威，客户端 SmoothDamp 插值
- 多开测试: ParrelSync

## 网络模型
- 服务端权威
- **客户端预测 (Dead Reckoning)**: 本地玩家即时响应输入，服务端异步温和修正（blendFactor 自适应）
- **服务器 tick**: 25Hz（WAN优化，从 50Hz 降低以减少带宽/CPU）
- **远程插值**: SmoothDamp 0.15s（适配 100-200ms RTT）
- **Quick Tunnel**: Cloudflare QUIC 协议（UDP），避免 TCP 队头阻塞
- **自动重连**: 3s 间隔，最多 10 次

## 项目结构
- `server-csharp/spacetimedb/Lib.cs` — 全部服务端逻辑（唯一手写文件，~1219行）
- `Client-unity/Assets/Scripts/` — 12个客户端脚本（含新增 NetworkDebugDisplay.cs）
- `Client-unity/Assets/autogen/` — SpacetimeDB 自动生成代码
- 所有平衡参数硬编码在 Lib.cs 头部常量区

## 客户端脚本清单
- SpacetimeDBNetworkManager, GameManager, CircleController, PlayerInputController
- CameraContoller, HudController, PrefabsManager, BulletController, AimIndicator
- LobbyUIController, ServerAddressAutoFetcher, **NetworkDebugDisplay** (新增)
- 网络调试: F3 切换 NetworkDebugDisplay，实时显示 RTT/连接状态

## 已实现系统
移动(质量减速)、吞噬(大吃小)、分裂(空格)、自动合并(贴合等待)、食物生成、断线保留
HP系统、子弹射击、特殊食物球(5种)、弹药栏、排行榜、重生机制、护盾/Buff

## 设计文档
- `GDD.md` — 游戏设计文档 v0.1

## 用户偏好
- 使用中文交流
- 偏好"质量即资源"的核心设计理念
