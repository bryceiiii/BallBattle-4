using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 可拖拽的虚拟摇杆组件。
/// 放在一个 Image（摇杆背景）+ 子 Image（摇杆手柄）的 UI 结构上。
/// 支持固定位置和跟随手指两种模式。
/// </summary>
public class VirtualJoystick : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("摇杆引用")]
    public RectTransform backgroundRect;    // 摇杆背景圆
    public RectTransform handleRect;        // 摇杆手柄（可拖拽部分）

    [Header("参数")]
    [Range(0.1f, 2f)]
    public float handleMoveRange = 1f;      // 手柄最大偏移量（相对背景半径的比例）
    [Range(0f, 0.5f)]
    public float deadZone = 0.1f;           // 死区（小于此值视为无输入）

    [Header("跟随手指模式")]
    public bool followFinger = true;         // true=手指按下时摇杆跟随到手指位置
    public float resetSpeed = 8f;           // 松手后归位速度

    /// <summary>当前输入方向（归一化，未移动时为 Vector2.zero）</summary>
    public Vector2 Direction { get; private set; }

    private Vector2 _startBgPos;
    private bool _isDragging;
    private Canvas _canvas;

    void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
        if (backgroundRect != null)
            _startBgPos = backgroundRect.anchoredPosition;

        // 若未拖拽引用，自动查找子物体
        if (backgroundRect == null)
            backgroundRect = transform as RectTransform;
        if (handleRect == null && backgroundRect != null)
            handleRect = backgroundRect.GetChild(0) as RectTransform;
    }

    void Update()
    {
        // 松手后平滑归位
        if (!_isDragging && followFinger && backgroundRect != null)
        {
            backgroundRect.anchoredPosition = Vector2.Lerp(
                backgroundRect.anchoredPosition, _startBgPos, Time.deltaTime * resetSpeed);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isDragging = true;

        if (followFinger && backgroundRect != null && _canvas != null)
        {
            // 将手指位置转换为 Canvas 局部坐标
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                backgroundRect.parent as RectTransform,
                eventData.position, _canvas.worldCamera, out Vector2 localPoint);
            backgroundRect.anchoredPosition = localPoint;
        }

        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (backgroundRect == null || handleRect == null || _canvas == null) return;

        // 手指位置（背景父节点的局部空间）
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            backgroundRect.parent as RectTransform,
            eventData.position, _canvas.worldCamera, out Vector2 localPoint);

        // 相对背景中心的方向
        Vector2 offset = localPoint - backgroundRect.anchoredPosition;

        // 限制最大偏移
        float maxRadius = backgroundRect.sizeDelta.x * 0.5f * handleMoveRange;
        if (offset.magnitude > maxRadius)
            offset = offset.normalized * maxRadius;

        handleRect.anchoredPosition = offset;

        // 死区过滤
        float normalizedMag = offset.magnitude / maxRadius;
        if (normalizedMag < deadZone)
            Direction = Vector2.zero;
        else
            Direction = offset.normalized * Mathf.InverseLerp(deadZone, 1f, normalizedMag);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isDragging = false;
        Direction = Vector2.zero;

        if (handleRect != null)
            handleRect.anchoredPosition = Vector2.zero;

        if (!followFinger && backgroundRect != null)
            backgroundRect.anchoredPosition = _startBgPos;
    }
}
