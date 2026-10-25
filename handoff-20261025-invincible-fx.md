# Handoff — 复活无敌期动画

> 结论先行：**无敌逻辑已存在，缺的只是视觉表现。** 不需要新增机制，只需要表现层。

## 一、现状（已核实）

| 环节 | 位置 | 现状 |
|------|------|------|
| 数据字段 | `Lib.cs:149` `Player.invincible_until_ms` | ✅ 存在（绝对时间戳 ms，0=非无敌） |
| 服务端设置 | `Lib.cs:339` `player.invincible_until_ms = now + 5000` | ✅ 复活即无敌 5 秒 |
| 服务端拦截·吞噬 | `Lib.cs:650` | ✅ 无敌玩家不会被吃 |
| 服务端拦截·子弹 | `Lib.cs:968` | ✅ 无敌玩家不掉血 |
| 单机设置 | `LocalGameWorld.cs:403 / 425` `_timeMs + 12000` | ⚠️ **12 秒，与联机 5 秒不一致** |
| 单机拦截 | `LocalGameWorld.cs:623 / 768`，bot 选目标 `:1116` | ✅ 逻辑生效 |
| 客户端视觉 | 全项目 0 处引用 `InvincibleUntilMs` | ❌ **缺口在这里** |

数据链路已通：
- `CircleController.playerId`（`CircleController.cs:117`，public）
- `Session.FindPlayerByPlayerId(playerId)` 返回 `Player`，含 `InvincibleUntilMs`
- 单机：`LocalGameWorld.UpdatePlayer()`（`:1444`）会 `RaisePlayerUpdate`；联机：`SpacetimeGameSession` 监听 `LoggedInPlayer.OnUpdate`
- 挂子特效的现有范式：`GameManager.cs:646` 把 `shieldEffectPrefab` 实例化到球 transform 下

## 二、开工前必须先决策的 3 件事

1. **无敌时长统一**：联机 5000ms / 单机 12000ms。动画按哪个时长播？建议先统一为 **5 秒**（改 `LocalGameWorld.cs:403/425` 的 12000→5000，纯客户端，无需 publish）。若要改服务端时长 → 改 `Lib.cs:339` → 需 `spacetime publish`。
2. **没有"无敌结束"事件**：服务端到期只是时间自然过期，字段不会被清零，**不会推 OnUpdate**。所以客户端必须**自己计时**（收到字段变化时记录本地起点 + 配置时长），不能等服务端通知。
3. **球会分裂成多个**：无敌是按 `playerId` 的，不是按球。每个球都要各自挂视觉，判断依据统一用 `playerId → Player.InvincibleUntilMs`。

## 三、四种方案

### 方案 A — 呼吸闪烁（最省事，零美术资源）⭐ 快速验证首选
- 球体 `SpriteRenderer.color` 的 alpha 做 1 → 0.35 → 1 呼吸（周期约 0.2s），无敌结束恢复原色。
- 复用 `ShieldBuffController` 已有的"改 color + 定时恢复"模式（`:37-54`）。
- **优点**：0 资源、1 个脚本、当天可上、性能零开销。**缺点**：朴素，玩家不一定理解含义。
- 改动：新建 `InvincibleVisual.cs` + `CircleController` 挂载 1 行。

### 方案 B — 复用护盾特效预制体（最快出"高级感"）
- 无敌期实例化已有的 `shieldEffectPrefab` 到球身，结束销毁。
- **优点**：视觉好、复用 Animator。**缺点**：**语义冲突**——玩家会以为是护盾（护盾是独立机制，可被打碎），易混淆。不推荐。

### 方案 C — 独立"复活无敌"光环预制体 ⭐⭐ 推荐
- 新建 `RespawnInvincibleFx.prefab`（六边形罩/上升粒子/光环，任选其一）+ `InvincibleVisual.cs` 控制生命周期：出场 → 循环 → 渐隐。
- 结束前 1 秒可做轻微脉冲提示"快结束了"。
- **优点**：语义清晰、和护盾区分、可扩展。**缺点**：需要 1 个美术资源。

