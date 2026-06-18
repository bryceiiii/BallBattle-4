using UnityEngine;
using UnityEngine.UI;
using SpacetimeDB.Types;
using SpacetimeDB;
using System.Collections.Generic;
using System;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public InputField InputField;
    public GameObject canvasGo;
    public GameObject bulletPrefab;      // 子弹预制体（拖拽）

    private static Dictionary<int, GameObject> Entities = new Dictionary<int, GameObject>();
    private static Dictionary<int, GameObject> Circles = new Dictionary<int, GameObject>();
    private static Dictionary<int, GameObject> Bullets = new Dictionary<int, GameObject>();

    private static Dictionary<int, List<int>> PlayerBallMap = new Dictionary<int, List<int>>();

    // ===== 每帧每实体只处理最后一次更新 =====
    private static HashSet<int> entitiesUpdatedThisFrame = new HashSet<int>();

    // ===== 护盾特效追踪 =====
    private static Dictionary<int, GameObject> shieldEffects = new Dictionary<int, GameObject>();

    private Identity localIdentity;
    public DbConnection Conn => SpacetimeDBNetworkManager.Instance?.Db;

    private bool isSubscribed = false;
    private bool _mainThreadSubscribed = false; // [新增] 主线程订阅标志

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 如果 canvasGo 存在，初始隐藏（等 LobbyUIController 或旧流程来显示）
        if (canvasGo != null) canvasGo.SetActive(false);
    }

    void Start()
    {
        Application.runInBackground = true;

        // 不再自动连接——连接由 LobbyUIController 或外部调用触发
        // 监听连接成功事件，在连接建立后再订阅表
        SpacetimeDBNetworkManager.OnConnected += SubscribeToTables;
    }

    private void SubscribeToTables()
    {
        if (isSubscribed) return;
        if (Conn == null) return;
        if (Conn.Db == null) return; // 连接尚未完全就绪

        // [Android 关键] 检测当前是否在 Unity 主线程
        // SpacetimeDB SDK 在 Android 上从后台线程调用 OnConnected 回调
        // 事件订阅（OnInsert += ...）在后台线程可能不会触发
        // 因此：后台线程直接 return，由 Update() 在主线程重试
        if (!IsMainThread())
        {
            Debug.LogWarning("[GameManager] SubscribeToTables 在后台线程被调用，忽略，等待主线程兜底");
            isSubscribed = true; // 标记为已订阅避免重复进入
            return;
        }

        if (Conn.Identity.HasValue)
        {
            localIdentity = Conn.Identity.Value;
            Debug.Log($"已成功获取并赋值 localIdentity: {localIdentity}");
        }

        Conn.Db.Food.OnInsert += OnFoodInserted;
        Conn.Db.Food.OnDelete += OnFoodDelete;
        Conn.Db.Entity.OnUpdate += OnEntityUpdated;
        Conn.Db.Circle.OnInsert += OnCircleInserted;
        Conn.Db.Circle.OnDelete += OnCircleDeleted;
        Conn.Db.Circle.OnUpdate += OnCircleUpdated;
        Conn.Db.Bullet.OnInsert += OnBulletInserted;
        Conn.Db.Bullet.OnDelete += OnBulletDeleted;
        Conn.Db.PlayerAmmo.OnInsert += OnPlayerAmmoInserted;
        Conn.Db.PlayerAmmo.OnUpdate += OnPlayerAmmoUpdated;
        Conn.Db.Shield.OnInsert += OnShieldInserted;
        Conn.Db.Shield.OnUpdate += OnShieldUpdated;
        Conn.Db.Shield.OnDelete += OnShieldDeleted;

        isSubscribed = true;
        _mainThreadSubscribed = true;
        Debug.Log("[GameManager] 已在主线程完成所有表事件订阅");

        // 调用 SubscribeToAllTables 请求服务端推送数据
        try
        {
            Conn.SubscriptionBuilder().SubscribeToAllTables();
            Debug.Log("[GameManager] 已调用 SubscribeToAllTables");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameManager] SubscribeToAllTables 失败: {e.Message}");
        }
    }

    /// <summary>
    /// 检测当前是否在 Unity 主线程。
    /// Android IL2CPP 上 SpacetimeDB SDK 会在后台线程调用 OnConnected
    /// </summary>
    private static bool IsMainThread()
    {
#if UNITY_ANDROID || UNITY_IOS
        // 简单方法：通过一个标志位（每次 Update 都在主线程设置）
        return _lastUpdateFrame == Time.frameCount;
#else
        return true; // PC 上 OnConnected 永远在主线程
#endif
    }

    private static int _lastUpdateFrame = -1;

    private void LateUpdate()
    {
        entitiesUpdatedThisFrame.Clear();
    }

    /// <summary>
    /// 同玩家球手动推开，替代 Rigidbody2D 物理碰撞，避免 SmoothDamp 振荡抖动。
    /// 每帧对所有同玩家球对做重叠检测，重叠则各推一半。
    private void OnEntityUpdated(EventContext context, Entity oldRow, Entity newRow)
    {
        if (entitiesUpdatedThisFrame.Contains(newRow.Id)) return;
        entitiesUpdatedThisFrame.Add(newRow.Id);

        // 更新玩家球
        if (Circles.TryGetValue(newRow.Id, out var go))
        {
            var ctrl = go.GetComponent<CircleController>();
            ctrl.SetTargetPos(new Vector3(newRow.Position.X, newRow.Position.Y, 0));
            ctrl.SetTargetScale(newRow.Mass);
            ctrl.SetHp(newRow.Hp, newRow.MaxHp);

            // 本地玩家球更新时同步 HUD
            if (ctrl.isLocalPlayer)
            {
                UpdateHudForLocalPlayer(newRow);
            }
        }

        // 更新子弹（SmoothDamp 插值）
        if (Bullets.TryGetValue(newRow.Id, out var bulletGo))
        {
            var trgPos = new Vector3(newRow.Position.X, newRow.Position.Y, 0);
            var ctrl = bulletGo.GetComponent<BulletController>();
            if (ctrl != null)
                ctrl.SetTargetPos(trgPos);
            else
                bulletGo.transform.position = trgPos;
        }
    }

    private void OnCircleDeleted(EventContext context, Circle row)
    {
        if (Circles.Remove(row.EntityId, out var go))
        {
            Debug.Log($"[Circle 表更新] 玩家Circle 被删除！EntityId: {row.EntityId}");

            RemoveFromPlayerBallMap(row.PlayerId, row.EntityId);
            GameObject.Destroy(go);

            // 检测本地玩家是否死亡（没有剩余球体）
            CheckLocalPlayerDeath();
        }
    }

    private void OnCircleUpdated(EventContext ctx, Circle oldC, Circle newC)
    {
        if (Circles.TryGetValue(newC.EntityId, out var go))
        {
            var ctrl = go.GetComponent<CircleController>();
            if (newC.IsMerging)
            {
                // 服务端在 splitFromEntityId 里存了合并目标球的大球 entity_id
                Transform mergeTarget = null;
                if (newC.SplitFromEntityId != 0 && Circles.TryGetValue(newC.SplitFromEntityId, out var targetGo))
                {
                    mergeTarget = targetGo.transform;
                }

                if (mergeTarget != null)
                {
                    ctrl.StartMergeAnim(mergeTarget);
                }
                else
                {
                    // 找不到目标（目标已被删除或异常），直接通知服务端完成合并
                    var conndb = SpacetimeDBNetworkManager.Instance?.Db;
                    if (conndb != null)
                    {
                        conndb.Reducers.FinishMerge(newC.EntityId);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 找指定玩家中质量最大（scale 最大）的球。
    /// </summary>
    private Transform FindBiggestBallOfPlayer(int playerId)
    {
        Transform biggest = null;
        float maxScale = 0f;

        foreach (var kv in Circles)
        {
            var ctr = kv.Value.GetComponent<CircleController>();
            if (ctr.playerId == playerId)
            {
                float s = kv.Value.transform.localScale.x;
                if (s > maxScale)
                {
                    maxScale = s;
                    biggest = kv.Value.transform;
                }
            }
        }
        return biggest;
    }

    private void OnCircleInserted(EventContext context, Circle row)
    {
        var entity = Conn.Db.Entity.Id.Find(row.EntityId);
        var player = Conn.Db.LoggedInPlayer.PlayerId.Find(row.PlayerId)
                     ?? new Player { Name = "Unknown" };

        GameObject circleGo = PrefabsManager.Instance.SpawnCircle(
            row.EntityId, entity.Position.X, entity.Position.Y, entity.Mass, player.Name
        );
        Circles.Add(row.EntityId, circleGo);

        var controller = circleGo.GetComponent<CircleController>();
        controller.entityId = row.EntityId;
        controller.playerId = row.PlayerId;
        if (entity != null) controller.SetHp(entity.Hp, entity.MaxHp); // 初始化 HP

        RegisterPlayerBall(row.PlayerId, row.EntityId);

        if (row.IsSplitting)
        {
            GameObject sourceGo = null;
            Vector3 serverEndPos = new Vector3(entity.Position.X, entity.Position.Y, 0);

            if (Circles.TryGetValue(row.SplitFromEntityId, out sourceGo))
            {
                controller.StartSplitAnim(sourceGo.transform.position, serverEndPos);
            }
            else
            {
                controller.SetTargetPos(serverEndPos);
            }
        }
        else
        {
            controller.SetTargetPos(new Vector3(entity.Position.X, entity.Position.Y, 0));
        }

        // 碰撞：同玩家球物理互推，不同玩家球穿透（服务端判吞噬）
        SetupCrossPlayerCollisionIgnore(controller);

        if (player.Identity == localIdentity)
        {
            CameraContoller.Instance.AddFollowTarget(circleGo.transform);
            controller.isLocalPlayer = true;
            controller.ApplyLocalPlayerVisual(); // Start() 执行时 isLocalPlayer 还是 false，这里补上

            // 为本地玩家球添加瞄准方向指示器
            var aimIndicator = circleGo.GetComponent<AimIndicator>();
            if (aimIndicator == null) aimIndicator = circleGo.AddComponent<AimIndicator>();
            aimIndicator.isLocalPlayer = true;
            aimIndicator.SetActive(true);
        }
    }

    private void RegisterPlayerBall(int playerId, int entityId)
    {
        if (!PlayerBallMap.ContainsKey(playerId))
        {
            PlayerBallMap[playerId] = new List<int>();
        }
        if (!PlayerBallMap[playerId].Contains(entityId))
        {
            PlayerBallMap[playerId].Add(entityId);
        }
    }

    private void RemoveFromPlayerBallMap(int playerId, int entityId)
    {
        if (PlayerBallMap.TryGetValue(playerId, out var list))
        {
            list.Remove(entityId);
            if (list.Count == 0)
            {
                PlayerBallMap.Remove(playerId);
            }
        }
    }

    /// <summary>
    /// 同玩家球 → 物理碰撞互推；不同玩家球 → 穿透（服务端判吞噬）
    /// </summary>
    private void SetupCrossPlayerCollisionIgnore(CircleController newController)
    {
        var thisCol = newController.GetComponent<CircleCollider2D>();
        if (thisCol == null) return;

        foreach (var kv in Circles)
        {
            if (kv.Key == newController.entityId) continue;
            var otherCtrl = kv.Value.GetComponent<CircleController>();
            if (otherCtrl == null) continue;

            if (otherCtrl.playerId != newController.playerId)
            {
                var otherCol = kv.Value.GetComponent<CircleCollider2D>();
                if (otherCol != null)
                    Physics2D.IgnoreCollision(thisCol, otherCol, true);
            }
        }
    }

    // ===== 食物相关 =====
    private void OnFoodDelete(EventContext ctx, Food deletedFood)
    {
        if (Entities.Remove(deletedFood.EntityId, out var go))
        {
            GameObject.Destroy(go);
        }
    }

    private void OnFoodInserted(EventContext ctx, Food newFood)
    {
        var entity = Conn.Db.Entity.Id.Find(newFood.EntityId);
        if (entity == null) return;

        GameObject foodGo;
        if (newFood.FoodType == 1)
            foodGo = PrefabsManager.Instance.SpawnHealthOrb(newFood.EntityId, entity.Position.X, entity.Position.Y, entity.Mass);
        else if (newFood.FoodType == 2)
            foodGo = PrefabsManager.Instance.SpawnSplitOrb(newFood.EntityId, entity.Position.X, entity.Position.Y, entity.Mass);
        else if (newFood.FoodType == 3)
            foodGo = PrefabsManager.Instance.SpawnShieldOrb(newFood.EntityId, entity.Position.X, entity.Position.Y, entity.Mass);
        else
            foodGo = PrefabsManager.Instance.SpawnFood(newFood.EntityId, entity.Position.X, entity.Position.Y, entity.Mass);

        Entities.Add(newFood.EntityId, foodGo);
    }

    // ===== 子弹相关 =====
    private void OnBulletInserted(EventContext ctx, Bullet newBullet)
    {
        var entity = Conn.Db.Entity.Id.Find(newBullet.EntityId);
        if (entity == null) return;

        // 按子弹类型选预制体：分裂弹用专属预制体，普通弹用默认预制体
        GameObject bulletGo;
        bool isSplit = newBullet.BulletType == 1;
        GameObject chosenPrefab = null;
        if (isSplit && PrefabsManager.Instance != null && PrefabsManager.Instance.splitBulletPrefab != null)
            chosenPrefab = PrefabsManager.Instance.splitBulletPrefab;
        else if (bulletPrefab != null)
            chosenPrefab = bulletPrefab;

        if (chosenPrefab != null)
        {
            bulletGo = Instantiate(chosenPrefab);
        }
        else
        {
            bulletGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(bulletGo.GetComponent<Collider>()); // 移除碰撞体
            var sr = bulletGo.AddComponent<SpriteRenderer>();
            var tex = new Texture2D(16, 16);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float dx = x - 7.5f, dy = y - 7.5f;
                    tex.SetPixel(x, y, (dx * dx + dy * dy) <= 56f ? Color.yellow : Color.clear);
                }
            tex.Apply();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f));
            var ctrl = bulletGo.AddComponent<BulletController>();
            ctrl.entityId = newBullet.EntityId;
        }
        bulletGo.name = "Bullet" + newBullet.EntityId;
        bulletGo.transform.position = new Vector3(entity.Position.X, entity.Position.Y, 0);
        bulletGo.transform.localScale = new Vector3(0.3f, 0.3f, 1f);

        // 子弹碰撞体设为 Trigger + Rigidbody Kinematic，防止推动球体导致抖动
        foreach (var col in bulletGo.GetComponentsInChildren<Collider2D>())
        {
            col.isTrigger = true;
        }
        var rb2d = bulletGo.GetComponent<Rigidbody2D>();
        if (rb2d != null)
        {
            rb2d.isKinematic = true;
        }

        // 确保有 BulletController
        var bulletCtrl = bulletGo.GetComponent<BulletController>();
        if (bulletCtrl == null) bulletCtrl = bulletGo.AddComponent<BulletController>();
        bulletCtrl.entityId = newBullet.EntityId;
        bulletCtrl.ownerPlayerId = newBullet.OwnerPlayerId; // 防止自伤

        Bullets.Add(newBullet.EntityId, bulletGo);
    }

    private void OnBulletDeleted(EventContext ctx, Bullet deletedBullet)
    {
        if (Bullets.Remove(deletedBullet.EntityId, out var go))
        {
            Destroy(go);
        }
    }

    // ===== 旧版UI（兼容旧场景） =====
    public void OnButtonEnterGameClick()
    {
        if (Conn == null)
        {
            Debug.LogError("SpacetimeDB 尚未连接！请先点击连接按钮。");
            return;
        }

        string playerName = InputField != null ? InputField.text.Trim() : "Player";
        if (string.IsNullOrEmpty(playerName)) playerName = "Player" + UnityEngine.Random.Range(100, 999);

        canvasGo.SetActive(false);
        Conn.Reducers.EnterGame(playerName);
    }

    // ===== 弹药相关 =====
    private void OnPlayerAmmoInserted(EventContext ctx, PlayerAmmo ammo)
    {
        UpdateHudAmmo(ammo);
    }

    private void OnPlayerAmmoUpdated(EventContext ctx, PlayerAmmo oldAmmo, PlayerAmmo newAmmo)
    {
        UpdateHudAmmo(newAmmo);
    }

    private void OnShieldInserted(EventContext ctx, Shield shield)
    {
        UpdateHudShield(shield);
    }

    private void OnShieldUpdated(EventContext ctx, Shield oldShield, Shield newShield)
    {
        UpdateHudShield(newShield);
    }

    private void OnShieldDeleted(EventContext ctx, Shield shield)
    {
        // 清理护盾特效
        if (shieldEffects.TryGetValue(shield.EntityId, out var fx))
        {
            Destroy(fx);
            shieldEffects.Remove(shield.EntityId);
        }

        // 检查护盾是否属于本地玩家
        foreach (var kv in Circles)
        {
            var ctrl = kv.Value?.GetComponent<CircleController>();
            if (ctrl != null && ctrl.isLocalPlayer && ctrl.entityId == shield.EntityId)
            {
                HudController.Instance?.ClearShield();
                return;
            }
        }
    }

    private void UpdateHudShield(Shield shield)
    {
        // 检查护盾是否属于本地玩家的球
        foreach (var kv in Circles)
        {
            var ctrl = kv.Value?.GetComponent<CircleController>();
            if (ctrl != null && ctrl.isLocalPlayer && ctrl.entityId == shield.EntityId)
            {
                HudController.Instance?.SetShield(shield.ExpireAtMs);
                HudController.Instance?.SetShieldBar(shield.ShieldHp, shield.ShieldMax);

                // 护盾特效
                if (PrefabsManager.Instance.shieldEffectPrefab != null && !shieldEffects.ContainsKey(shield.EntityId))
                {
                    var fx = Instantiate(PrefabsManager.Instance.shieldEffectPrefab, kv.Value.transform);
                    fx.transform.localPosition = Vector3.zero;
                    shieldEffects[shield.EntityId] = fx;
                }
                return;
            }
        }
    }

    private void UpdateHudAmmo(PlayerAmmo ammo)
    {
        if (localIdentity != null)
        {
            var player = Conn.Db.LoggedInPlayer.Identity.Find(localIdentity);
            if (player != null && player.PlayerId == ammo.PlayerId)
            {
                int max = PrefabsManager.Instance != null ? PrefabsManager.Instance.maxSplitAmmo : 5;
                HudController.Instance?.SetAmmoCountMax(1, ammo.AmmoSplit, max);
            }
        }
    }

    void Update()
    {
        _lastUpdateFrame = Time.frameCount; // [主线程标记] 每次 Update 都更新，供 IsMainThread() 判断

        // [Android 兜底] 后台线程订阅事件没生效时，主线程重新订阅
        if (!_mainThreadSubscribed && SpacetimeDBNetworkManager.Instance != null
            && SpacetimeDBNetworkManager.Instance.IsConnected)
        {
            Debug.Log("[GameManager] 主线程兜底：发现已连接但未在主线程订阅，重新订阅");
            // 重置 isSubscribed，让 SubscribeToTables 能再进来
            isSubscribed = false;
            SubscribeToTables();
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (Conn != null)
            {
                print($"当前食物总数: {Conn.Db.Food.Count}");
            }
        }

        // [调试] H 键对自己最大球扣 10 HP
        if (Input.GetKeyDown(KeyCode.H))
        {
            if (Conn != null)
            {
                var mainBall = FindLocalMainBall();
                if (mainBall != null)
                {
                    var ctrl = mainBall.GetComponent<CircleController>();
                    if (ctrl != null)
                    {
                        Conn.Reducers.DebugDamage(ctrl.entityId, 10f);
                        Debug.Log($"[调试] 对自己最大球 {ctrl.entityId} 扣血 10");
                    }
                }
                else
                {
                    Debug.LogWarning("[调试] 未找到本地玩家的球");
                }
            }
        }
    }

    private void OnDestroy()
    {
        SpacetimeDBNetworkManager.OnConnected -= SubscribeToTables;

        if (Conn != null)
        {
            Conn.Db.Food.OnInsert -= OnFoodInserted;
            Conn.Db.Food.OnDelete -= OnFoodDelete;
            Conn.Db.Entity.OnUpdate -= OnEntityUpdated;
            Conn.Db.Circle.OnInsert -= OnCircleInserted;
            Conn.Db.Circle.OnDelete -= OnCircleDeleted;
            Conn.Db.Circle.OnUpdate -= OnCircleUpdated;
            Conn.Db.Bullet.OnInsert -= OnBulletInserted;
            Conn.Db.Bullet.OnDelete -= OnBulletDeleted;
            Conn.Db.PlayerAmmo.OnInsert -= OnPlayerAmmoInserted;
            Conn.Db.PlayerAmmo.OnUpdate -= OnPlayerAmmoUpdated;
            Conn.Db.Shield.OnInsert -= OnShieldInserted;
            Conn.Db.Shield.OnUpdate -= OnShieldUpdated;
            Conn.Db.Shield.OnDelete -= OnShieldDeleted;
        }
    }

    /// <summary>
    /// 获取本地玩家主球的世界坐标位置。
    /// 供 PlayerInputController 等外部组件调用。
    /// </summary>
    public static Vector3 GetLocalMainBallPosition()
    {
        if (Instance == null) return Vector3.zero;
        var t = Instance.FindLocalMainBall();
        return t != null ? t.position : Vector3.zero;
    }

    private Transform FindLocalMainBall()
    {
        Transform mainBall = null;
        float maxMass = 0f;

        foreach (var kv in Circles)
        {
            var ctr = kv.Value.GetComponent<CircleController>();
            if (ctr.isLocalPlayer)
            {
                float mass = kv.Value.transform.localScale.x;
                if (mass > maxMass)
                {
                    maxMass = mass;
                    mainBall = kv.Value.transform;
                }
                if (mainBall == null)
                    mainBall = kv.Value.transform;
            }
        }
        return mainBall;
    }

    /// <summary>将本地玩家实体数据同步到 HUD</summary>
    private void UpdateHudForLocalPlayer(Entity ent)
    {
        if (HudController.Instance == null) return;

        // 汇总本地玩家所有球的质量和 HP
        float totalMass = 0f, totalHp = 0f, totalMaxHp = 0f;
        foreach (var kv in Circles)
        {
            var ctr = kv.Value.GetComponent<CircleController>();
            if (ctr != null && ctr.isLocalPlayer)
            {
                float d = kv.Value.transform.localScale.x; // d = diameter = sqrt(mass)/2
                totalMass += (d * 2f) * (d * 2f);          // mass = (2*diameter)^2
                totalHp += ctr.debugHp;
                totalMaxHp += ctr.debugMaxHp;
            }
        }

        HudController.Instance.SetHp(totalHp, totalMaxHp);
        HudController.Instance.SetMass(totalMass);
    }

    /// <summary>检查本地玩家是否已经没有球体（死亡）</summary>
    private void CheckLocalPlayerDeath()
    {
        foreach (var kv in Circles)
        {
            if (kv.Value != null && kv.Value.GetComponent<CircleController>()?.isLocalPlayer == true)
                return; // 还有本地玩家的球，没死
        }
        // 本地玩家没有剩余球体 → 显示死亡画面
        HudController.Instance?.ShowDeathScreen();
    }

    /// <summary>重新开始游戏（死亡画面按钮回调）</summary>
    public void RespawnPlayer()
    {
        // 调用 EnterGame 重新进入
        if (Conn != null)
        {
            string playerName = InputField != null ? InputField.text.Trim() : "Player";
            if (string.IsNullOrEmpty(playerName)) playerName = "Player" + UnityEngine.Random.Range(100, 999);
            Conn.Reducers.EnterGame(playerName);
        }
    }
}
