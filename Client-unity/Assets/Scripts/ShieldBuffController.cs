using UnityEngine;

/// <summary>
/// 护盾特效三状态控制：获得(Get) → 保持(Idle 循环) → 破碎(Break)。
/// Get 是状态机默认态，特效实例化时自动播放，无需手动触发。
/// 服务端 Shield 行被删除时由 GameManager 调用 Break()：
/// 先脱离球体（球可能同时死亡/缩小），播完破碎动画后自毁。
/// </summary>
public class ShieldBuffController : MonoBehaviour
{
    [Header("引用")]
    public Animator animator;

    [Header("破碎")]
    [Tooltip("略大于 ShieldBreak 动画时长(0.33s)，播完再销毁")]
    public float breakDuration = 0.45f;

    private bool _breaking;

    // 受击闪色
    private SpriteRenderer _sr;
    private Color _baseColor = Color.white;
    private float _flashTimer = -1f;

    void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        _sr = GetComponent<SpriteRenderer>();
        if (_sr == null) _sr = GetComponentInChildren<SpriteRenderer>(true);
        if (_sr != null) _baseColor = _sr.color;  // 记录预制体原始色（含透明度）
    }

    /// <summary>
    /// 护盾被子弹打中：护盾本体闪一下（只改 SpriteRenderer.color，保留原透明度）。
    /// 连续命中会重新计时，不会闪烁打架；到点自动恢复原色。
    /// </summary>
    public void FlashHit(Color color, float duration)
    {
        if (_sr == null || duration <= 0f) return;
        var c = color;
        c.a = _baseColor.a;          // 透明度跟随预制体，避免闪成全不透明
        _sr.color = c;
        _flashTimer = duration;
    }

    void Update()
    {
        if (_flashTimer < 0f) return;
        _flashTimer -= Time.deltaTime;
        if (_flashTimer > 0f) return;

        if (_sr != null) _sr.color = _baseColor;
        _flashTimer = -1f;
    }

    /// <summary>
    /// 护盾破碎：脱离父级保留世界位置，触发 Break 状态，播完自毁。
    /// 重复调用安全（连中多次删除事件只播一次）。
    /// </summary>
    public void Break()
    {
        if (_breaking) return;
        _breaking = true;

        // 恢复原色，避免带着受击闪色去播破碎动画
        if (_sr != null) _sr.color = _baseColor;
        _flashTimer = -1f;

        // 脱离球体，留在原地播破碎（球死亡/缩小不影响特效）
        transform.SetParent(null);

        if (animator != null && animator.runtimeAnimatorController != null)
            animator.SetTrigger("Break");

        Destroy(gameObject, breakDuration);
    }
}