### 方案 D — 进度环 + 半透明罩（信息量最高）
- 球外一圈圆形进度（`Image.fillAmount` 或 shader），随时间走完，同时半透明罩渐隐。
- **优点**：玩家能读到"还剩几秒"。**缺点**：成本最高，UI 跟随球缩放/朝向要处理，手机端多一层开销。

## 四、推荐

**方案 C 为主 + 方案 A 兜底**：先用 A 跑通"能识别无敌状态并计时"的骨架（1 小时），再把表现替换成 C 的预制体。两者共用同一个 `InvincibleVisual.cs`，只换渲染部分。

---

## 五、交接给 DeepSeekHarness（5 项格式，可直接复制）

### 【推荐版本：方案 C】

```
1. 本轮唯一目标
为"复活后无敌期"增加可视动画。无敌逻辑已存在（Lib.cs:339 设置 invincible_until_ms，
客户端无任何表现），本轮只做客户端表现层，不改无敌规则。

2. 允许改动的精确路径列表
- Client-unity/Assets/Scripts/InvincibleVisual.cs        （新建：读 playerId → Player.InvincibleUntilMs，计时并驱动特效）
- Client-unity/Assets/Scripts/CircleController.cs        （仅：在球初始化时挂载 InvincibleVisual，1~2 处）
- Client-unity/Assets/Scripts/PrefabsManager.cs          （仅：新增 public GameObject invincibleFxPrefab 字段）
- Client-unity/Assets/Prefabs/RespawnInvincibleFx.prefab （新建：可先用占位 Sprite/粒子，后续换美术）
- Client-unity/Assets/Scripts/GameSession/LocalGameWorld.cs （仅：第 403/425 行 12000d → 5000d，统一时长）

3. 禁止触碰
默认沿用：Client-unity/Assets/autogen/** + 白名单以外全部。
额外禁止：server-csharp/spacetimedb/Lib.cs（本轮不改服务端；
若需改 5 秒时长，另行批准后再 publish）。

4. 验收标准
1) 复活后该玩家的每个球周围出现无敌视觉，持续与无敌时长一致（约 5s）后自动消失；
   无敌期间分裂出的新球同样带视觉。
2) 无敌期内被更大球吞噬 / 被子弹击中均不扣血（不得破坏任何现有无敌逻辑）；
   联机与单机两种模式表现一致。

5. 验证方式
- Unity 无编译错误；
- 单机模式：死亡 → 点 Respawn → 可见无敌动画，约 5s 后消失；
- 联机模式：A 玩家复活，B 玩家客户端也能看到 A 的无敌动画；
- 战斗校验：无敌期内被吃/中弹不掉血。
```

### 【快速版：方案 A，只做闪烁】

把上面第 2 项替换为：

```
2. 允许改动的精确路径列表
- Client-unity/Assets/Scripts/InvincibleVisual.cs   （新建，仅改 SpriteRenderer.color 做 alpha 呼吸）
- Client-unity/Assets/Scripts/CircleController.cs   （挂载 InvincibleVisual）
- Client-unity/Assets/Scripts/GameSession/LocalGameWorld.cs （仅 403/425 行 12000d → 5000d）
```
其余 1/3/4/5 不变（第 2 项验收里"出现无敌视觉"改为"球体呼吸闪烁"）。

---

## 六、实现要点（给实现者，避免踩坑）

- **不要信"结束事件"**：服务端到期不清零、不推送。必须 `Time.time` 自己计时。
- **起点判定**：`InvincibleUntilMs` 由 0/过去 变成"未来值"的那一刻 = 无敌开始。用"值发生变化"判断即可，无需服务端时钟。
- **一个 playerId 多个球**：每个球独立实例化特效，但状态源统一是 Player 表，不要存 per-circle 布尔。
- **单机模式也必须生效**：`LocalGameWorld.UpdatePlayer` 已会 `RaisePlayerUpdate`，无需额外改事件；但若用轮询，`LocalGameSession.FindPlayerByPlayerId` 是字典查找，成本极低。
- **不要在 `MoveAllPlayer` 之类服务端逻辑里加视觉判断**，客户端零介入服务端。
