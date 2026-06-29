using UnityEngine;
using UnityEngine.UI;

[UnityEngine.Scripting.Preserve]
public class PerformanceMonitor : MonoBehaviour
{
    [Header("UI 引用")]
    public Text fpsText;
    public Text lagText;

    [Header("FPS 配置")]
    [Range(0.2f, 1f)] public float refreshInterval = 0.5f;
    public Color fpsGoodColor = Color.green;
    public float fpsGoodThreshold = 30f;
    public Color fpsWarnColor = Color.yellow;
    public float fpsWarnThreshold = 15f;

    [Header("卡顿检测")]
    public float lagThreshold = 0.1f;
    public float lagClearSec = 0.5f;

    // ponytail: frame-counting, no ring buffer
    private int _frameCount;
    private float _elapsed;
    private float _lagTimer;
    private bool _lagging;

    void Awake()
    {
#if UNITY_ANDROID || UNITY_IOS
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 300;
#endif
        if (lagText != null) { lagText.text = ""; lagText.color = Color.white; }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // FPS: 帧计数法，按时间窗口取均值，不受 targetFrameRate 干扰
        _frameCount++;
        _elapsed += dt;

        if (_elapsed >= refreshInterval && fpsText != null)
        {
            float fps = _frameCount / _elapsed;
            _frameCount = 0;
            _elapsed = 0f;

            fpsText.text = $"FPS: {fps:F0}";
            fpsText.color = fps >= fpsGoodThreshold ? fpsGoodColor
                          : fps >= fpsWarnThreshold ? fpsWarnColor
                          : Color.red;
        }

        // 卡顿检测
        if (dt > lagThreshold)
        {
            _lagging = true;
            _lagTimer = 0f;
            if (lagText != null) lagText.text = "⚠ 网络卡顿";
        }
        else if (_lagging)
        {
            _lagTimer += dt;
            if (_lagTimer >= lagClearSec)
            {
                _lagging = false;
                if (lagText != null) lagText.text = "";
            }
        }
    }
}
