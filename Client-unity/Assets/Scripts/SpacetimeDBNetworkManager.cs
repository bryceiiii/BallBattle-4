// 必须同时引入这两个命名空间！
using SpacetimeDB;
using SpacetimeDB.ClientApi;
using UnityEngine;
using SpacetimeDB.Types; // 引入由 SpacetimeDB 自动生成的类型所在的命名空间
using System; // 引入 System 以便使用 Exception

public class SpacetimeDBNetworkManager : MonoBehaviour
{
    // 全局单例，方便其他脚本调用
    public static SpacetimeDBNetworkManager Instance { get; private set; }

    [Header("服务器配置")]
    public string moduleName = "ballbattle4"; // 你的模块名
    public string host = "ws://localhost:3000"; // 服务器地址

    // ✅ 改为 SpacetimeDB 新版 1.11 SDK 自动生成的基于当前模块的上下文对象
    public DbConnection Db { get; private set; }

    private void Awake()
    {
        // 单例初始化
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // 🚨 使用新版 DbConnectionBuilder 来建立连接与事件回调
        DbConnectionBuilder<DbConnection> builder = DbConnection.Builder();
        builder.WithUri(host);

        // 修复：使用 WithDatabaseName 替代不存在的 WithModuleNameOrAddress
        builder.WithDatabaseName(moduleName);

        builder.OnConnect(delegate
        {
            Debug.Log("<color=green>✅ 成功连接 SpacetimeDB 服务器！</color>");
            // Db.SubscriptionBuilder().SubscribeToAllTables(); // 通常连接成功后我们需要订阅所有表
        });

        // 拆分出独立的错误捕获函数
        builder.OnConnectError((Exception error) =>
        {
            Debug.LogError($"❌ 连接失败：{error}");
        });

        // 生成 Db 对象并在 Build 阶段直接启动连接
        Db = builder.Build();
    }

    private void Update()
    {
        // 💥 只有这样消息才会实际去排队处理，网络连接和回调才能被正常触发更新！
        // Db?.Update(); // 移除不存在的方法
        Db?.FrameTick(); // 使用正确的方法
    }

    private void OnDestroy()
    {
        // 退出时断开连接
        Db?.Disconnect();
    }
}