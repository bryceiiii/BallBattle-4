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
    [Header("枪口闪光")]
    public float muzzleFlashDuration = 0.08f;
    public float muzzleFlashSize = 0.6f;    // 占球直径比例
    public Color muzzleFlashColor = new Color(1f, 0.85f, 0.4f, 1f);

    // ===== 命中爆炸 =====
    [Header("命中爆炸")]
    public int hitBurstCount = 6;
    public float hitBurstSize = 0.35f;
    public float hitBurstLife = 0.25f;
    public Color hitBurstColor = new Color(1f, 0.55f, 0.15f, 1f);
    public float hitFlashDuration = 0.15f;
    public Color hitFlashColor = new Color(1f, 0.35f, 0.3f, 1f);

    // ===== 伤害飘字 =====
    [Header("伤害飘字")]
    public Color damageTextColor = Color.white;
    public Color blockedTextColor = new Color(0.6f, 0.8f, 1f, 1f);

    // ===== 音效（留空则用程序生成的占位音） =====
    [Header("音效（留空则用程序生成的占位音）")]
    public AudioClip fireClip;
    public AudioClip hitClip;
    public AudioClip emptyClip;
    public AudioClip splitClip;
    [Range(0f, 1f)] public float sfxVolume = 0.8f;
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
    private readonly List<AudioSource> _audioPool = new();

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

        // 爆炸粒子
        SpawnHitBurst(hitPos);

        // 被击球：闪色 + 反方向推动（纯视觉）
        if (target != null)
        {
            target.FlashColor(hitFlashColor, hitFlashDuration);
            if (enableRecoil)
            {
                float radius = target.transform.localScale.x * 0.5f;
                target.ApplyRecoil(bulletDir, radius * hitKnockbackDistance);
            }
        }

        // 屏幕震动（比开火更强）
        if (enableScreenShake)
            CameraContoller.Instance?.Shake(hitShakeIntensity, hitShakeDuration);

        // 音效
        PlaySfx(hitClip, _hitGen, 1f);
    }

    /// <summary>分裂反馈（音效）。</summary>
    public void OnSplit()
    {
        PlaySfx(splitClip, _splitGen, 1f);
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
        var ball = GameManager.GetLocalMainBallController();

        // 枪口闪光
        if (enableMuzzleFlash)
            SpawnFlash(ballPos, dir, ball != null ? ball.transform.localScale.x : 1f);

        // 后座（沿射击反方向）
        if (enableRecoil && ball != null)
        {
            float radius = ball.transform.localScale.x * 0.5f;
            ball.ApplyRecoil(-dir, radius * recoilDistance);
        }

        // 屏幕震动（轻微）
        if (enableScreenShake)
            CameraContoller.Instance?.Shake(fireShakeIntensity, fireShakeDuration);

        // 音效
        PlaySfx(fireClip, _fireGen, 1f);
    }

    void PlayEmptyShot(Vector3 ballPos, Vector2 dir)
    {
        // 冷却被吞掉的点击：很轻的空枪反馈，别让玩家觉得哑火
        if (enableMuzzleFlash)
            SpawnFlash(ballPos, dir, 0.5f, 0.3f); // 更小更暗的闪光

        PlaySfx(emptyClip, _emptyGen, 0.6f);
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
        float size = diameter * muzzleFlashSize * sizeScale;
        Vector3 pos = ballPos + (Vector3)(dir * radius);

        var p = GetParticle();
        p.Init(_circleSprite, pos,
            new Vector2(size, size), new Vector2(size * 1.5f, size * 1.5f),
            muzzleFlashColor, dir * 2f, muzzleFlashDuration, 0f, ReturnParticle);
    }

    void SpawnHitBurst(Vector3 pos)
    {
        for (int i = 0; i < hitBurstCount; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 v = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(2f, 6f);
            float s = hitBurstSize * Random.Range(0.7f, 1.3f);
            var p = GetParticle();
            p.Init(_circleSprite, pos,
                new Vector2(s, s), new Vector2(0.04f, 0.04f),
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

    // ============================================================
    //  音效
    // ============================================================
    void PlaySfx(AudioClip clip, AudioClip fallback, float volumeScale)
    {
        if (!enableSfx) return;
        var c = clip != null ? clip : fallback;
        if (c == null) return;

        var src = GetAudioSource();
        // 注意：PlayOneShot 不响应 pitch，故用 clip+Play() 实现随机音高
        src.clip = c;
        src.volume = sfxVolume * volumeScale;
        src.pitch = Random.Range(sfxPitchMin, sfxPitchMax);
        src.Play();
    }

    AudioSource GetAudioSource()
    {
        foreach (var a in _audioPool)
            if (a != null && !a.isPlaying) return a;
        var go = new GameObject("SfxSource");
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f; // 2D 音效
        _audioPool.Add(src);
        return src;
    }

    // ============================================================
    //  程序生成占位音（有素材后替换 public 的 fireClip/hitClip/... 即可）
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
