using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 右上角排行榜，代码构建 Canvas。
/// 拖拽 skinBallPrefab 自定义皮肤球样式。
/// 每行：排名 + 皮肤球 + 竖血条（Filled bottom-up） + 名字 + 质量。
/// </summary>
public class LeaderboardController : MonoBehaviour
{
    public static LeaderboardController Instance { get; private set; }

    [Header("预制体（拖拽）")]
    public GameObject skinBallPrefab;          // 皮肤球小预览

    [Header("外观")]
    public int maxRows = 5;
    public float rowHeight = 22f;
    public float panelWidth = 200f;
    public Color panelBgColor = new Color(0f, 0f, 0f, 0.55f);
    public Color localHighlightColor = new Color(0.22f, 0.54f, 0.87f, 1f);

    [Header("刷新")]
    [Range(0.2f, 2f)] public float refreshInterval = 0.5f;

    private Canvas _canvas;
    private RectTransform _panelRect;
    private RectTransform _contentRect;
    private GameObject _rowsContainer;
    private bool _collapsed;
    private int _sortedCount;
    private float _timer;

    struct RowCtx
    {
        public GameObject go;
        public Image skinImage;
        public Image hpFill;
        public Text rankText;
        public Text nameText;
        public Text massText;
        public Image rowBg;
    }
    private List<RowCtx> _rows = new();

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    void Start() => BuildUI();

