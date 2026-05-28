// 必须同时引入这两个命名空间！
using SpacetimeDB;
using SpacetimeDB.ClientApi;
using SpacetimeDB.Types; 
using UnityEngine;
using System;

public class SpacetimeDBNetworkManager : MonoBehaviour
{
    // 全局单例，方便其他脚本调用
    public static SpacetimeDBNetworkManager Instance { get; private set; }

    public enum Environment
    {
        Local,
        Cloud
    }

    [Header("服务器配置")]
    [Tooltip("选择要连接的服务器环境")]
    public Environment serverEnvironment = Environment.Local;

    const string LocalUri = "http://127.0.0.1:3000";
    const string CloudUri = "wss://maincloud.spacetimedb.com";

    public DbConnection Db { get; private set; }

    private void Awake()
    {
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
        AuthToken.Init();// 初始化认证系统

        // 动态选择模块名
        string moduleName = serverEnvironment == Environment.Cloud ? "ballbattle4v2" : "ballbattle4";
        string activeUri = serverEnvironment == Environment.Cloud ? CloudUri : LocalUri;

        Debug.Log($"[SpacetimeDB] 正在连接到 {serverEnvironment} 服务器: {activeUri} 模块: {moduleName}");

        DbConnectionBuilder<DbConnection> builder = DbConnection.Builder();
        builder.WithUri(activeUri);
        builder.WithDatabaseName(moduleName);

        builder.OnConnect(HandleConnect);
        builder.OnConnectError(HandleConnectError);

        Db = builder.Build();
    }

    // 增加一个静态委托，当连接成功、Db 初始化完成后通知 GameManager 等脚本
    public static event Action OnConnected;

    private void HandleConnectError(Exception error)
    {
        Debug.LogError($"<color=red>❌ 连接 SpacetimeDB 服务器失败：{error.Message}</color>");
    }

    private void HandleConnect(DbConnection conn, Identity identity, string token)
    {
        Debug.Log("<color=green>✅ 成功连接 SpacetimeDB 服务器！</color>");
        AuthToken.SaveToken(token);
        print(token);
        print(identity);

        conn.SubscriptionBuilder().SubscribeToAllTables();// 连接成功后订阅所有表，确保数据能够同步到客户端
        
        // 触发连接成功事件，让依赖网络的对象 (如 GameManager) 开始订阅
        OnConnected?.Invoke();
    }

    private void Update()
    {
        Db?.FrameTick();
    }

    private void OnDestroy()
    {
        Db?.Disconnect();
    }
}