using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单次网络探测采样点数据
/// </summary>
[Serializable]
public struct NetworkSample
{
    public int index;
    public double timestamp;      // 采样时间（游戏启动秒数）
    public float pingMs;          // 往返延迟 ms
    public float tcpConnectMs;    // TCP 握手耗时 ms
    public bool isTimeout;        // 是否超时(=丢包)
    public float jitter;          // 本次与上次的延迟变化绝对值（首次为 0）

    public override string ToString() =>
        $"[{index}] t={timestamp:F2}s ping={pingMs:F1}ms connect={tcpConnectMs:F1}ms " +
        $"timeout={isTimeout} jitter={jitter:F1}ms";
}

/// <summary>
/// 卡顿事件记录
/// </summary>
[Serializable]
public struct LagEvent
{
    public double startTime;
    public double endTime;
    public float durationMs;        // 持续时长 ms
    public float peakPingMs;        // 峰值延迟
    public int severity;            // 0=轻 1=中 2=重
    public int lostPackets;         // 卡顿期间丢包数

    public static string SeverityName(int s) => s switch
    {
        0 => "轻微",
        1 => "中等",
        2 => "严重",
        _ => "未知"
    };

    public override string ToString() =>
        $"[卡顿] {startTime:F2}s → {endTime:F2}s 持续{durationMs:F0}ms " +
        $"峰值{peakPingMs:F0}ms 丢包{lostPackets} {SeverityName(severity)}";
}
