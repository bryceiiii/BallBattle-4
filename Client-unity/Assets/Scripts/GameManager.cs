using UnityEngine;
using UnityEngine.UI;
using SpacetimeDB.Types; 
using SpacetimeDB; // 引入基础命名空间以使用 EventContext 等

public class GameManager : MonoBehaviour
{
    public InputField InputField;
    public GameObject canvasGo;

    // ✅ 定义一个便捷属性获取 Connection 实例，随时可以用 Conn 获取网络引用
    public DbConnection Conn => SpacetimeDBNetworkManager.Instance?.Db;

    void Start()
    {
        // 确保网络管理器已经初始化了 Db 对象
        if (Conn != null)
        {
            // 订阅 Food 表的数据插入事件
            Conn.Db.Food.OnInsert += OnFoodInserted;
        }
        else
        {
            Debug.LogWarning("SpacetimeDB 未初始化，无法订阅 Food 表。");
        }
    }

    // 当服务端或本地同步到 Food 表新增一行数据时，会触发此回调
    private void OnFoodInserted(EventContext ctx, Food newFood)
    {
        // 返回的 ctx 包含触发这次插入的 reducer 信息，newFood 则是新插入的行数据
        //Debug.Log($"[Food 表更新] 发现新的食物插入！当前总数: {Conn.Db.Food.Count}");
        var entity = Conn.Db.Entity.Id.Find(newFood.EntityId);
        //Debug.Log($"[Food 表更新] 发现新的食物插入！EntityId: {newFood.EntityId}, Position: ({entity.Position.X}, {entity.Position.Y}), Mass: {entity.Mass}");
        PrefabsManager.Instance.SpawnFood(newFood.EntityId, entity.Position.X, entity.Position.Y, entity.Mass);
        // 你可以在这里读取 newFood 的属性，并在 Unity 场景中生成对应的预制体实体
        // 例如： Instantiate(foodPrefab, new Vector3(newFood.x, 0, newFood.y), Quaternion.identity);
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
        // 记得在脚本销毁时取消订阅，防止内存泄漏和空引用异常
        if (Conn != null)
        {
            Conn.Db.Food.OnInsert -= OnFoodInserted;
        }
    }
}