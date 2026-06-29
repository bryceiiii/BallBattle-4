using UnityEngine;
using UnityEngine.UI;

public class PerformanceMonitor : MonoBehaviour
{
    [Header("UI 引用")]
    public Text fpsText;
    public Text lagText;

    [Header("FPS 配置")]
    [Range(0.1f, 1f)] public float refreshRate = 0.3f;
    public int smoothSamples = 15;
    public Color fpsGoodColor = Color.green;
    public float fpsGoodThreshold = 30f;
    public Color fpsWarnColor = Color.yellow;
    public float fpsWarnThreshold = 15f;

    [Header("卡顿检测")]
    public float lagThreshold = 0.1f;
    public float lagClearSec = 0.5f;

    private float[] _samples;
    private int _idx;
    private float _timer;
    private float _lagTimer;
    private bool _lagging;

    void Awake()
    {
        _samples = new float[smoothSamples];
        if (lagText != null) { lagText.text = ""; lagText.color = Color.white; }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // FPS 采样
        _samples[_idx] = 1f / dt;
        _idx = (_idx + 1) % smoothSamples;
        _timer += dt;

        if (_timer >= refreshRate && fpsText != null)
        {
            _timer = 0f;
            float sum = 0f; int n = 0;
            for (int i = 0; i < smoothSamples; i++)
                if (_samples[i] > 0f) { sum += _samples[i]; n++; }

            float fps = n > 0 ? sum / n : 0f;
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
