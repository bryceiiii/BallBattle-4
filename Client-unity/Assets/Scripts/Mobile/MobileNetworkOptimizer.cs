using UnityEngine;

/// <summary>
/// 手机端网络优化器。
/// 在移动数据网络（3G/4G/5G）下自动降低发送频率，减少带宽消耗和发热。
/// 适配 SpacetimeDB 架构：调整客户端输入发送间隔。
/// </summary>
public class MobileNetworkOptimizer : MonoBehaviour
{
    public static MobileNetworkOptimizer Instance { get; private set; }

    [Header("发送间隔(秒) — 越小越灵敏，越大越省流量")]
    public float pcSendInterval = 0.025f;       // PC: 25ms (40Hz)
    public float mobileSendInterval = 0.050f;   // 手机: 50ms (20Hz)

    [Header("手机额外节流（WiFi/蜂窝网络）")]
    public float mobileThrottleFactor = 0.75f;  // 在wifi下也适度降低

    /// <summary>当前实际使用的发送间隔</summary>
    public float CurrentSendInterval { get; private set; }

    private bool _isMobile;

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
#if UNITY_ANDROID || UNITY_IOS
        _isMobile = true;
#else
        _isMobile = false;
#endif

        CurrentSendInterval = _isMobile
            ? mobileSendInterval * mobileThrottleFactor
            : pcSendInterval;

        Debug.Log($"[MobileNetworkOptimizer] 平台={(_isMobile?"手机":"PC")} | 发送间隔={CurrentSendInterval*1000:F0}ms");

        // 将优化后的间隔应用到 PlayerInputController
        ApplyToPlayerInputController();
    }

    private void ApplyToPlayerInputController()
    {
        // PlayerInputController 已内置去重逻辑（方向值变化才发送），无需额外调整间隔
        // 手机端 MobileInputController 自身管理发送频率
    }

    // ===== 公开接口 =====
    /// <summary>获取当前平台推荐的移动方向发送频率</summary>
    public float GetRecommendedDirSendInterval()
    {
        return CurrentSendInterval;
    }
}
