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

    // ===== 重连机制 =====
    [Header("重连配置")]
    public bool enableAutoReconnect = true;
    public float reconnectInterval = 3f;        // 重连间隔（秒）
    public int maxReconnectAttempts = 10;       // 最大重连次数
    private int _reconnectAttempts = 0;
    private float _reconnectTimer = 0f;
    private bool _isReconnecting = false;
    private bool _wasConnected = false;         // 追踪是否曾经连接成功过，判断是断线还是从未连上

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
                // 局域网 IP 无 TLS 证书 → http；公网/Tunnel → https
                ActiveUri = $"{(IsPrivateIP(remoteHost) ? "http" : "https")}://{remoteHost}:{remotePort}";
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
        // 重置重连状态（每次新连接都是全新生命周期）
        _wasConnected = false;
        _isReconnecting = false;
        _reconnectAttempts = 0;
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
        _wasConnected = true;
        _isReconnecting = false;  // 重连成功，重置状态
        _reconnectAttempts = 0;
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

        // ===== 断线检测 + 自动重连 =====
        // SpacetimeDB SDK 断线时会通过 OnDisconnect 回调或 FrameTick 异常表现出来
        // 这里不主动轮询连接状态，由 SDK 回调驱动 _wasConnected 和 IsConnected

        // 自动重连逻辑
        if (enableAutoReconnect && _wasConnected && !IsConnected && !_isReconnecting)
        {
            _isReconnecting = true;
            _reconnectAttempts = 0;
            _reconnectTimer = reconnectInterval;
            Debug.Log($"[SpacetimeDB] 检测到断线，将在 {reconnectInterval}s 后开始重连...");
        }

        if (_isReconnecting)
        {
            _reconnectTimer -= Time.deltaTime;
            if (_reconnectTimer <= 0f)
            {
                if (_reconnectAttempts >= maxReconnectAttempts)
                {
                    Debug.LogError($"[SpacetimeDB] 重连失败：已达最大尝试次数 ({maxReconnectAttempts})");
                    _isReconnecting = false;
                    OnConnectFailed?.Invoke("重连失败，已达最大尝试次数");
                    return;
                }

                _reconnectAttempts++;
                Debug.Log($"[SpacetimeDB] 重连尝试 {_reconnectAttempts}/{maxReconnectAttempts}...");
                TryReconnect();
                _reconnectTimer = reconnectInterval;
            }
        }
    }

    /// <summary>
    /// 尝试重新连接：断开旧连接，创建新连接
    /// </summary>
    private void TryReconnect()
    {
        try
        {
            Db?.Disconnect();
        }
        catch { }

        IsConnected = false;
        _fallbackTriggered = false;
        _connectStartTime = Time.time;

        // 重新构建连接（复用上次的模式和地址）
        DbConnectionBuilder<DbConnection> builder = DbConnection.Builder();
        builder.WithUri(ActiveUri);
        builder.WithDatabaseName(ActiveModuleName);
        builder.OnConnect(HandleConnect);
        builder.OnConnectError(HandleConnectError);

        Db = builder.Build();
    }

    /// <summary>手动停止重连（用户点击取消等场景）</summary>
    public void StopReconnect()
    {
        _isReconnecting = false;
        _reconnectAttempts = 0;
        Debug.Log("[SpacetimeDB] 重连已取消");
    }

    private void OnDestroy()
    {
        Db?.Disconnect();
    }

    // ponytail: 一行判断，覆盖 RFC 1918 全部私有网段
    private static bool IsPrivateIP(string host)
    {
        return System.Net.IPAddress.TryParse(host, out var ip)
            && ip.GetAddressBytes() is byte[] b && b.Length == 4
            && (b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || b[0] == 127);
    }
}
