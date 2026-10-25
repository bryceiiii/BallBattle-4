using UnityEngine;

/// <summary>
/// 激光贯穿光柱（纯表现层，世界空间独立物体，不挂在球体/子弹节点下）。
/// 三层结构：外辉光(65) → 内核(68) → 起终点光斑(70)。
/// 球体 sortingOrder 为 1~60（随直径变化），玩家名字 Canvas=200、HP 条 Canvas=201，
/// 所以 65/68/70 恰好压在球体之上、名字与 HP 条之下。
/// 0.15s 内 alpha 线性淡出后自销毁。
/// </summary>
public class LaserBeamFX : MonoBehaviour
{
    public const float Lifetime = 0.15f;

    public const int SortingGlow = 65;
    public const int SortingCore = 68;
    public const int SortingSpark = 70;

    [Header("外观（纯视觉）")]
    public float glowWidth = 0.6f;        // 外辉光宽度
    public float coreWidth = 0.12f;       // 内核宽度
    public float sparkDiameter = 1.2f;    // 起终点光斑直径
    public Color glowColor = new Color(0.55f, 0.62f, 1f, 0.35f); // 紫蓝、半透明
    public Color coreColor = Color.white;                        // 纯白内核

    private SpriteRenderer[] _layers;
    private float[] _baseAlphas;
    private float _elapsed;

    // 1x1 白色 Sprite，PPU=1 → 一个 sprite 正好 1 世界单位，靠 localScale 拉伸
    private static Sprite _white;

    public static LaserBeamFX Spawn(Vector3 start, Vector3 end)
    {
        Vector3 delta = end - start;
        float length = delta.magnitude;
        if (length < 0.01f) return null;

        var go = new GameObject("LaserBeamFX");
        var fx = go.AddComponent<LaserBeamFX>();
        fx.Build(start, end, delta / length, length);
        return fx;
    }

    private void Build(Vector3 start, Vector3 end, Vector3 dir, float length)
    {
        // 朝向用 Atan2（Z 轴朝向 2D 射线方向）
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector3 mid = (start + end) * 0.5f;
        Quaternion rot = Quaternion.Euler(0f, 0f, angle);

        var pm = PrefabsManager.Instance;
        var bodyPrefab = pm != null ? pm.laserBeamPrefab : null;

        var renderers = new System.Collections.Generic.List<SpriteRenderer>();

        if (bodyPrefab != null)
        {
            // 有美术资源：实例化后按长度沿 +X 拉伸（rot 已把 +X 轴对齐射线方向，
            // 预制体需按 1 世界单位长度、水平（+X）设计）
            var body = Instantiate(bodyPrefab, mid, rot, transform);
            var s = body.transform.localScale;
            body.transform.localScale = new Vector3(s.x * length, s.y, s.z);
            renderers.AddRange(body.GetComponentsInChildren<SpriteRenderer>(true));
        }
        else
        {
            // 兜底：代码生成三层纯色 Sprite
            renderers.Add(CreateBar("Glow", mid, rot, glowWidth, length, glowColor, SortingGlow));
            renderers.Add(CreateBar("Core", mid, rot, coreWidth, length, coreColor, SortingCore));
            renderers.Add(CreateSpot("SpotStart", start, sparkDiameter, glowColor, SortingSpark));
            renderers.Add(CreateSpot("SpotEnd", end, sparkDiameter, coreColor, SortingSpark));
        }

        _layers = renderers.ToArray();
        _baseAlphas = new float[_layers.Length];
        for (int i = 0; i < _layers.Length; i++)
            _baseAlphas[i] = _layers[i] != null ? _layers[i].color.a : 0f;
    }

    private SpriteRenderer CreateBar(string name, Vector3 mid, Quaternion rot, float width, float length, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = transform.InverseTransformPoint(mid);
        go.transform.localRotation = Quaternion.Inverse(transform.rotation) * rot;
        // 1x1 白 sprite（PPU=1）：rot 已把 +X 轴对齐射线方向，
        // 所以 X 轴=光束长度、Y 轴=光束宽度（写反会导致光柱垂直于射击方向）
        go.transform.localScale = new Vector3(length, width, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = WhiteSprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    private SpriteRenderer CreateSpot(string name, Vector3 pos, float diameter, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = transform.InverseTransformPoint(pos);
        go.transform.localScale = new Vector3(diameter, diameter, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = WhiteSprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    private static Sprite WhiteSprite
    {
        get
        {
            if (_white != null) return _white;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.DontSave;

            _white = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            _white.hideFlags = HideFlags.DontSave;
            return _white;
        }
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        float k = 1f - Mathf.Clamp01(_elapsed / Lifetime);

        if (_layers != null)
        {
            for (int i = 0; i < _layers.Length; i++)
            {
                var sr = _layers[i];
                if (sr == null) continue;
                var c = sr.color;
                c.a = _baseAlphas[i] * k;
                sr.color = c;
            }
        }

        if (_elapsed >= Lifetime) Destroy(gameObject);
    }
}
