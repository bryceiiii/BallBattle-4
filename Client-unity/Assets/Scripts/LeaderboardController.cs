using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Compact competitive leaderboard with clear rank, skin, mass and HP hierarchy.
/// The panel is built at runtime so it can adapt to game state without scene edits.
/// </summary>
public class LeaderboardController : MonoBehaviour
{
    public static LeaderboardController Instance { get; private set; }

    [Header("Prefab")]
    public GameObject skinBallPrefab;

    [Header("Layout")]
    public int maxRows = 5;
    public float rowHeight = 40f;
    public float panelWidth = 306f;

    [Header("Palette")]
    public Color panelBgColor = new Color(0.035f, 0.055f, 0.095f, 0.94f);
    public Color headerColor = new Color(0.86f, 0.93f, 1f, 1f);
    public Color mutedTextColor = new Color(0.58f, 0.67f, 0.80f, 1f);
    public Color accentColor = new Color(0.20f, 0.78f, 0.96f, 1f);
    public Color localHighlightColor = new Color(0.20f, 0.62f, 1f, 1f);

    [Header("Refresh")]
    [Range(0.2f, 2f)] public float refreshInterval = 0.5f;

    private RectTransform _contentRect;
    private Text _foldLabel;
    private bool _collapsed;
    private int _sortedCount;
    private float _timer;
    private readonly List<RowCtx> _rows = new();

    private struct RowCtx
    {
        public GameObject go;
        public Image rowBg;
        public Image rankBadge;
        public Image localAccent;
        public Image skinImage;
        public Image hpFill;
        public Text rankText;
        public Text nameText;
        public Text massText;
    }

