using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 手机端网络测试监控器。
/// 使用 TCP 连接计时探测服务器可达性，
/// 兼容 Android/iOS，无需原生插件。
/// </summary>
public class NetworkMonitor : MonoBehaviour
{
    public static NetworkMonitor Instance { get; private set; }

    #region —— Inspector 配置 ——

    [Header("目标服务器")]
    [Tooltip("测试用的服务器地址（域名或IP，不含协议）")]
    public string testHost = "100.86.230.25";
    [Tooltip("服务器端口")]
    public int testPort = 3000;

    [Header("采样参数")]
    [Tooltip("采样间隔（秒）")]
    [Range(0.1f, 5f)]
    public float sampleInterval = 0.5f;
    [Tooltip("发起请求后的超时时间（秒）")]
    [Range(1f, 30f)]
    public float requestTimeout = 3f;

    [Header("连接联动")]
    [Tooltip("勾选后，仅在 SpacetimeDB 连接成功后才开始检测，断线自动停止")]
    public bool listenConnection = true;

    [Header("卡顿判定")]
    [Tooltip("Ping 超过此值视为进入卡顿状态 (ms)")]
    [Range(50f, 2000f)]
    public float lagThresholdMs = 200f;
    [Tooltip("最低连续超时次数，达到后标记严重丢包")]
    [Range(1, 20)]
    public int consecutiveTimeoutForSevere = 3;
    [Tooltip("卡顿结束后多久判定为恢复（秒）")]
    [Range(0.5f, 10f)]
    public float lagRecoveryTime = 1f;

    #endregion

    #region —— 运行时状态 ——

    public bool IsRunning { get; private set; }
    public float CurrentPingMs { get; private set; }
    public float CurrentJitter { get; private set; }
    public float PacketLossRate { get; private set; }  // 0~1
    public float AvgPingMs { get; private set; }
    public float MinPingMs { get; private set; } = float.MaxValue;
    public float MaxPingMs { get; private set; }
    public int TotalSamples { get; private set; }
    public int TimeoutCount { get; private set; }
    public bool IsCurrentlyLagging { get; private set; }
    public LagEvent? CurrentLag { get; private set; }
    public float CurrentFPS { get; private set; }

    public List<NetworkSample> Samples { get; } = new();
    public List<LagEvent> LagEvents { get; } = new();
    public event Action<LagEvent> OnLagStart;
    public event Action<LagEvent> OnLagEnd;
    public event Action<NetworkSample> OnNewSample;

    private float _lastSampleTime;
    private float _lastPingMs;
    private float _pingSum;
    private bool _isLagging;
    private float _lagStartTime;
    private float _lagPeakPing;
    private int _lagLostPackets;
    private int _consecutiveTimeouts;
    private int _sampleIndex;
    private bool _connectionChecked;  // 轮询防重
    private float _smoothedDelta;    // ponytail: FPS 指数平滑

