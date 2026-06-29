using UnityEngine;

/// <summary>
/// 网络监控 HUD 显示。挂到任意 GameObject 上，自动创建屏幕角落浮层。
/// </summary>
public class NetworkMonitorHUD : MonoBehaviour
{
    [Header("显示设置")]
    [Tooltip("HUD 在屏幕上的锚点位置")]
    public TextAnchor anchor = TextAnchor.UpperLeft;
    [Tooltip("字体大小")]
    public int fontSize = 14;
    [Tooltip("背景透明度")]
    [Range(0f, 1f)]
    public float bgAlpha = 0.5f;
    [Tooltip("是否显示详细列表（最近 N 条记录）")]
    public bool showDetails = true;
    [Range(0, 20)]
    public int detailLines = 5;

    [Header("自动启动")]
    [Tooltip("勾选后 Start 时自动开始监控")]
    public bool autoStart = true;
    [Tooltip("自动启动的目标 NetworkMonitor（留空则查找场景中第一个）")]
    public NetworkMonitor targetMonitor;

    private GUIStyle _boxStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _labelWarnStyle;
    private GUIStyle _labelBadStyle;
    private GUIStyle _tinyStyle;

    void Start()
    {
        if (autoStart && targetMonitor == null)
            targetMonitor = FindObjectOfType<NetworkMonitor>();

        // 如果启用了连接联动，不在 Start 里强制启动，由 NetworkMonitor 自行监听连接事件
        if (autoStart && targetMonitor != null && !targetMonitor.listenConnection)
            targetMonitor.StartMonitoring();
    }

    void OnGUI()
    {
        if (targetMonitor == null) return;

        InitStyles();
        DrawHUD();
    }

    void InitStyles()
    {
        if (_boxStyle != null) return;

        _boxStyle = new GUIStyle(GUI.skin.box);
        _boxStyle.normal.background = MakeTex(2, 2, new Color(0, 0, 0, bgAlpha));

        _labelStyle = new GUIStyle(GUI.skin.label);
        _labelStyle.fontSize = fontSize;
        _labelStyle.normal.textColor = Color.white;

        _labelWarnStyle = new GUIStyle(_labelStyle);
        _labelWarnStyle.normal.textColor = Color.yellow;

        _labelBadStyle = new GUIStyle(_labelStyle);
        _labelBadStyle.normal.textColor = Color.red;

        _tinyStyle = new GUIStyle(_labelStyle);
        _tinyStyle.fontSize = Mathf.Max(10, fontSize - 2);
        _tinyStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
    }

    void DrawHUD()
    {
        var m = targetMonitor;
        float w = 280f;
        int lines = showDetails ? 8 + detailLines : 6;  // ponytail: +1 for FPS
        float h = lines * (fontSize + 4) + 16;
        Rect rect = GetAnchorRect(w, h, 10, 10);

        GUI.Box(rect, "", _boxStyle);
        GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, rect.height - 16));
        GUILayout.BeginVertical();

        // 标题
        GUILayout.Label($"◇ 网络监控 {(m.IsRunning ? "●" : "○")}", _labelStyle);

        // FPS — 低于 30 变黄，低于 20 变红
        var fpsStyle = m.CurrentFPS < 20f ? _labelBadStyle :
                       m.CurrentFPS < 30f ? _labelWarnStyle : _labelStyle;
        GUILayout.Label($"FPS:    {m.CurrentFPS,6:F0}", fpsStyle);

        // 核心指标
        var pingStyle = m.CurrentPingMs > m.lagThresholdMs ? _labelBadStyle :
                        m.CurrentPingMs > m.lagThresholdMs * 0.6f ? _labelWarnStyle : _labelStyle;

        GUILayout.Label($"Ping:   {m.CurrentPingMs,6:F1} ms  (avg: {m.AvgPingMs:F1})", pingStyle);
        GUILayout.Label($"Jitter: {m.CurrentJitter,6:F1} ms", _labelStyle);
        GUILayout.Label($"丢包率: {m.PacketLossRate * 100f,6:F1}%  ({m.TimeoutCount}/{m.TotalSamples})", _labelStyle);

        // 卡顿状态
        if (m.IsCurrentlyLagging)
            GUILayout.Label("⚠ 正在卡顿中...", _labelBadStyle);
        else if (m.CurrentLag != null && m.CurrentLag.Value.durationMs > 0)
            GUILayout.Label($"最近卡顿: {m.CurrentLag.Value.durationMs:F0}ms {LagEvent.SeverityName(m.CurrentLag.Value.severity)}", _labelWarnStyle);

        // 极值
        GUILayout.Label($"范围:   {m.MinPingMs:F0} ~ {m.MaxPingMs:F0} ms  |  卡顿: {m.LagEvents.Count}次", _tinyStyle);

        // 详情
        if (showDetails && m.Samples.Count > 0)
        {
            GUILayout.Space(4);
            int start = Mathf.Max(0, m.Samples.Count - detailLines);
            for (int i = start; i < m.Samples.Count; i++)
            {
                var s = m.Samples[i];
                var st = s.isTimeout ? _labelBadStyle : _tinyStyle;
                GUILayout.Label($"{s.pingMs,5:F0}ms {s.jitter,4:F0}j {(s.isTimeout ? " TIMEOUT" : "")}", st);
            }
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    Rect GetAnchorRect(float w, float h, float padX, float padY)
    {
        return anchor switch
        {
            TextAnchor.UpperRight => new Rect(Screen.width - w - padX, padY, w, h),
            TextAnchor.LowerLeft => new Rect(padX, Screen.height - h - padY, w, h),
            TextAnchor.LowerRight => new Rect(Screen.width - w - padX, Screen.height - h - padY, w, h),
            TextAnchor.MiddleCenter => new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h),
            _ => new Rect(padX, padY, w, h), // UpperLeft
        };
    }

    static Texture2D MakeTex(int w, int h, Color col)
    {
        var pix = new Color[w * h];
        for (int i = 0; i < pix.Length; i++) pix[i] = col;
        var tex = new Texture2D(w, h);
        tex.SetPixels(pix);
        tex.Apply();
        return tex;
    }

}
