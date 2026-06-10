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
    public GameObject bulletPrefab;  // 子弹预制体（拖拽）

    private static Dictionary<int, GameObject> Entities = new Dictionary<int, GameObject>();
    private static Dictionary<int, GameObject> Circles = new Dictionary<int, GameObject>();
    private static Dictionary<int, GameObject> Bullets = new Dictionary<int, GameObject>();

    private static Dictionary<int, List<int>> PlayerBallMap = new Dictionary<int, List<int>>();

    // ===== 每帧每实体只处理最后一次更新 =====
    private static HashSet<int> entitiesUpdatedThisFrame = new HashSet<int>();

    private Identity localIdentity;
    public DbConnection Conn => SpacetimeDBNetworkManager.Instance?.Db;

    private bool isSubscribed = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        Application.runInBackground = true;

        if (Conn != null)
        {
            SubscribeToTables();
        }
        else
        {
            Debug.LogWarning("SpacetimeDB 尚未初始化，稍后连上服务器时会自动订阅表。");
            SpacetimeDBNetworkManager.OnConnected += SubscribeToTables;
        }
    }

    private void SubscribeToTables()
    {
        if (isSubscribed) return;

        if (Conn != null && Conn.Identity.HasValue)
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

        isSubscribed = true;
        Debug.Log("? GameManager 成功订阅所有表事件。");
    }

    private void LateUpdate()
    {
        entitiesUpdatedThisFrame.Clear();
    }

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
        }

        // 更新子弹（用 MovePosition 确保物理触发事件正常工作）
        if (Bullets.TryGetValue(newRow.Id, out var bulletGo))
        {
            var trgPos = new Vector2(newRow.Position.X, newRow.Position.Y);
            var rb2d = bulletGo.GetComponent<Rigidbody2D>();
            if (rb2d != null)
                rb2d.MovePosition(trgPos);
            else
                bulletGo.transform.position = new Vector3(trgPos.x, trgPos.y, 0);
        }
    }

    private void OnCircleDeleted(EventContext context, Circle row)
    {
        if (Circles.Remove(row.EntityId, out var go))
        {
            Debug.Log($"[Circle 表更新] 玩家Circle 被删除！EntityId: {row.EntityId}");

            RemoveFromPlayerBallMap(row.PlayerId, row.EntityId);
            GameObject.Destroy(go);
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
                    var conn = SpacetimeDBNetworkManager.Instance?.Db;
                    if (conn != null)
                    {
                        conn.Reducers.FinishMerge(newC.EntityId);
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

        // 碰撞逻辑：同玩家球物理互推（不穿透），不同玩家球忽略碰撞（穿透，大吞小）
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
    /// 碰撞模型（球球大作战经典规则）：
    /// 同玩家球 → 物理碰撞互推（Rigidbody2D 自然处理）
    /// 不同玩家球 → 穿透（大球覆盖小球，服务器判吞噬）
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

            // 不同玩家 → 穿透
            if (otherCtrl.playerId != newController.playerId)
            {
                var otherCol = kv.Value.GetComponent<CircleCollider2D>();
                if (otherCol != null)
                {
                    Physics2D.IgnoreCollision(thisCol, otherCol, true);
                }
            }
            // 同玩家 → 不设置 IgnoreCollision（默认物理碰撞互推）
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
        Debug.Log($"[Food 表更新] 发现新的食物插入！EntityId: {newFood.EntityId}, Position: ({entity.Position.X}, {entity.Position.Y}), Mass: {entity.Mass}");
        GameObject foodGo = PrefabsManager.Instance.SpawnFood(newFood.EntityId, entity.Position.X, entity.Position.Y, entity.Mass);
        Entities.Add(newFood.EntityId, foodGo);
    }

    // ===== 子弹相关 =====
    private void OnBulletInserted(EventContext ctx, Bullet newBullet)
    {
        var entity = Conn.Db.Entity.Id.Find(newBullet.EntityId);
        if (entity == null) return;

        // 使用预制体创建子弹（如果没有预制体，用默认小球体）
        GameObject bulletGo;
        if (bulletPrefab != null)
        {
            bulletGo = Instantiate(bulletPrefab);
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

    // ===== UI =====
    public void OnButtonEnterGameClick()
    {
        canvasGo.SetActive(false);

        if (Conn != null)
        {
            Conn.Reducers.EnterGame(InputField.text);
        }
        else
        {
            Debug.LogError("SpacetimeDB 网络尚未连接或者尚未初始化！");
        }
    }

    void Update()
    {
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
}
