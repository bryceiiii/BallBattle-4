using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 手感优化版虚拟摇杆。
/// - 极小死区（仅过滤硬件噪声）
/// - 幂次曲线（低速精准 + 高速有力）
/// - 方向平滑插值（无突兀转向）
/// - 固定位置（肌肉记忆）默认关闭跟随手指
/// </summary>
public class VirtualJoystick : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("摇杆引用")]
    public RectTransform backgroundRect;
    public RectTransform handleRect;

    [Header("基础参数")]
    [Range(0.3f, 1.5f)]
    public float handleMoveRange = 0.9f;

    [Range(0f, 0.15f)]
    [Tooltip("死区半径比例：仅过滤硬件噪声，不要设太大")]
    public float deadZone = 0.04f;

    [Header("手感曲线")]
    [Range(1f, 3f)]
    [Tooltip("幂次：1=线性, 1.5=轻推精准/重推有力, 2=强烈加速感")]
    public float powerCurve = 1.6f;

    [Range(0.05f, 0.5f)]
    [Tooltip("方向平滑度：越小越跟手，越大越平滑")]
    public float directionSmoothTime = 0.12f;

    [Header("跟随手指")]
    [Tooltip("开启=按下时摇杆跳到手指位置 | 关闭=固定位置（推荐，肌肉记忆）")]
    public bool followFinger = false;

    [Range(2f, 20f)]
    public float resetSpeed = 10f;

    /// <summary>当前输入方向（归一化，带力度）</summary>
    public Vector2 Direction { get; private set; }

    /// <summary>原始方向（未平滑）</summary>
    public Vector2 RawDirection { get; private set; }

    /// <summary>当前力度 [0, 1]</summary>
    public float Magnitude { get; private set; }

    /// <summary>手指是否在摇杆上</summary>
    public bool IsActive => _isDragging;

    private Vector2 _startBgScreenPos;   // 初始时摇杆在屏幕上的位置
    private bool _isDragging;
    private Canvas _canvas;

    // 平滑
    private Vector2 _smoothDirection;
    private Vector2 _directionVelocity;
    private Vector2 _handleVelocity;

    void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
        if (backgroundRect == null)
            backgroundRect = transform as RectTransform;
        if (handleRect == null && backgroundRect != null)
            handleRect = backgroundRect.GetChild(0) as RectTransform;
        if (backgroundRect != null)
            _startBgScreenPos = backgroundRect.position; // 屏幕空间位置
    }

    void Update()
    {
        if (!_isDragging)
        {
            Direction = Vector2.zero;
            RawDirection = Vector2.zero;
            Magnitude = 0f;
            _smoothDirection = Vector2.zero;
            _directionVelocity = Vector2.zero;

            // 松手后背景归位（屏幕空间平滑回归）
            if (followFinger && backgroundRect != null)
            {
                backgroundRect.position = Vector2.Lerp(
                    backgroundRect.position, _startBgScreenPos, Time.deltaTime * resetSpeed);
            }
        }
        else
        {
            _smoothDirection = Vector2.SmoothDamp(
                _smoothDirection,
                RawDirection,
                ref _directionVelocity,
                directionSmoothTime,
                Mathf.Infinity,
                Time.deltaTime);
            Direction = _smoothDirection;
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isDragging = true;

        if (followFinger && backgroundRect != null)
        {
            // 屏幕空间直接设置位置（Screen Space Overlay Canvas 上 position = screen pos）
            Vector2 fingerScreen = eventData.position;
            float halfW = backgroundRect.sizeDelta.x * 0.5f;
            float halfH = backgroundRect.sizeDelta.y * 0.5f;
            fingerScreen.x = Mathf.Clamp(fingerScreen.x, halfW, Screen.width - halfW);
            fingerScreen.y = Mathf.Clamp(fingerScreen.y, halfH, Screen.height - halfH);
            backgroundRect.position = fingerScreen;
        }

        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (backgroundRect == null || handleRect == null || _canvas == null) return;

        // 将屏幕坐标转到背景 Rect 局部空间 → offset 从 (0,0) 算起
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            backgroundRect,
            eventData.position,
            _canvas.worldCamera,
            out Vector2 offset);

        // 最大半径
        float bgRadius = backgroundRect.sizeDelta.x * 0.5f;
        float maxRadius = bgRadius * handleMoveRange;

        // 钳制
        float dist = offset.magnitude;
        if (dist > maxRadius)
            offset = offset.normalized * maxRadius;

        // ---- 手感曲线 ----
        float normalizedDist = Mathf.Clamp01(dist / maxRadius);

        float magnitude;
        if (normalizedDist < deadZone)
        {
            magnitude = 0f;
            offset = Vector2.zero;
        }
        else
        {
            float t = (normalizedDist - deadZone) / (1f - deadZone);
            magnitude = Mathf.Pow(t, powerCurve);
        }

        RawDirection = offset.normalized * magnitude;
        Magnitude = magnitude;

        // 手柄视觉平滑
        Vector2 handleTarget = offset.normalized * Mathf.Min(dist, maxRadius);
        handleRect.anchoredPosition = Vector2.SmoothDamp(
            handleRect.anchoredPosition,
            handleTarget,
            ref _handleVelocity,
            0.06f,
            Mathf.Infinity,
            Time.deltaTime);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isDragging = false;

        if (handleRect != null)
            handleRect.anchoredPosition = Vector2.zero;
    }
}
