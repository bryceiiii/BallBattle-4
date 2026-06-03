using UnityEngine;
using UnityEngine.UI;
using SpacetimeDB.Types; 
using SpacetimeDB;
using System.Collections.Generic;
using System; // 引入基础命名空间以使用 EventContext 等

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public InputField InputField;
    public GameObject canvasGo;
    private static Dictionary<int,GameObject>Entities = new Dictionary<int, GameObject>();
    private static Dictionary<int,GameObject>Circles = new Dictionary<int, GameObject>();
    private Identity localIdentity;
    // ✅ 定义一个便捷属性获取 Connection 实例，随时可以用 Conn 获取网络引用
    public DbConnection Conn => SpacetimeDBNetworkManager.Instance?.Db;

    void Start()
    {
        Application.runInBackground = true; // 确保应用在后台也能继续运行，保持网络连接,适用于调试和服务器环境
        // 先检查当前是否已经初始化
        if (Conn != null)
        {
            SubscribeToTables();
        }
        else
        {
            Debug.LogWarning("SpacetimeDB 尚未初始化，稍后连上服务器时会自动订阅表。");
            // 监听 SpacetimeDB 的连接成功回调
            SpacetimeDBNetworkManager.OnConnected += SubscribeToTables;
        }
    }

    private void SubscribeToTables()
    {
        // 为 localIdentity 赋值，需要判断是否有值（因为如果未连接成功是获取不到的）
        if (Conn != null && Conn.Identity.HasValue)
        {
            localIdentity = Conn.Identity.Value;
            Debug.Log($"已成功获取并赋值 localIdentity: {localIdentity}");
        }

        // 订阅 Food 表的数据插入事件
        Conn.Db.Food.OnInsert += OnFoodInserted;
        // 订阅 Food 表的数据删除事件
        Conn.Db.Food.OnDelete += OnFoodDelete;
        Conn.Db.Entity.OnUpdate += OnEntityUpdated;

        Conn.Db.Circle.OnInsert += OnCircleInserted;
        Conn.Db.Circle.OnDelete += OnCircleDeleted;
        Conn.Db.Circle.OnUpdate += OnCircleUpdated;

        Debug.Log("✅ GameManager 成功订阅所有表事件。");
    }

    private void OnEntityUpdated(EventContext context, Entity oldRow, Entity newRow)
    {
        if (Circles.TryGetValue(newRow.Id, out var go) == false) return;
        go.GetComponent<CircleController>().SetTargetPos(new Vector3(newRow.Position.X, newRow.Position.Y, 0));
        go.GetComponent<CircleController>().SetTargetScale(newRow.Mass);
    }
    private void OnCircleDeleted(EventContext context, Circle row)
    {
        // 通过字典查找GameObject,当 Circle 被删除时，移除对应的 GameObject
        if (Circles.Remove(row.EntityId, out var go))
        {
            Debug.Log($"[Circle 表更新]!玩家Circle 被删除！EntityId: {row.EntityId}");
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
        var player = Conn.Db.LoggedInPlayer.PlayerId.Find(row.PlayerId)??new Player{ Name = "Unknown" };
        GameObject circleGo = PrefabsManager.Instance.SpawnCircle(row.EntityId, entity.Position.X, entity.Position.Y, entity.Mass, player.Name);
        Circles.Add(row.EntityId, circleGo);
        if(player.Identity == localIdentity)
        {
            // 使用 AddFollowTarget 替代之前的 SetFollowTarget，将分裂出的新球也加入相机的跟随列表
            CameraContoller.Instance.AddFollowTarget(circleGo.transform);
            circleGo.GetComponent<CircleController>().isLocalPlayer = true;
        }
    }

    private void OnFoodDelete(EventContext ctx, Food deletedFood)
    {
        // 当 Food 被删除时，移除对应的 GameObject
        if (Entities.Remove(deletedFood.EntityId,out var go))
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

    public void OnButtonEnterGameClick()
    {
        canvasGo.SetActive(false);
        
        // 现在可以通过简短的 Conn 来操作，代码变得简单很多
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
        // 移除事件监听防泄漏
        SpacetimeDBNetworkManager.OnConnected -= SubscribeToTables;
        
        if (Conn != null)
        {
            Conn.Db.Food.OnInsert -= OnFoodInserted;
            Conn.Db.Food.OnDelete -= OnFoodDelete;
            Conn.Db.Circle.OnInsert -= OnCircleInserted;
            Conn.Db.Circle.OnDelete -= OnCircleDeleted;
        }
    }
    //查找玩家主球
    private Transform FindLocalMainBall()
    {
        foreach (var kv in Circles)
        {
            var ctr = kv.Value.GetComponent<CircleController>();
            if (ctr.isLocalPlayer)
                return kv.Value.transform;
        }
        return null;
    }
}