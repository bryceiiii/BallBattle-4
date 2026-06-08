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

    private static Dictionary<int, GameObject> Entities = new Dictionary<int, GameObject>();
    private static Dictionary<int, GameObject> Circles = new Dictionary<int, GameObject>();

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

        isSubscribed = true;
        Debug.Log("✅ GameManager 成功订阅所有表事件。");
    }

    private void LateUpdate()
    {
        entitiesUpdatedThisFrame.Clear();
    }

    private void OnEntityUpdated(EventContext context, Entity oldRow, Entity newRow)
    {
        if (entitiesUpdatedThisFrame.Contains(newRow.Id)) return;
        entitiesUpdatedThisFrame.Add(newRow.Id);

        if (Circles.TryGetValue(newRow.Id, out var go) == false) return;
        var ctrl = go.GetComponent<CircleController>();
        ctrl.SetTargetPos(new Vector3(newRow.Position.X, newRow.Position.Y, 0));
        ctrl.SetTargetScale(newRow.Mass);
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
            var mainTrans = FindLocalMainBall();
            if (newC.IsMerging && mainTrans != null)
            {
                ctrl.StartMergeAnim(mainTrans);
            }
        }
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

        // 同玩家球忽略碰撞：服务端处理吞噬/合并/聚拢，客户端不需要同玩家物理碰撞
        SetupSiblingCollisionIgnore(row.PlayerId, controller);

        if (player.Identity == localIdentity)
        {
            CameraContoller.Instance.AddFollowTarget(circleGo.transform);
            controller.isLocalPlayer = true;
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
    /// 同玩家所有球之间忽略碰撞。
    /// 不同玩家球之间由 Rigidbody2D 物理引擎自然处理（速度驱动 + drag 衰减 → 无抖动贴边滑动）。
    /// </summary>
    private void SetupSiblingCollisionIgnore(int playerId, CircleController newController)
    {
        if (!PlayerBallMap.TryGetValue(playerId, out var siblingIds)) return;

        foreach (var siblingId in siblingIds)
        {
            if (siblingId == newController.entityId) continue;
            if (Circles.TryGetValue(siblingId, out var siblingGo))
            {
                var siblingCtrl = siblingGo.GetComponent<CircleController>();
                var otherCol = siblingGo.GetComponent<CircleCollider2D>();
                var thisCol = newController.GetComponent<CircleCollider2D>();
                if (thisCol != null && otherCol != null)
                {
                    Physics2D.IgnoreCollision(thisCol, otherCol, true);
                }
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
        Debug.Log($"[Food 表更新] 发现新的食物插入！EntityId: {newFood.EntityId}, Position: ({entity.Position.X}, {entity.Position.Y}), Mass: {entity.Mass}");
        GameObject foodGo = PrefabsManager.Instance.SpawnFood(newFood.EntityId, entity.Position.X, entity.Position.Y, entity.Mass);
        Entities.Add(newFood.EntityId, foodGo);
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