    void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer >= refreshInterval) { _timer = 0f; Refresh(); }
    }

    // ============================================================
    //  UI
    // ============================================================
    void BuildUI()
    {
        var canvasGo = new GameObject("LeaderboardCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 80;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        // Panel (top-right, fixed width, auto height by ContentSizeFitter)
        var panel = new GameObject("Panel");
        panel.transform.SetParent(canvasGo.transform, false);
        _panelRect = panel.AddComponent<RectTransform>();
        _panelRect.anchorMin = new Vector2(1f, 1f);
        _panelRect.anchorMax = new Vector2(1f, 1f);
        _panelRect.pivot = new Vector2(1f, 1f);
        _panelRect.sizeDelta = new Vector2(panelWidth, 24f);
        _panelRect.anchoredPosition = new Vector2(-20, -20);
        panel.AddComponent<Image>().color = panelBgColor;
        var fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var rootVlg = panel.AddComponent<VerticalLayoutGroup>();
        rootVlg.childAlignment = TextAnchor.UpperCenter;
        rootVlg.childForceExpandWidth = true;
        rootVlg.childForceExpandHeight = false;
        rootVlg.childControlHeight = true;
        rootVlg.spacing = 1;
        rootVlg.padding = new RectOffset(4, 4, 2, 2);

        // ===== TitleRow (24px) =====
        var titleRow = new GameObject("TitleRow");
        titleRow.transform.SetParent(panel.transform, false);
        AddLayoutElement(titleRow, 24, 24);

        var titleTxt = new GameObject("TitleText");
        titleTxt.transform.SetParent(titleRow.transform, false);
        var ttRt = titleTxt.AddComponent<RectTransform>();
        ttRt.anchorMin = new Vector2(0, 0); ttRt.anchorMax = new Vector2(1, 1);
        ttRt.offsetMin = new Vector2(8, 0); ttRt.offsetMax = new Vector2(-32, 0);
        var tt = titleTxt.AddComponent<Text>();
        tt.text = "LEADERBOARD"; tt.font = DefFont(); tt.fontSize = 12;
        tt.alignment = TextAnchor.MiddleLeft;
        tt.color = new Color(1f, 1f, 1f, 0.6f);
        tt.raycastTarget = false;

        // Fold button (overlay on title)
        var fold = new GameObject("FoldBtn");
        fold.transform.SetParent(titleRow.transform, false);
        var fRt = fold.AddComponent<RectTransform>();
        fRt.anchorMin = fRt.anchorMax = new Vector2(1f, 0.5f);
        fRt.pivot = new Vector2(1f, 0.5f);
        fRt.sizeDelta = new Vector2(24, 24);
        fRt.anchoredPosition = Vector2.zero;
        fold.AddComponent<Image>().color = new Color(0, 0, 0, 0);
        var btn = fold.AddComponent<Button>();
        btn.onClick.AddListener(OnFoldClicked);
        var fl = new GameObject("FoldLabel");
        fl.transform.SetParent(fold.transform, false);
        var flRt = fl.AddComponent<RectTransform>();
        flRt.anchorMin = Vector2.zero; flRt.anchorMax = Vector2.one;
        flRt.sizeDelta = Vector2.zero;
        var flTxt = fl.AddComponent<Text>();
        flTxt.text = "V"; flTxt.font = DefFont(); flTxt.fontSize = 10;
        flTxt.alignment = TextAnchor.MiddleCenter;
        flTxt.color = new Color(1f, 1f, 1f, 0.4f);
        flTxt.raycastTarget = false;

        // ===== Separator =====
        var sep = new GameObject("Separator");
        sep.transform.SetParent(panel.transform, false);
        AddLayoutElement(sep, 1, 1);
        sep.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);

        // ===== RowsContainer =====
        _rowsContainer = new GameObject("RowsContainer");
        _rowsContainer.transform.SetParent(panel.transform, false);
        _contentRect = _rowsContainer.AddComponent<RectTransform>();
        var cVlg = _rowsContainer.AddComponent<VerticalLayoutGroup>();
        cVlg.childAlignment = TextAnchor.UpperCenter;
        cVlg.childForceExpandWidth = true;
        cVlg.childForceExpandHeight = false;
        cVlg.childControlHeight = true;
        cVlg.spacing = 1;
    }

    // ============================================================
    //  行构建
    // ============================================================
    RowCtx BuildRow()
    {
        var go = new GameObject("Row");
        go.transform.SetParent(_contentRect, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.spacing = 4;
        hlg.padding = new RectOffset(4, 4, 2, 2);
        AddLayoutElement(go, 0, rowHeight);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0f); bg.raycastTarget = false;
        var ctx = new RowCtx { go = go, rowBg = bg };

        // Rank (24px)
        var rGo = NewText(go.transform, "Rank", 10, TextAnchor.MiddleCenter, Color.white);
        AddLayoutElement(rGo, 24, 0);
        ctx.rankText = rGo.GetComponent<Text>();

        // Skin ball (16x16)
        var sGo = InstantiateSkin(go.transform);
        sGo.name = "SkinBall";
        AddLayoutElement(sGo, 16, 16);
        ctx.skinImage = sGo.GetComponent<Image>();

        // HP bg (3x14)
        var hpBg = new GameObject("HpBg");
        hpBg.transform.SetParent(go.transform, false);
        AddLayoutElement(hpBg, 3, 14);
        var hpBgImg = hpBg.AddComponent<Image>();
        hpBgImg.color = new Color(1f, 1f, 1f, 0.12f); hpBgImg.raycastTarget = false;

        // HP fill (bottom-up filled)
        var hpFill = new GameObject("HpFill");
        hpFill.transform.SetParent(hpBg.transform, false);
        var hpFillRt = hpFill.AddComponent<RectTransform>();
        hpFillRt.anchorMin = new Vector2(0, 0); hpFillRt.anchorMax = new Vector2(1, 1);
        hpFillRt.offsetMin = Vector2.zero; hpFillRt.offsetMax = Vector2.zero;
        ctx.hpFill = hpFill.AddComponent<Image>();
        ctx.hpFill.sprite = GetSolidSprite();
        ctx.hpFill.type = Image.Type.Filled;
        ctx.hpFill.fillMethod = Image.FillMethod.Vertical;
        ctx.hpFill.fillOrigin = 0; // bottom-up
        ctx.hpFill.raycastTarget = false;

        // Name (stretch)
        var nGo = NewText(go.transform, "Name", 10, TextAnchor.MiddleLeft,
            new Color(1f, 1f, 1f, 0.9f));
        var nLe = nGo.AddComponent<LayoutElement>();
        nLe.flexibleWidth = 1;
        nLe.minWidth = 50;
        nLe.preferredWidth = 80;
        ctx.nameText = nGo.GetComponent<Text>();
        ctx.nameText.horizontalOverflow = HorizontalWrapMode.Overflow;

        // Mass (52px, right aligned)
        var mGo = NewText(go.transform, "Mass", 9, TextAnchor.MiddleRight,
            new Color(1f, 1f, 1f, 0.5f));
        AddLayoutElement(mGo, 52, 0);
        ctx.massText = mGo.GetComponent<Text>();

        return ctx;
    }

    GameObject InstantiateSkin(Transform parent)
    {
        if (skinBallPrefab != null)
        {
            var inst = Instantiate(skinBallPrefab, parent, false);
            if (inst.GetComponent<Image>() == null)
                inst.AddComponent<Image>();
            return inst;
        }
        var go = new GameObject("SkinBall");
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().sprite = MobileCanvasSetup.CreateCircleSprite(16, Color.white);
        return go;
    }

    // ============================================================
    //  刷新
    // ============================================================
    void Refresh()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        // 聚合：playerId → PlayerInfo
        var players = new Dictionary<int, PlayerInfo>();
        foreach (var kv in GameManager.Circles)
        {
            var ctrl = kv.Value.GetComponent<CircleController>();
            if (ctrl == null || ctrl.playerId == 0) continue;

            if (!players.TryGetValue(ctrl.playerId, out var pi))
            {
                pi = new PlayerInfo();
                pi.name = ctrl.nameText != null ? ctrl.nameText.text : "???";
                pi.isLocal = ctrl.isLocalPlayer;
            }
            float diameter = Mathf.Max(ctrl.transform.localScale.x, 0.001f);
            pi.totalMass += 4f * diameter * diameter;
            pi.totalHp += ctrl.debugHp;
            pi.totalMaxHp += ctrl.debugMaxHp;
            var sr = ctrl.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) pi.skinSprite = sr.sprite;
            players[ctrl.playerId] = pi;
        }

        if (players.Count == 0) { _sortedCount = 0; ApplyVisibility(); return; }

        // 排序：质量降序
        var sorted = new List<KeyValuePair<int, PlayerInfo>>(players);
        sorted.Sort((a, b) => b.Value.totalMass.CompareTo(a.Value.totalMass));
        if (sorted.Count > maxRows) sorted.RemoveRange(maxRows, sorted.Count - maxRows);

        int localId = -1;
        for (int i = 0; i < sorted.Count; i++)
            if (sorted[i].Value.isLocal) { localId = sorted[i].Key; break; }

        // 确保行数量足够
        while (_rows.Count < sorted.Count) _rows.Add(BuildRow());
        while (_rows.Count > sorted.Count) { var last = _rows[^1]; Destroy(last.go); _rows.RemoveAt(_rows.Count - 1); }

        // 填充数据
        for (int i = 0; i < sorted.Count; i++)
            UpdateRow(_rows[i], i + 1, sorted[i].Value, sorted[i].Key == localId);

        _sortedCount = sorted.Count;
        ApplyVisibility();
    }

    void ApplyVisibility()
    {
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].go.SetActive(!_collapsed && i < _sortedCount);
    }

    void OnFoldClicked()
    {
        _collapsed = !_collapsed;
        ApplyVisibility();
    }

    struct PlayerInfo
    {
        public string name;
        public float totalMass;
        public float totalHp;
        public float totalMaxHp;
        public Sprite skinSprite;
        public bool isLocal;
    }

    void UpdateRow(RowCtx row, int rank, PlayerInfo info, bool isLocal)
    {
        row.rankText.text = rank.ToString();
        row.nameText.text = Truncate(info.name, 8);
        row.massText.text = ((int)info.totalMass).ToString();

        float ratio = info.totalMaxHp > 0f ? Mathf.Clamp01(info.totalHp / info.totalMaxHp) : 1f;
        row.hpFill.fillAmount = ratio;
        row.hpFill.color = ratio > 0.6f ? Color.green : ratio > 0.3f ? Color.yellow : Color.red;

        row.nameText.color = isLocal ? localHighlightColor : new Color(1f, 1f, 1f, 0.9f);
        row.rowBg.color = isLocal ? new Color(localHighlightColor.r, localHighlightColor.g, localHighlightColor.b, 0.15f) : new Color(0f, 0f, 0f, 0f);

        if (row.skinImage != null)
        {
            if (info.skinSprite != null) { row.skinImage.sprite = info.skinSprite; row.skinImage.color = Color.white; }
            else { row.skinImage.sprite = null; row.skinImage.color = new Color(0.8f, 0.8f, 0.8f, 1f); }
        }
    }

    // ============================================================
    //  辅助
    // ============================================================
    static Sprite _solidSprite;
    /// <summary>1x1 纯白 Sprite（Filled 血条底图，运行时生成一次复用）。</summary>
    static Sprite GetSolidSprite()
    {
        if (_solidSprite != null) return _solidSprite;
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color32[16];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        _solidSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        return _solidSprite;
    }

    static void AddLayoutElement(GameObject go, float width, float height)
    {
        var le = go.AddComponent<LayoutElement>();
        if (width > 0) { le.minWidth = width; le.preferredWidth = width; }
        if (height > 0) { le.minHeight = height; le.preferredHeight = height; }
    }

    static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? "???" : (s.Length > max ? s[..max] + ".." : s);

    static Font DefFont() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    static GameObject NewText(Transform parent, string goName, int size, TextAnchor align, Color color)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var txt = go.AddComponent<Text>();
        txt.font = DefFont(); txt.fontSize = size; txt.alignment = align; txt.color = color;
        txt.raycastTarget = false;
        return go;
    }
}
