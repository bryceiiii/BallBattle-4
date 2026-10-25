using UnityEngine;

/// <summary>
/// 复活无敌期可视化（纯表现层）。
/// 每帧用 IGameSession 查该球所属玩家的 Player.InvincibleUntilMs，与 IGameSession.NowMs 比较：
/// 无敌中 → 在球体子级显示脉冲光环；到期 → 自动隐藏。不写任何权威数据，不改无敌规则。
/// 联机（SpacetimeGameSession，墙钟 ms）与单机（LocalGameSession，模拟时钟 ms）共用同一份逻辑。
/// </summary>
[DisallowMultipleComponent]
public class InvincibleVisual : MonoBehaviour
{
    [Header("光环外观（纯视觉，可在 Inspector 调）")]
    public float ringScale = 1.2f;         // 光环直径相对球体直径的倍数
    public float pulseAmplitude = 0.06f;   // 脉冲缩放幅度
    public float pulseSpeed = 4f;          // 脉冲速度
    public float rotateSpeed = 50f;        // 光环自转角速度（度/秒）
    public Color ringColor = new Color(0.45f, 0.95f, 1f, 0.9f);
    public int sortingOrder = 160;         // 盖在球体之上（球体随直径 1~60，故须高于 60）

    private CircleController _circle;
    private GameObject _fx;
    private SpriteRenderer _fxSr;
    private float _baseAlpha = 0.9f;
    private bool _visible;

    // 运行时兜底光环：整场景共用一张，避免每个球各生成一次贴图
    private static Sprite _fallbackRing;

    void Awake()
    {
        _circle = GetComponent<CircleController>();
    }

    void Update()
    {
        bool inInvincible = EvaluateInvincible();
        if (inInvincible != _visible)
        {
            _visible = inInvincible;
            SetVisible(_visible);
        }
        if (_visible) Animate();
    }

    /// <summary>
    /// 无敌判定：服务端下发的时间戳 vs 当前会话时钟。
    /// 在线模式 NowMs = UTC 墙钟毫秒（与服务端 Timestamp 同纪元），单机模式 = 模拟时钟毫秒。
    /// </summary>
    private bool EvaluateInvincible()
    {
        var session = GameSessionManager.Current;
        if (session == null || _circle == null) return false;

        int playerId = _circle.playerId;
        if (playerId == 0) return false; // GameManager 尚未赋值，下一帧再判

        var player = session.FindPlayerByPlayerId(playerId);
        if (player == null || player.InvincibleUntilMs <= 0d) return false;

        return player.InvincibleUntilMs > session.NowMs;
    }

    private void SetVisible(bool visible)
    {
        if (visible) EnsureFx();
        if (_fx != null) _fx.SetActive(visible);
    }

    private void EnsureFx()
    {
        if (_fx != null) return;

        var prefab = PrefabsManager.Instance != null ? PrefabsManager.Instance.invincibleFxPrefab : null;
        _fx = prefab != null ? Instantiate(prefab) : new GameObject("RespawnInvincibleFx(Runtime)");
        _fx.name = "RespawnInvincibleFx";
        _fx.transform.SetParent(transform, false);
        _fx.transform.localPosition = Vector3.zero;
        _fx.transform.localRotation = Quaternion.identity;

        _fxSr = _fx.GetComponentInChildren<SpriteRenderer>(true);
        if (_fxSr == null) _fxSr = _fx.AddComponent<SpriteRenderer>();
        if (_fxSr.sprite == null) _fxSr.sprite = GetFallbackRing(); // 预制体还没配美术时用兜底环

        if (_fxSr.color.a <= 0.01f) _fxSr.color = ringColor;
        _baseAlpha = _fxSr.color.a;
        _fxSr.sortingOrder = sortingOrder;

        _fx.SetActive(false);
    }

    private void Animate()
    {
        if (_fx == null) return;

        float t = Time.time;
        float s = ringScale * (1f + pulseAmplitude * Mathf.Sin(t * pulseSpeed));
        _fx.transform.localScale = new Vector3(s, s, 1f);
        _fx.transform.localRotation = Quaternion.Euler(0f, 0f, t * rotateSpeed);

        if (_fxSr != null)
        {
            var c = _fxSr.color;
            c.a = _baseAlpha * (0.75f + 0.25f * Mathf.Sin(t * pulseSpeed * 1.5f));
            _fxSr.color = c;
        }
    }

    /// <summary>生成一张柔边圆环贴图作为占位美术：外缘贴住贴图边界，宽约半径的 7%，PPU=128 → 直径 1 世界单位。</summary>
    private static Sprite GetFallbackRing()
    {
        if (_fallbackRing != null) return _fallbackRing;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        var px = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        float outer = center;
        float inner = center * 0.86f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((outer - d) / 2f) * Mathf.Clamp01((d - inner) / 2f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        tex.SetPixels32(px);
        tex.Apply();
        tex.hideFlags = HideFlags.DontSave;

        _fallbackRing = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        _fallbackRing.hideFlags = HideFlags.DontSave;
        return _fallbackRing;
    }
}