    private static Sprite _solidSprite;
    private static Sprite _panelSprite;
    private static Sprite _rowSprite;
    private static Sprite _badgeSprite;
    private static Font _font;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        BuildUI();
    }

    private void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer < refreshInterval) return;
        _timer = 0f;
        Refresh();
    }

    private void BuildUI()
    {
        rowHeight = Mathf.Max(40f, rowHeight);
        panelWidth = Mathf.Clamp(panelWidth, 286f, 306f);
        EnsureVisualAssets();

        var canvasGo = new GameObject("LeaderboardCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Shadow));
        panel.transform.SetParent(canvasGo.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = new Vector2(-22f, -22f);
        panelRect.sizeDelta = new Vector2(panelWidth, 44f);

        var panelImage = panel.GetComponent<Image>();
        panelImage.sprite = _panelSprite;
        panelImage.type = Image.Type.Sliced;
        panelImage.color = panelBgColor;

        var shadow = panel.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        shadow.effectDistance = new Vector2(0f, -5f);

        var outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.14f);
        outline.effectDistance = new Vector2(1f, 1f);

        var fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var rootLayout = panel.AddComponent<VerticalLayoutGroup>();
        rootLayout.childAlignment = TextAnchor.UpperCenter;
        rootLayout.childForceExpandWidth = true;
        rootLayout.childForceExpandHeight = false;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = true;
        rootLayout.spacing = 6f;
        rootLayout.padding = new RectOffset(10, 10, 8, 10);

        BuildHeader(panel.transform);

        var separator = new GameObject("Separator", typeof(RectTransform), typeof(Image));
        separator.transform.SetParent(panel.transform, false);
        AddLayoutElement(separator, 0f, 1f);
        separator.GetComponent<Image>().color = new Color(0.5f, 0.72f, 0.92f, 0.13f);

        var rowsGo = new GameObject("RowsContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
        rowsGo.transform.SetParent(panel.transform, false);
        _contentRect = rowsGo.GetComponent<RectTransform>();
        var rowsLayout = rowsGo.GetComponent<VerticalLayoutGroup>();
        rowsLayout.childAlignment = TextAnchor.UpperCenter;
        rowsLayout.childForceExpandWidth = true;
        rowsLayout.childForceExpandHeight = false;
        rowsLayout.childControlWidth = true;
        rowsLayout.childControlHeight = true;
        rowsLayout.spacing = 4f;
    }

    private void BuildHeader(Transform parent)
    {
        var header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(parent, false);
        AddLayoutElement(header, 0f, 34f);

        var accent = CreateImage("Accent", header.transform, _solidSprite,
            new Color(accentColor.r, accentColor.g, accentColor.b, 0.95f));
        SetRect(accent.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(4f, 20f));

        var title = NewText(header.transform, "Title", "LEADERBOARD", 14,
            FontStyle.Bold, TextAnchor.MiddleLeft, headerColor);
        SetRect(title.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(-64f, 0f));

        var count = NewText(header.transform, "Count", "TOP 5", 10,
            FontStyle.Bold, TextAnchor.MiddleRight, accentColor);
        SetRect(count.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f),
            new Vector2(1f, 0.5f), new Vector2(-34f, 0f), new Vector2(34f, 0f));

        var fold = new GameObject("FoldBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        fold.transform.SetParent(header.transform, false);
        var foldRect = fold.GetComponent<RectTransform>();
        foldRect.anchorMin = foldRect.anchorMax = new Vector2(1f, 0.5f);
        foldRect.pivot = new Vector2(1f, 0.5f);
        foldRect.sizeDelta = new Vector2(26f, 26f);
        foldRect.anchoredPosition = Vector2.zero;
        fold.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
        fold.GetComponent<Button>().onClick.AddListener(OnFoldClicked);

        _foldLabel = NewText(fold.transform, "FoldLabel", "−", 16,
            FontStyle.Bold, TextAnchor.MiddleCenter, mutedTextColor);
        SetRect(_foldLabel.rectTransform, Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    private RowCtx BuildRow()
    {
        var row = new GameObject("Row", typeof(RectTransform), typeof(Image));
        row.transform.SetParent(_contentRect, false);
        AddLayoutElement(row, 0f, rowHeight);

        var ctx = new RowCtx { go = row };
        ctx.rowBg = row.GetComponent<Image>();
        ctx.rowBg.sprite = _rowSprite;
        ctx.rowBg.type = Image.Type.Sliced;
        ctx.rowBg.color = new Color(0.10f, 0.14f, 0.22f, 0.70f);
        ctx.rowBg.raycastTarget = false;

        var accent = CreateImage("LocalAccent", row.transform, _solidSprite,
            new Color(localHighlightColor.r, localHighlightColor.g, localHighlightColor.b, 0.95f));
        ctx.localAccent = accent;
        SetRect(accent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(0f, 0.5f), new Vector2(0f, 3f), new Vector2(3f, -6f));

        var badge = CreateImage("RankBadge", row.transform, _badgeSprite,
            new Color(0.40f, 0.48f, 0.60f, 0.95f));
        ctx.rankBadge = badge;
        SetRect(badge.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(9f, 1f), new Vector2(28f, 28f));

        var rank = NewText(badge.transform, "Rank", "1", 12,
            FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        ctx.rankText = rank;
        SetRect(rank.rectTransform, Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        var skin = InstantiateSkin(row.transform);
        skin.name = "SkinBall";
        var skinRect = skin.GetComponent<RectTransform>();
        skinRect.anchorMin = skinRect.anchorMax = new Vector2(0f, 0.5f);
        skinRect.pivot = new Vector2(0f, 0.5f);
        skinRect.sizeDelta = new Vector2(24f, 24f);
        skinRect.anchoredPosition = new Vector2(43f, 2f);
        ctx.skinImage = skin.GetComponent<Image>();
        if (ctx.skinImage != null)
            ctx.skinImage.preserveAspect = true;

        var name = NewText(row.transform, "Name", "Player", 13,
            FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.94f, 0.97f, 1f, 1f));
        ctx.nameText = name;
        name.horizontalOverflow = HorizontalWrapMode.Overflow;
        name.rectTransform.anchorMin = new Vector2(0f, 0.42f);
        name.rectTransform.anchorMax = new Vector2(1f, 1f);
        name.rectTransform.offsetMin = new Vector2(72f, 0f);
        name.rectTransform.offsetMax = new Vector2(-58f, -2f);

        var mass = NewText(row.transform, "Mass", "0", 12,
            FontStyle.Bold, TextAnchor.MiddleRight, new Color(0.82f, 0.91f, 1f, 1f));
        ctx.massText = mass;
        SetRect(mass.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f), new Vector2(-10f, 2f), new Vector2(48f, 26f));

        var hpBg = CreateImage("HpBg", row.transform, _solidSprite,
            new Color(0.01f, 0.025f, 0.05f, 0.90f));
        hpBg.rectTransform.anchorMin = new Vector2(0f, 0f);
        hpBg.rectTransform.anchorMax = new Vector2(1f, 0f);
        hpBg.rectTransform.pivot = new Vector2(0f, 0f);
        hpBg.rectTransform.offsetMin = new Vector2(72f, 6f);
        hpBg.rectTransform.offsetMax = new Vector2(-10f, 10f);

        var hpFill = CreateImage("HpFill", hpBg.transform, _solidSprite,
            new Color(0.24f, 0.90f, 0.58f, 1f));
        ctx.hpFill = hpFill;
        hpFill.type = Image.Type.Filled;
        hpFill.fillMethod = Image.FillMethod.Horizontal;
        hpFill.fillOrigin = 0;
        SetRect(hpFill.rectTransform, Vector2.zero, Vector2.one,
            new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

        return ctx;
    }

    private void Refresh()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        var players = new Dictionary<int, PlayerInfo>();
        foreach (var entry in GameManager.Circles)
        {
            var go = entry.Value;
            if (go == null) continue;
            var ctrl = go.GetComponent<CircleController>();
            if (ctrl == null || ctrl.playerId == 0) continue;

            if (!players.TryGetValue(ctrl.playerId, out var info))
            {
                info.name = ctrl.nameText != null ? ctrl.nameText.text : "Player";
                info.isLocal = ctrl.isLocalPlayer;
            }
            float diameter = Mathf.Max(go.transform.localScale.x, 0.001f);
            info.totalMass += 4f * diameter * diameter;
            info.totalHp += ctrl.debugHp;
            info.totalMaxHp += ctrl.debugMaxHp;
            var renderer = go.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null)
                info.skinSprite = renderer.sprite;
            players[ctrl.playerId] = info;
        }

        if (players.Count == 0)
        {
            _sortedCount = 0;
            ApplyVisibility();
            return;
        }

        var sorted = new List<KeyValuePair<int, PlayerInfo>>(players);
        sorted.Sort((a, b) => b.Value.totalMass.CompareTo(a.Value.totalMass));
        if (sorted.Count > maxRows)
            sorted.RemoveRange(maxRows, sorted.Count - maxRows);

        while (_rows.Count < sorted.Count)
            _rows.Add(BuildRow());
        while (_rows.Count > sorted.Count)
        {
            var last = _rows[^1];
            if (last.go != null) Destroy(last.go);
            _rows.RemoveAt(_rows.Count - 1);
        }

        for (int i = 0; i < sorted.Count; i++)
            UpdateRow(_rows[i], i + 1, sorted[i].Value);

        _sortedCount = sorted.Count;
        ApplyVisibility();
    }

    private void UpdateRow(RowCtx row, int rank, PlayerInfo info)
    {
        row.rankText.text = rank.ToString();
        row.nameText.text = Truncate(info.name, 13);
        row.massText.text = info.totalMass >= 1000f
            ? $"{info.totalMass / 1000f:F1}K"
            : Mathf.RoundToInt(info.totalMass).ToString();

        Color rankColor = rank switch
        {
            1 => new Color(0.98f, 0.77f, 0.23f, 1f),
            2 => new Color(0.72f, 0.80f, 0.90f, 1f),
            3 => new Color(0.86f, 0.50f, 0.30f, 1f),
            _ => new Color(0.34f, 0.42f, 0.54f, 0.92f)
        };
        row.rankBadge.color = rankColor;
        row.rankText.color = rank <= 3 ? new Color(0.08f, 0.10f, 0.14f, 1f) : Color.white;

        row.nameText.color = info.isLocal
            ? new Color(0.48f, 0.82f, 1f, 1f)
            : new Color(0.94f, 0.97f, 1f, 1f);
        row.rowBg.color = info.isLocal
            ? new Color(0.08f, 0.20f, 0.34f, 0.88f)
            : rank == 1
                ? new Color(0.19f, 0.16f, 0.08f, 0.82f)
                : new Color(0.10f, 0.14f, 0.22f, 0.70f);
        row.localAccent.gameObject.SetActive(info.isLocal);

        float ratio = info.totalMaxHp > 0f
            ? Mathf.Clamp01(info.totalHp / info.totalMaxHp)
            : 0f;
        row.hpFill.fillAmount = ratio;
        row.hpFill.color = ratio > 0.6f
            ? new Color(0.24f, 0.90f, 0.58f, 1f)
            : ratio > 0.3f
                ? new Color(0.98f, 0.76f, 0.22f, 1f)
                : new Color(1f, 0.35f, 0.32f, 1f);

        if (row.skinImage == null) return;
        if (info.skinSprite != null)
        {
            row.skinImage.sprite = info.skinSprite;
            row.skinImage.color = Color.white;
        }
        else
        {
            row.skinImage.sprite = _badgeSprite;
            row.skinImage.color = new Color(0.62f, 0.72f, 0.86f, 1f);
        }
    }

    private void ApplyVisibility()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].go != null)
                _rows[i].go.SetActive(!_collapsed && i < _sortedCount);
        }
    }

    private void OnFoldClicked()
    {
        _collapsed = !_collapsed;
        if (_foldLabel != null)
            _foldLabel.text = _collapsed ? "+" : "−";
        ApplyVisibility();
    }

    private GameObject InstantiateSkin(Transform parent)
    {
        if (skinBallPrefab != null)
        {
            var instance = Instantiate(skinBallPrefab, parent, false);
            if (instance.GetComponent<Image>() == null)
                instance.AddComponent<Image>();
            return instance;
        }

        var go = new GameObject("SkinBall", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = _badgeSprite;
        image.preserveAspect = true;
        return go;
    }

    private struct PlayerInfo
    {
        public string name;
        public float totalMass;
        public float totalHp;
        public float totalMaxHp;
        public Sprite skinSprite;
        public bool isLocal;
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text NewText(Transform parent, string name, string value, int size,
        FontStyle style, TextAnchor alignment, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = FontAsset;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static Font FontAsset
    {
        get
        {
            if (_font == null)
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _font;
        }
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }

    private static void AddLayoutElement(GameObject go, float width, float height)
    {
        var element = go.AddComponent<LayoutElement>();
        if (width > 0f)
        {
            element.minWidth = width;
            element.preferredWidth = width;
        }
        if (height > 0f)
        {
            element.minHeight = height;
            element.preferredHeight = height;
        }
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "Player";
        return value.Length > max ? value[..max] + ".." : value;
    }

    private static void EnsureVisualAssets()
    {
        if (_solidSprite != null) return;
        _solidSprite = CreateSolidSprite();
        _panelSprite = CreateRoundedSprite(64, 18, new Vector4(18f, 18f, 18f, 18f));
        _rowSprite = CreateRoundedSprite(48, 10, new Vector4(12f, 12f, 12f, 12f));
        _badgeSprite = CreateRoundedSprite(64, 32, new Vector4(24f, 24f, 24f, 24f));
    }

    private static Sprite CreateSolidSprite()
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var pixels = new Color32[16];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32(255, 255, 255, 255);
        texture.SetPixels32(pixels);
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f),
            new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite CreateRoundedSprite(int size, int radius, Vector4 border)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float dx = Mathf.Max(Mathf.Abs(px - half) - (half - radius), 0f);
                float dy = Mathf.Max(Mathf.Abs(py - half) - (half - radius), 0f);
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                byte alpha = (byte)(Mathf.Clamp01(radius + 0.5f - distance) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
    }
}
