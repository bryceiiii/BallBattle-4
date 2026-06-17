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

    public enum ConnectionMode
    {
        Local,       // 本机单机 (127.0.0.1:3000)
        LAN,         // 局域网/远程自定义IP
        Cloud        // SpacetimeDB 云端
    }

    [Header("连接模式")]
    [Tooltip("Local=本机单机 | LAN=局域网/远程IP | Cloud=SpacetimeDB云端")]
    public ConnectionMode connectionMode = ConnectionMode.Local;

    [Header("LAN / 远程服务器设置")]
    [Tooltip("远程服务器 IP 地址")]
    public string remoteHost = "192.168.1.100";
    [Tooltip("远程服务器端口 (SpacetimeDB默认3000)")]
    public int remotePort = 3000;

    [Header("模块名称")]
    [Tooltip("本地/LAN模式的模块名")]
    public string localModuleName = "ballbattle4";
    [Tooltip("云端模式的模块名")]
    public string cloudModuleName = "ballbattle4v2";

    // 云服务地址（通常不需要修改）
    const string CloudUri = "wss://maincloud.spacetimedb.com";

    /// <summary>当前实际使用的连接URI</summary>
    public string ActiveUri { get; private set; }

    /// <summary>当前实际使用的模块名</summary>
    public string ActiveModuleName { get; private set; }

    /// <summary>数据库连接对象</summary>
    public DbConnection Db { get; private set; }

    /// <summary>是否已连接</summary>
    public bool IsConnected { get; private set; }

    // 连接成功事件
    public static event Action OnConnected;
    // 连接失败事件
    public static event Action<string> OnConnectFailed;

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

    /// <summary>
    /// 使用当前Inspector中的设置开始连接。
    /// 可以在运行时修改 connectionMode/remoteHost/remotePort 后调用。
    /// </summary>
    public void Connect()
    {
        AuthToken.Init();

        // 手机端检测：Local 模式（127.0.0.1）在真机上指向手机自身，无法连接 PC 服务器
        bool isMobile = Application.platform == RuntimePlatform.Android
                     || Application.platform == RuntimePlatform.IPhonePlayer;

        switch (connectionMode)
        {
            case ConnectionMode.Local:
                if (isMobile)
                {
                    string msg = "⚠️ 手机端不支持\"本机服务器\"模式！\n"
                               + "127.0.0.1 在手机上指向手机自身，不是你的电脑。\n"
                               + "请改用局域网模式，输入你电脑的局域网IP（如 192.168.x.x）。";
                    Debug.LogError(msg);
                    OnConnectFailed?.Invoke(msg);
                    return; // 阻止连接
                }
                ActiveUri = $"http://127.0.0.1:3000";
                ActiveModuleName = localModuleName;
                break;

            case ConnectionMode.LAN:
                ActiveUri = $"http://{remoteHost}:{remotePort}";
                ActiveModuleName = localModuleName;
                break;

            case ConnectionMode.Cloud:
                ActiveUri = CloudUri;
                ActiveModuleName = cloudModuleName;
                break;
        }

        Debug.Log($"[SpacetimeDB] 平台={Application.platform} | 模式={connectionMode} | URI={ActiveUri} | 模块={ActiveModuleName}");

        DbConnectionBuilder<DbConnection> builder = DbConnection.Builder();
        builder.WithUri(ActiveUri);
        builder.WithDatabaseName(ActiveModuleName);
        builder.OnConnect(HandleConnect);
        builder.OnConnectError(HandleConnectError);

        Db = builder.Build();
    }

    /// <summary>
    /// 运行时动态设置LAN模式并连接。
    /// 适合从UI输入IP后调用。
    /// </summary>
    public void ConnectToLAN(string host, int port, string moduleName = "ballbattle4")
    {
        connectionMode = ConnectionMode.LAN;
        remoteHost = host;
        remotePort = port;
        localModuleName = moduleName;
        Connect();
    }

    /// <summary>
    /// 运行时连接本地服务器。
    /// </summary>
    public void ConnectLocal(string moduleName = "ballbattle4")
    {
        connectionMode = ConnectionMode.Local;
        localModuleName = moduleName;
        Connect();
    }

    /// <summary>
    /// 运行时连接云端。
    /// </summary>
    public void ConnectCloud(string moduleName = "ballbattle4v2")
    {
        connectionMode = ConnectionMode.Cloud;
        cloudModuleName = moduleName;
        Connect();
    }

    private void HandleConnectError(Exception error)
    {
        IsConnected = false;
        string msg = $"❌ 连接失败：{error.Message}";
        Debug.LogError($"<color=red>{msg}</color>");
        OnConnectFailed?.Invoke(error.Message);
    }

    private void HandleConnect(DbConnection conn, Identity identity, string token)
    {
        IsConnected = true;
        Debug.Log($"<color=green>已连接到 {ActiveUri} (模块: {ActiveModuleName})</color>");
        AuthToken.SaveToken(token);

        // 先触发 OnConnected 让 GameManager 绑定好所有回调，
        // 再订阅表数据，避免数据先到而回调未注册。
        OnConnected?.Invoke();

        conn.SubscriptionBuilder().SubscribeToAllTables();
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
