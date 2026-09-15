using UnityEngine;

/// <summary>
/// 子弹控制器。管理子弹外观和碰撞反馈。
/// 碰撞检测和扣血由服务端处理，本脚本只负责视觉反馈。
/// </summary>
public class BulletController : MonoBehaviour
{
    public int entityId;
    public int ownerPlayerId = -1;  // 发射者，防止自伤

    [Header("参数")]
    public float maxLifetime = 3f;       // 最大存活时间（与服务端一致）
    public float fadeDuration = 0.5f;    // 消失前淡出时间
    public float smoothTime = 0.04f;     // SmoothDamp 插值时间
    public float speedMultiplier = 1f;   // 视觉速度倍率：1=服务端速度，>1 更快，<1 更慢

    [Header("命中效果")]
    public GameObject hitEffectPrefab;    // 命中特效预制体（可拖拽）
    public float hitEffectDuration = 0.3f; // 特效存活时间
    public float hitRadius = 0.3f;         // 命中判定半径（子弹边缘+余量），越大越容易判定命中

    [Header("速度感（可选增强）")]
    public bool enableVelocityStretch = false;   // 沿飞行方向拉伸子弹
    [Range(1f, 4f)] public float stretchFactor = 2f;
    public bool enableTrail = false;             // 拖尾
    public float trailTime = 0.15f;
    public float trailWidth = 0.18f;
    public Color trailColor = new Color(1f, 0.9f, 0.5f, 0.5f);

    private Vector3 _baseScale = Vector3.one;

    private float age = 0f;
    private bool hasHit = false;

    private Vector3 _targetPos;
    private Vector2 _posVelocity;
    private bool _hasTarget = false;
    private Vector3 _lastReceivedPos;  // 上一次服务端位置，用于推算方向

    void Awake()
    {
        _baseScale = transform.localScale;
        if (enableTrail)
        {
            var tr = GetComponent<TrailRenderer>();
            if (tr == null) tr = gameObject.AddComponent<TrailRenderer>();
            tr.time = trailTime;
            tr.startWidth = trailWidth;
            tr.endWidth = 0f;
            tr.material = new Material(Shader.Find("Sprites/Default"));
            tr.startColor = trailColor;
            tr.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
            tr.minVertexDistance = 0.03f;
            tr.sortingOrder = 50;
        }
    }

    public void SetTargetPos(Vector3 serverPos)
    {
        if (!_hasTarget)
        {
            _targetPos = serverPos;
            transform.position = serverPos;
            _hasTarget = true;
        }
        else
        {
            // 根据上一个位置推算方向，乘以倍率外推 target
            Vector3 delta = serverPos - _lastReceivedPos;
            _targetPos = serverPos + delta * (speedMultiplier - 1f);
        }
        _lastReceivedPos = serverPos;
    }

    /// <summary>
    /// 初始化朝向（在子弹创建时由发射器调用）。
    /// 作用：立即把 sprite 朝向发射方向，避免出现发射瞬间朝向不对的帧。
    /// 同时给一个极小的初始速度量，确保 Update() 中用 _posVelocity 能立刻计算朝向。
    /// </summary>
    public void InitializeDirection(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        Vector2 n = dir.normalized;
        float angle = Mathf.Atan2(n.y, n.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
        // 给一个非常小的初始速度避免零向量导致瞬间不转向
        _posVelocity = n * 0.001f;
    }

    /// <summary>
    /// 手动命中检测（替代 OnTriggerEnter2D）。
    /// 用数学距离判定取代物理触发器边沿检测，多子弹聚簇/顿帧/快速移动下每发都能可靠触发。
    /// </summary>
    void DetectHit(Vector2 prevPos, Vector2 curPos)
    {
        Vector2 mid = (prevPos + curPos) * 0.5f;
        TryHitAt(curPos);
        if (hasHit) return;
        TryHitAt(mid);
        if (hasHit) return;
        TryHitAt(prevPos);
    }

    /// <summary>检查某采样点是否命中敌人球。命中则触发受击反馈并返回 true。</summary>
    bool TryHitAt(Vector2 p)
    {
        if (hasHit) return false;
        if (GameManager.Circles == null) return false;

        foreach (var kv in GameManager.Circles)
        {
            var go = kv.Value;
            if (go == null) continue;
            var targetCtrl = go.GetComponent<CircleController>();
            if (targetCtrl == null) continue;
            if (targetCtrl.playerId == ownerPlayerId) continue; // 跳过发射者自己的球

            float targetRadius = go.transform.localScale.x * 0.5f;
            float r = targetRadius + hitRadius;
            float dx = p.x - go.transform.position.x;
            float dy = p.y - go.transform.position.y;
            if (dx * dx + dy * dy <= r * r)
            {
                HandleHit(targetCtrl, p);
                return true;
            }
        }
        return false;
    }

    void HandleHit(CircleController targetCtrl, Vector3 hitPos)
    {
        hasHit = true;

        // 命中手感反馈（顿帧/爆炸/被击闪色+推动/震动/音效，纯客户端当帧生效）
        Vector2 bulletDir = _posVelocity;
        if (bulletDir.sqrMagnitude < 0.0001f)
            bulletDir = (Vector2)(_targetPos - transform.position);
        GameFeel.Instance?.OnHit(hitPos, targetCtrl, bulletDir);

        // 播放命中特效
        if (hitEffectPrefab != null)
        {
            var fx = Instantiate(hitEffectPrefab, hitPos, Quaternion.identity);
            Destroy(fx, hitEffectDuration);
        }
        else
        {
            // 兜底：子弹自身闪烁变红
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
            {
                sr.color = Color.red;
            }
            transform.localScale *= 1.5f; // 放大表示命中
        }

        // 禁用后续触发检测
        foreach (var col in GetComponentsInChildren<Collider2D>())
        {
            col.enabled = false;
        }
    }

    void Update()
    {
        age += Time.deltaTime;

        Vector2 prevPos = transform.position;

        // SmoothDamp 插值
        if (_hasTarget)
        {
            Vector2 desired = Vector2.SmoothDamp(transform.position, _targetPos,
                ref _posVelocity, smoothTime);
            transform.position = new Vector3(desired.x, desired.y, 0);
        }

        // 手动命中检测（替代物理触发器）
        if (!hasHit)
            DetectHit(prevPos, transform.position);

        // 方向与朝向：优先使用插值速度，其次使用 targetDelta（初始帧）
        Vector2 movementDir = Vector2.zero;
        if (_posVelocity.sqrMagnitude > 0.0001f)
        {
            movementDir = _posVelocity;
        }
        else if (_hasTarget)
        {
            movementDir = (Vector2)(_targetPos - transform.position);
        }

        if (movementDir.sqrMagnitude > 0.0001f)
        {
            float angle = Mathf.Atan2(movementDir.y, movementDir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        // 速度感：根据方向是否显著决定是否拉伸，否则恢复基础缩放
        if (enableVelocityStretch && movementDir.sqrMagnitude > 0.01f)
        {
            transform.localScale = new Vector3(_baseScale.x * stretchFactor, _baseScale.y, _baseScale.z);
        }
        else
        {
            transform.localScale = _baseScale;
        }

        // 淡出
        if (age >= maxLifetime - fadeDuration && !hasHit)
        {
            float alpha = Mathf.Lerp(1f, 0f, (age - (maxLifetime - fadeDuration)) / fadeDuration);
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
            {
                var c = sr.color;
                sr.color = new Color(c.r, c.g, c.b, alpha);
            }
        }

        // 超时保护
        if (age >= maxLifetime + 1f)
            Destroy(gameObject);
    }
}
