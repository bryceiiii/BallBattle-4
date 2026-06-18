// 必须同时引入这两个命名空间！
using SpacetimeDB;
using SpacetimeDB.ClientApi;
using SpacetimeDB.Types;
using UnityEngine;
using System;

public class SpacetimeDBNetworkManager : MonoBehaviour
{
    public static SpacetimeDBNetworkManager Instance { get; private set; }

    public enum ConnectionMode { Local, LAN, Cloud }

    [Header("连接模式")]
    public ConnectionMode connectionMode = ConnectionMode.Local;

    [Header("LAN / 远程服务器设置")]
    public string remoteHost = "192.168.1.100";
    public int remotePort = 3000;

    [Header("模块名称")]
    public string localModuleName = "ballbattle4";
    public string cloudModuleName = "ballbattle4v2";

    const string CloudUri = "wss://maincloud.spacetimedb.com";
    public string ActiveUri { get; private set; }
    public string ActiveModuleName { get; private set; }
    public DbConnection Db { get; private set; }
    public bool IsConnected { get; private set; }

    public static event Action OnConnected;
    public static event Action<string> OnConnectFailed;

    private float _connectStartTime;
    private bool _fallbackTriggered;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Connect()
    {
        AuthToken.Init();

#if UNITY_ANDROID || UNITY_IOS
        if (connectionMode == ConnectionMode.Local)
        {
            OnConnectFailed?.Invoke("手机端请使用局域网模式");
            return;
        }
#endif

        switch (connectionMode)
        {
            case ConnectionMode.Local:
                ActiveUri = "http://127.0.0.1:3000";
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

        Debug.Log($"[SpacetimeDB] 模式={connectionMode} | URI={ActiveUri} | 模块={ActiveModuleName}");

        DbConnectionBuilder<DbConnection> builder = DbConnection.Builder();
        builder.WithUri(ActiveUri);
        builder.WithDatabaseName(ActiveModuleName);
        builder.OnConnect(HandleConnect);
        builder.OnConnectError(HandleConnectError);

        Db = builder.Build();
        _connectStartTime = Time.time;
        _fallbackTriggered = false;
    }

    public void ConnectToLAN(string host, int port, string moduleName = "ballbattle4")
    {
        connectionMode = ConnectionMode.LAN;
        remoteHost = host;
        remotePort = port;
        localModuleName = moduleName;
        Connect();
    }

    public void ConnectLocal(string moduleName = "ballbattle4")
    {
        connectionMode = ConnectionMode.Local;
        localModuleName = moduleName;
        Connect();
    }

    public void ConnectCloud(string moduleName = "ballbattle4v2")
    {
        connectionMode = ConnectionMode.Cloud;
        cloudModuleName = moduleName;
        Connect();
    }

    // ================================================================
    //  HandleConnect：SDK 回调（PC=主线程，Android=后台线程）
    //
    //  IsConnected = true 会被 LobbyUIController.Update() 轮询检测到
    //  不依赖 OnConnected 事件来更新 UI（Android 后台线程事件被吞）
    //
    //  SubscribeToAllTables 由 GameManager.Update() 在主线程唯一调用，
    //  避免后台线程订阅导致 GameObject 操作被 IL2CPP 吞掉
    // ================================================================
    private void HandleConnectError(Exception error)
    {
        IsConnected = false;
        Debug.LogError($"<color=red>连接失败：{error.Message}</color>");
        OnConnectFailed?.Invoke(error.Message);
    }

    private void HandleConnect(DbConnection conn, Identity identity, string token)
    {
        IsConnected = true;
        Debug.Log($"<color=green>已连接到 {ActiveUri}</color>");
        AuthToken.SaveToken(token);

        // [关键] 不在这里调用 SubscribeToAllTables()
        // HandleConnect 在 Android 上是后台线程调用，订阅事件分发会在后台线程
        // 事件回调里的 GameObject 操作（Instantiate/transform）会被 IL2CPP 吞掉
        // 改为由 GameManager.Update() 在主线程上重新订阅
        // 这里只发一个 OnConnected 事件，PC 上能正常订阅，Android 上靠 GameManager 兜底
        OnConnected?.Invoke();
    }

    private void Update()
    {
        try { Db?.FrameTick(); } catch (Exception e) { Debug.LogError($"FrameTick: {e}"); }

        // Android 兜底：HandleConnect 完全不触发时（3秒超时），强制标记 IsConnected
        // SubscribeToAllTables 由 GameManager.Update() 在主线程调用
        if (!IsConnected && !_fallbackTriggered && Db != null && _connectStartTime > 0
            && Time.time - _connectStartTime > 3f)
        {
            _fallbackTriggered = true;
            Debug.Log("[SpacetimeDB] 兜底：3秒超时，强制设置 IsConnected=true（订阅由 GameManager 完成）");
            IsConnected = true;
        }
    }

    private void OnDestroy()
    {
        Db?.Disconnect();
    }
}
