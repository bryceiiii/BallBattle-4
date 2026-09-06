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

    void OnTriggerEnter2D(Collider2D other)
    {
        if (hasHit) return; // 只处理首次命中

        // 跳过发射者自己的球
        var targetCtrl = other.GetComponent<CircleController>();
        if (targetCtrl != null && targetCtrl.playerId == ownerPlayerId)
            return;

        // 标记命中
        hasHit = true;

        // 命中手感反馈（顿帧/爆炸/被击闪色+推动/震动/音效，纯客户端当帧生效）
        Vector2 bulletDir = _posVelocity;
        if (bulletDir.sqrMagnitude < 0.0001f)
            bulletDir = (Vector2)(_targetPos - transform.position);
        GameFeel.Instance?.OnHit(transform.position, targetCtrl, bulletDir);

        // 播放命中特效
        if (hitEffectPrefab != null)
        {
            var fx = Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
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

        // SmoothDamp 插值
        if (_hasTarget)
        {
            Vector2 desired = Vector2.SmoothDamp(transform.position, _targetPos,
                ref _posVelocity, smoothTime);
            transform.position = new Vector3(desired.x, desired.y, 0);
        }

        // 速度感：沿飞行方向拉伸子弹
        if (enableVelocityStretch && _posVelocity.sqrMagnitude > 0.01f)
        {
            float angle = Mathf.Atan2(_posVelocity.y, _posVelocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
            transform.localScale = new Vector3(_baseScale.x * stretchFactor, _baseScale.y, _baseScale.z);
        }
        else if (enableVelocityStretch)
        {
            transform.rotation = Quaternion.identity;
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
