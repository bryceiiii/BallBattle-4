using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 游戏手感（Game Feel）单例 —— 纯客户端本地表现，绝不写服务端、绝不等服务器回包。
/// 集中管理：命中顿帧(HitStop)、屏幕震动、枪口闪光、后座、命中爆炸、被击反馈、飘字、音效。
/// 所有强度/时长均为 public 字段，Inspector 可调，方便 A/B 手感调参。
/// 挂载即用：未手动挂载时首次访问会自动创建。
/// </summary>
public class GameFeel : MonoBehaviour
{
    private static GameFeel _instance;
    public static GameFeel Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<GameFeel>();
                if (_instance == null)
                {
                    var go = new GameObject("GameFeel");
                    _instance = go.AddComponent<GameFeel>();
                }
            }
            return _instance;
        }
    }

    // ===== 总开关 =====
    [Header("总开关")]
    public bool enableHitStop = true;
    public bool enableScreenShake = true;
    public bool enableRecoil = true;
    public bool enableMuzzleFlash = true;
    public bool enableFloatingDamage = true;
    public bool enableSfx = true;

    // ===== 命中顿帧 =====
    [Header("命中顿帧 HitStop")]
    [Range(0f, 0.5f)] public float hitStopTimeScale = 0.05f;
    [Range(0f, 0.3f)] public float hitStopDuration = 0.06f;

    // ===== 屏幕震动 =====
    [Header("屏幕震动")]
    public float fireShakeIntensity = 0.08f;
    public float fireShakeDuration = 0.08f;
    public float hitShakeIntensity = 0.25f;
    public float hitShakeDuration = 0.15f;

    // ===== 后座（视觉） =====
    [Header("后座（视觉，仅动表现不动服务端位置）")]
    [Range(0f, 1f)] public float recoilDistance = 0.18f;   // 占球半径比例
    [Range(0f, 1f)] public float hitKnockbackDistance = 0.22f; // 命中时被击球推动距离（占半径比例）

    // ===== 枪口闪光 =====
    [Header("枪口闪光（无 Prefab 时的程序生成纯色光斑）")]
    public float muzzleFlashDuration = 0.08f;
    public float muzzleFlashSize = 0.6f;    // 占球直径比例
    public Color muzzleFlashColor = new Color(1f, 0.85f, 0.4f, 1f);

    // ===== 枪口火花特效 Prefab =====
    [Header("枪口火花特效 Prefab（可选：拖入自带动画的预制体）")]
    [Tooltip("拖入带 Animator 的枪口火花预制体。留空 = 回退到上面的程序生成纯色光斑")]
    public GameObject muzzleFlashPrefab;
    [Tooltip("整体尺寸 = 球直径 × 此倍率（美术资源原生像素大时调小，例如 0.3）")]
    public float muzzleFlashPrefabScale = 1f;
    [Tooltip("是否按射击方向旋转特效（美术资源朝右时保持开启）")]
    public bool muzzleFlashRotateToDir = true;
    [Tooltip("旋转补偿角度：美术资源朝向不是右时用，例如朝上用 90")]
    public float muzzleFlashAngleOffset = 0f;
    [Tooltip("是否用 muzzleFlashColor 给美术资源重新着色（默认不染，保留原画颜色）")]
    public bool muzzleFlashTint = false;
    [Tooltip("特效存活时间；填 0 = 自动读取 Animator 动画长度（读不到则用 muzzleFlashDuration）")]
    public float muzzleFlashLife = 0f;

    // ===== 命中爆炸 =====
    [Header("命中爆炸")]
    [Tooltip("爆炸碎片数量")]
    public int hitBurstCount = 12;
    [Tooltip("碎片初始尺寸 = 被击球直径 × 此比例（随球缩放，大球碎片更大）")]
    public float hitBurstSize = 0.22f;
    [Tooltip("碎片存活时长（秒）")]
    public float hitBurstLife = 0.3f;
    [Tooltip("碎片扩散速度范围（世界单位/秒）")]
    public Vector2 hitBurstSpeed = new Vector2(3f, 9f);
    public Color hitBurstColor = new Color(1f, 0.55f, 0.15f, 1f);
    public float hitFlashDuration = 0.15f;
    public Color hitFlashColor = new Color(1f, 0.35f, 0.3f, 1f);

    // ===== 护盾受击 =====
    [Header("护盾受击（命中带盾的球时护盾本体闪色）")]
    public bool enableShieldHitFlash = true;
    public float shieldHitFlashDuration = 0.15f;
    public Color shieldHitFlashColor = new Color(1f, 0.3f, 0.25f, 1f);

    // ===== 伤害飘字 =====
    [Header("伤害飘字")]
    public Color damageTextColor = Color.white;
    public Color blockedTextColor = new Color(0.6f, 0.8f, 1f, 1f);

    // ===== 音效表现（音频资源统一在 GameAudioManager 中配置） =====
    [Header("音效表现")]
    [Tooltip("音效随机音高范围。音频资源请在 GameAudioManager 的 Inspector 中修改。")]
    [Range(0.9f, 1.1f)] public float sfxPitchMin = 0.9f;
    [Range(0.9f, 1.1f)] public float sfxPitchMax = 1.1f;

    // ===== 内部 =====
    private float _serverCooldownSec = 0.25f; // 与服务端 Lib.cs BULLET_COOLDOWN_SEC 一致
    private float _lastFireTime = -999f;
    private float _originalTimeScale = 1f;
    private Coroutine _hitStopCoroutine;

    private Sprite _circleSprite;
    private readonly List<GameFeelParticle> _particlePool = new();
    private readonly List<FloatingText> _textPool = new();
    private readonly List<MuzzleFlashFx> _flashPool = new();

    // 程序生成的占位音
    private AudioClip _fireGen, _hitGen, _emptyGen, _splitGen;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        Init();
    }

    void Init()
    {
        _originalTimeScale = Time.timeScale;
        _circleSprite = MobileCanvasSetup.CreateCircleSprite(64, Color.white);
        _fireGen = GenerateFire();
        _hitGen = GenerateHit();
        _emptyGen = GenerateEmpty();
        _splitGen = GenerateSplit();
    }

    void OnDestroy()
    {
        // 兜底：确保顿帧状态不会把 timeScale 卡住
        if (Time.timeScale < _originalTimeScale)
            Time.timeScale = _originalTimeScale;
    }

    // ============================================================
    //  对外接口
    // ============================================================

    /// <summary>开火请求（两条开火路径统一入口）。内部按服务端冷却判断：冷却中→空枪，否则→完整开火反馈。</summary>
    public void OnFire(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        Vector3 ballPos = GameManager.GetLocalMainBallPosition();
        if (ballPos == Vector3.zero) return;

        bool cooled = Time.time - _lastFireTime >= _serverCooldownSec;
        if (!cooled)
        {
            PlayEmptyShot(ballPos, dir);
            return;
        }
        _lastFireTime = Time.time;
        PlayFire(ballPos, dir);
    }

    /// <summary>命中反馈（由 BulletController.OnTriggerEnter2D 触发）。</summary>
    public void OnHit(Vector3 hitPos, CircleController target, Vector2 bulletDir)
    {
        if (bulletDir.sqrMagnitude < 0.0001f) bulletDir = Vector2.right;
        bulletDir.Normalize();

        // 命中顿帧
        TriggerHitStop();

        // 爆炸粒子（尺寸随被击球直径缩放）
        float hitDiameter = target != null ? target.transform.localScale.x : 1f;
        SpawnHitBurst(hitPos, hitDiameter);

        // 被击球：闪色 + 反方向推动（纯视觉）
        if (target != null)
        {
            // 有护盾 = 伤害被盾吸收：球本体不闪色、不被击退，改由护盾闪红表现
            bool shielded = GameManager.HasShield(target.entityId);

            if (!shielded)
            {
                target.FlashColor(hitFlashColor, hitFlashDuration);
                if (enableRecoil)
                {
                    float radius = target.transform.localScale.x * 0.5f;
                    target.ApplyRecoil(bulletDir, radius * hitKnockbackDistance);
                }
            }

            // 护盾受损：护盾本体闪红（命中当帧生效，不等服务端 ShieldHp 回包）
            if (enableShieldHitFlash)
                GameManager.FlashShieldHit(target.entityId, shieldHitFlashColor, shieldHitFlashDuration);
        }

        // 屏幕震动（比开火更强）
        if (enableScreenShake)
            CameraContoller.Instance?.Shake(hitShakeIntensity, hitShakeDuration);

        // 音效
        PlaySfx(GameAudioManager.Instance.hitClip, _hitGen, 1f);
    }

    /// <summary>分裂反馈（音效）。</summary>
    public void OnSplit()
    {
        PlaySfx(GameAudioManager.Instance.splitClip, _splitGen, 1f);
    }

    /// <summary>Local power-up pickup feedback. Type: 0 shield, 1 speed, 2 ammo.</summary>
    public void OnPickup(int pickupType)
    {
        float pitch = pickupType == 1 ? 1.10f : pickupType == 2 ? 0.94f : 1f;
        PlaySfx(GameAudioManager.Instance.pickupClip, _splitGen, 0.82f, pitch);
    }

    /// <summary>伤害飘字（可选）。damage>0 白字扣血；blocked=true 出"格挡"。</summary>
    public void SpawnDamageNumber(Vector3 worldPos, int damage, bool blocked)
    {
        if (!enableFloatingDamage) return;
        string text = blocked ? "格挡" : "-" + damage;
        Color c = blocked ? blockedTextColor : damageTextColor;
        SpawnText(worldPos, text, c);
    }

    // ============================================================
    //  开火反馈
    // ============================================================
    void PlayFire(Vector3 ballPos, Vector2 dir)
    {
        var balls = GameManager.GetLocalPlayerBalls();

        // 兜底：极端情况拿不到本地球时，至少在主球位置做一次反馈
        if (balls.Count == 0)
        {
            if (enableMuzzleFlash) SpawnFlash(ballPos, dir, 1f);
            if (enableScreenShake) CameraContoller.Instance?.Shake(fireShakeIntensity, fireShakeDuration);
            PlaySfx(GameAudioManager.Instance.shootClip, _fireGen, 1f);
            return;
        }

        // 枪口闪光 + 后座：对每个本地球都做（分裂后所有球一起开火、一起后退）
        for (int i = 0; i < balls.Count; i++)
        {
            var ball = balls[i];
            if (ball == null) continue;
            Vector3 p = ball.transform.position;

            // 枪口闪光（在每个球的边缘）
            if (enableMuzzleFlash)
                SpawnFlash(p, dir, ball.transform.localScale.x);

            // 后座（沿射击反方向）
            if (enableRecoil)
            {
                float radius = ball.transform.localScale.x * 0.5f;
                ball.ApplyRecoil(-dir, radius * recoilDistance);
            }
        }

        // 屏幕震动（轻微，只触发一次）
        if (enableScreenShake)
            CameraContoller.Instance?.Shake(fireShakeIntensity, fireShakeDuration);

        // 音效（只触发一次）
        PlaySfx(GameAudioManager.Instance.shootClip, _fireGen, 1f);
    }

    void PlayEmptyShot(Vector3 ballPos, Vector2 dir)
    {
        // 冷却被吞掉的点击：很轻的空枪反馈，别让玩家觉得哑火
        if (enableMuzzleFlash)
            SpawnFlash(ballPos, dir, 0.5f, 0.3f); // 更小更暗的闪光

        PlaySfx(GameAudioManager.Instance.emptyClip, _emptyGen, 0.6f);
    }

    // ============================================================
    //  顿帧
    // ============================================================
    void TriggerHitStop()
    {
        if (!enableHitStop) return;
        if (_hitStopCoroutine != null) StopCoroutine(_hitStopCoroutine);
        _hitStopCoroutine = StartCoroutine(HitStopRoutine());
    }

    IEnumerator HitStopRoutine()
    {
        Time.timeScale = hitStopTimeScale;
        yield return new WaitForSecondsRealtime(hitStopDuration);
        Time.timeScale = _originalTimeScale;
        _hitStopCoroutine = null;
    }

    // ============================================================
    //  粒子 / 闪光 / 爆炸 / 飘字
    // ============================================================
    void SpawnFlash(Vector3 ballPos, Vector2 dir, float diameter, float sizeScale = 1f)
    {
        float radius = diameter * 0.5f;
        Vector3 pos = ballPos + (Vector3)(dir * radius);

        // 优先使用美术序列帧预制体（自带动画，播完自动回收）
        if (muzzleFlashPrefab != null)
        {
            float angle = (muzzleFlashRotateToDir ? Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg : 0f)
                          + muzzleFlashAngleOffset;
            float scale = Mathf.Max(0.001f, diameter * muzzleFlashPrefabScale * sizeScale);
            Color tint = muzzleFlashTint ? muzzleFlashColor : Color.white;

            var fx = GetMuzzleFlash();
            float life = muzzleFlashLife > 0f ? muzzleFlashLife : fx.AutoLife(muzzleFlashDuration);
            fx.Play(pos, angle, new Vector3(scale, scale, 1f), tint, life, ReturnMuzzleFlash);
            return;
        }

        // 兜底：程序生成的纯色光斑
        float size = diameter * muzzleFlashSize * sizeScale;
        var p = GetParticle();
        p.Init(_circleSprite, pos,
            new Vector2(size, size), new Vector2(size * 1.5f, size * 1.5f),
            muzzleFlashColor, dir * 2f, muzzleFlashDuration, 0f, ReturnParticle);
    }

    void SpawnHitBurst(Vector3 pos, float diameter)
    {
        for (int i = 0; i < hitBurstCount; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float speed = Random.Range(hitBurstSpeed.x, hitBurstSpeed.y);
            Vector2 v = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;

            // 碎片尺寸随球直径缩放（大球大碎片），再叠加随机浮动
            float s = Mathf.Max(0.04f, hitBurstSize * diameter) * Random.Range(0.8f, 1.4f);
            var p = GetParticle();
            // startScale=s → endScale=s*1.6：向外膨胀扩散，更像爆炸
            p.Init(_circleSprite, pos,
                new Vector2(s, s), new Vector2(s * 1.6f, s * 1.6f),
                hitBurstColor, v, hitBurstLife * Random.Range(0.7f, 1.2f), 6f, ReturnParticle);
        }
    }

    void SpawnText(Vector3 pos, string text, Color color)
    {
        var t = GetText();
        t.Init(text, pos, color, new Vector3(0f, 2.2f, 0f), 0.7f, ReturnText);
    }

    GameFeelParticle GetParticle()
    {
        foreach (var p in _particlePool)
            if (p != null && !p.gameObject.activeSelf) return p;
        var go = new GameObject("GameFeelParticle");
        go.transform.SetParent(transform, false);
        var np = go.AddComponent<GameFeelParticle>();
        _particlePool.Add(np);
        return np;
    }

    void ReturnParticle(GameFeelParticle p) { p.gameObject.SetActive(false); }

    FloatingText GetText()
    {
        foreach (var t in _textPool)
            if (t != null && !t.gameObject.activeSelf) return t;
        var go = new GameObject("FloatingText");
        go.transform.SetParent(transform, false);
        var nt = go.AddComponent<FloatingText>();
        _textPool.Add(nt);
        return nt;
    }

    void ReturnText(FloatingText t) { t.gameObject.SetActive(false); }

    MuzzleFlashFx GetMuzzleFlash()
    {
        foreach (var f in _flashPool)
            if (f != null && !f.gameObject.activeSelf) return f;

        var go = Instantiate(muzzleFlashPrefab, transform, true);
        go.name = "MuzzleFlashFx";
        var fx = go.GetComponent<MuzzleFlashFx>();
        if (fx == null) fx = go.AddComponent<MuzzleFlashFx>();
        _flashPool.Add(fx);
        go.SetActive(false);
        return fx;
    }

    void ReturnMuzzleFlash(MuzzleFlashFx fx) { if (fx != null) fx.gameObject.SetActive(false); }

    // ============================================================
    //  音效
    // ============================================================
    void PlaySfx(AudioClip clip, AudioClip fallback, float volumeScale, float pitch = -1f)
    {
        if (!enableSfx) return;
        var c = clip != null ? clip : fallback;
        if (c == null) return;

        GameAudioManager.Instance.PlaySfx(
            c,
            volumeScale,
            pitch > 0f ? pitch : Random.Range(sfxPitchMin, sfxPitchMax));
    }

    // ============================================================
    //  程序生成后备音（Inspector 音频槽为空时使用）
    // ============================================================
    const int SR = 44100;

    static AudioClip GenerateFire()
    {
        float dur = 0.09f; int n = (int)(SR * dur);
        var d = new float[n]; var rng = new System.Random(1234);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Exp(-t * 45f);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float sweep = Mathf.Sin(2f * Mathf.PI * (1100f - 700f * (t / dur)) * t);
            d[i] = (noise * 0.45f + sweep * 0.55f) * env;
        }
        return MakeClip("sfx_fire", d);
    }

    static AudioClip GenerateHit()
    {
        float dur = 0.12f; int n = (int)(SR * dur);
        var d = new float[n]; var rng = new System.Random(5678);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Exp(-t * 30f);
            float low = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(160f, 50f, t / dur) * t);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.3f;
            d[i] = (low * 0.8f + noise) * env;
        }
        return MakeClip("sfx_hit", d);
    }

    static AudioClip GenerateEmpty()
    {
        float dur = 0.04f; int n = (int)(SR * dur);
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Exp(-t * 120f);
            d[i] = Mathf.Sin(2f * Mathf.PI * 1800f * t) * env * 0.5f;
        }
        return MakeClip("sfx_empty", d);
    }

    static AudioClip GenerateSplit()
    {
        float dur = 0.1f; int n = (int)(SR * dur);
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Exp(-t * 20f);
            float sweep = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(300f, 900f, t / dur) * t);
            d[i] = sweep * env * 0.7f;
        }
        return MakeClip("sfx_split", d);
    }

    static AudioClip MakeClip(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, SR, false);
        clip.SetData(data, 0);
        return clip;
    }
}
