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

## 射击协议（重要约定）
- `ShootBullet(float,float,int)` 客户端一律发**归一化方向向量**（基于主球+瞄准点），不再发世界坐标目标点。
- 服务器对所有球用同一方向**平行发射**（不再按 DB 第一个球算方向）。
- 参数名保留 targetX/targetY，reducer 签名类型不变 → schema 不变，改逻辑只需 `spacetime publish`，无需重新 generate。

## 平台输入
- `PlatformInputManager` 是唯一权威，IsMobileMode 在 Awake 确定；PC 禁用场景烘焙的 MobileInputController+MobileTouchCanvas，移动禁用 PlayerInputController。
- `AimIndicator` 用 `PlatformInputManager.IsMobileMode` 判断分支（移动=右摇杆，PC=鼠标）。
- 瞄准摇杆用 `RawDirection`（跟手），`useRawDirection` 开关开启；移动摇杆仍用 SmoothDamp 平滑。

## 物理/插值（重要约定）
- 客户端圆-圆之间**一律 IgnoreCollision(true)**（同玩家/不同玩家都穿透）。吃球/合并判定全在服务端，客户端不做圆-圆物理，避免 depenetration 把深重叠球一次性推开造成瞬移/抖动。
- `CircleController.FixedUpdate` 的 SmoothDamp 必须传 maxSpeed（local=30, remote=35），防止目标跳远时无限速追平=瞬移。球速基准 14，加速×1.8≈25。
- 同队球防重叠由**服务端权威分离**负责（`Lib.cs` MoveAllPlayer 里 `SEPARATION_FACTOR=0.5` 温和推开重叠的同队球到相切），移动中/静止都生效；客户端不做圆-圆物理。
- 不搞客户端预测（SmoothDamp + maxSpeed 足够平滑）。

## 子弹命中特效判定（重要约定）
- 客户端子弹命中特效**不用物理触发器 `OnTriggerEnter2D`**（多子弹聚簇/顿帧/kinematic 直接移 transform 会漏判），改用 `BulletController` 每帧**手动数学距离判定**：遍历 `GameManager.Circles`，`dist <= 球半径 + hitRadius` 即命中，三点采样（cur/mid/prev）防隧穿。服务端仍权威判定伤害。

## 设计文档
- `GDD.md` — 游戏设计文档 v0.1

## 用户偏好
- 使用中文交流
- 偏好"质量即资源"的核心设计理念