    #endregion

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); }
    }

    void Start()
    {
        if (listenConnection)
        {
            SpacetimeDBNetworkManager.OnConnected += OnGameConnected;
            SpacetimeDBNetworkManager.OnConnectFailed += OnGameDisconnected;
        }
    }

    void OnDestroy()
    {
        SpacetimeDBNetworkManager.OnConnected -= OnGameConnected;
        SpacetimeDBNetworkManager.OnConnectFailed -= OnGameDisconnected;
    }

    private void OnGameConnected()
    {
        if (!IsRunning && listenConnection)
        {
            UnityEngine.Debug.Log("[NetworkMonitor] 游戏已连接，开始网络检测");
            StartMonitoring();
        }
    }

    private void OnGameDisconnected(string reason)
    {
        _connectionChecked = false;  // 重置轮询标记，断线后可重新检测
        if (IsRunning && listenConnection)
        {
            UnityEngine.Debug.Log($"[NetworkMonitor] 游戏断线({reason})，停止网络检测");
            StopMonitoring();
        }
    }

    public void StartMonitoring()
    {
        if (IsRunning) return;
        IsRunning = true;
        _lastSampleTime = Time.time;
        Samples.Clear();
        LagEvents.Clear();
        _sampleIndex = 0;
        TimeoutCount = 0;
        TotalSamples = 0;
        MinPingMs = float.MaxValue;
        MaxPingMs = 0;
        _pingSum = 0;
        _lastPingMs = 0;
        CurrentPingMs = 0;
        CurrentJitter = 0;
        PacketLossRate = 0;
        AvgPingMs = 0;
        UnityEngine.Debug.Log($"[NetworkMonitor] 开始监控，目标={testHost}:{testPort}，间隔={sampleInterval}s");
    }

    public void StopMonitoring()
    {
        if (!IsRunning) return;
        IsRunning = false;
        if (_isLagging) EndLag(Time.time);
        UnityEngine.Debug.Log($"[NetworkMonitor] 停止监控。共 {Samples.Count} 样本，{LagEvents.Count} 次卡顿");
    }

    void Update()
    {
        // 指数平滑 FPS
        _smoothedDelta += (Time.unscaledDeltaTime - _smoothedDelta) * 0.1f;
        CurrentFPS = _smoothedDelta > 0.0001f ? 1f / _smoothedDelta : 0f;

        // —— Android IL2CPP 兼容：轮询 SpacetimeDB 连接状态 ——
        // SpacetimeDBNetworkManager.OnConnected 事件在 Android 后台线程触发，
        // IL2CPP 的 managed/native 边界会吞掉事件，导致 OnGameConnected 收不到。
        // 所以这里用轮询 IsConnected 做兜底 —— 和 LobbyUIController 同理。
        if (listenConnection && !_connectionChecked)
        {
            var net = SpacetimeDBNetworkManager.Instance;
            if (net != null && net.IsConnected)
            {
                _connectionChecked = true;
                OnGameConnected();
            }
        }

        if (!IsRunning) return;
        if (Time.time - _lastSampleTime < sampleInterval) return;
        _lastSampleTime = Time.time;

        // 异步执行采样，不阻塞主线程
        _ = DoSampleAsync(Time.time, _sampleIndex++);
    }

    // ==================== 核心探测 ====================

    /// <summary>
    /// 异步执行一次采样：TCP 连接计时 = 网络可达性 + 延迟。
    /// 删掉了 HTTP HEAD —— SpacetimeDB 端口是 WebSocket，HTTP 请求永远返回非 200，以前 100% 误报丢包。
    /// </summary>
    private async Task DoSampleAsync(float sampleTime, int idx)
    {
        float pingMs = 0;
        bool timeout = false;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();
            var connectTask = tcp.ConnectAsync(testHost, testPort);
            var timeoutTask = Task.Delay((int)(requestTimeout * 1000));
            if (await Task.WhenAny(connectTask, timeoutTask) == connectTask)
            {
                sw.Stop();
                pingMs = (float)sw.Elapsed.TotalMilliseconds;
            }
            else
            {
                sw.Stop();
                pingMs = requestTimeout * 1000f;
                timeout = true;
            }
        }
        catch
        {
            sw.Stop();
            pingMs = requestTimeout * 1000f;
            timeout = true;
        }

        // ---- 后处理 ----
        if (timeout)
        {
            TimeoutCount++;
            _consecutiveTimeouts++;
        }
        else
        {
            _consecutiveTimeouts = 0;
            if (pingMs < MinPingMs) MinPingMs = pingMs;
            if (pingMs > MaxPingMs) MaxPingMs = pingMs;
            _pingSum += pingMs;
        }

        float jitter = (_lastPingMs > 0) ? Mathf.Abs(pingMs - _lastPingMs) : 0f;
        _lastPingMs = pingMs;

        var sample = new NetworkSample
        {
            index = idx,
            timestamp = sampleTime,
            pingMs = pingMs,
            tcpConnectMs = pingMs,  // ponytail: TCP 连接时间 = 延迟
            isTimeout = timeout,
            jitter = jitter
        };

        Samples.Add(sample);
        while (Samples.Count > 300) Samples.RemoveAt(0);
        TotalSamples++;
        CurrentPingMs = pingMs;
        CurrentJitter = jitter;
        PacketLossRate = TotalSamples > 0 ? (float)TimeoutCount / TotalSamples : 0f;
        AvgPingMs = (TotalSamples - TimeoutCount) > 0 ? _pingSum / (TotalSamples - TimeoutCount) : 0f;
        OnNewSample?.Invoke(sample);

        CheckLag(sample);
    }

    private void CheckLag(NetworkSample sample)
    {
        if (sample.isTimeout || sample.pingMs >= lagThresholdMs)
        {
            if (!_isLagging)
            {
            _isLagging = true;
            _lagStartTime = (float)sample.timestamp;
            _lagPeakPing = sample.pingMs;
            _lagLostPackets = sample.isTimeout ? 1 : 0;
            IsCurrentlyLagging = true;
            OnLagStart?.Invoke(default); // 触发卡顿开始回调
            }
            else
            {
                if (sample.pingMs > _lagPeakPing) _lagPeakPing = sample.pingMs;
                if (sample.isTimeout) _lagLostPackets++;
            }
        }
        else if (_isLagging)
        {
            // 恢复正常，判断是否需要结束卡顿
            if (sample.timestamp - _lagStartTime >= lagRecoveryTime)
            {
                EndLag(sample.timestamp);
            }
        }
    }

    private void EndLag(double endTime)
    {
        float duration = (float)(endTime - _lagStartTime) * 1000f;
        int severity;
        if (_consecutiveTimeouts >= consecutiveTimeoutForSevere)
            severity = 2; // 严重
        else if (_lagPeakPing > lagThresholdMs * 2.5f)
            severity = 1; // 中等
        else
            severity = 0; // 轻微

        var evt = new LagEvent
        {
            startTime = _lagStartTime,
            endTime = endTime,
            durationMs = duration,
            peakPingMs = _lagPeakPing,
            severity = severity,
            lostPackets = _lagLostPackets
        };

        LagEvents.Add(evt);
        while (LagEvents.Count > 50) LagEvents.RemoveAt(0);  // ponytail: 上限
        CurrentLag = evt;
        OnLagEnd?.Invoke(evt);
        _isLagging = false;
        IsCurrentlyLagging = false;
        UnityEngine.Debug.Log($"[NetworkMonitor] {evt}");
    }

}
