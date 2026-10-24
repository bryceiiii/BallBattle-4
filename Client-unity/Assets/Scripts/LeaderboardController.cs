using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 排行榜。UI 层级在场景里手工搭好（Panel / Header / RowsContainer / RowTemplate），
/// 本脚本只负责「填数据 + 复制行」，不再在运行时生成任何界面元素。
/// 因此面板大小、行高、锚点、字体、颜色都可以直接在 Editor 里拖 / 改。
/// </summary>
[DisallowMultipleComponent]
public class LeaderboardController : MonoBehaviour
{
    public static LeaderboardController Instance { get; private set; }

    [Header("引用（留空会按名字自动查找）")]
    [Tooltip("面板根节点。高度交给 ContentSizeFitter 时可以只改宽度")]
    [SerializeField] private RectTransform panelRect;
    [Tooltip("行的父节点，需要 VerticalLayoutGroup")]
    [SerializeField] private RectTransform rowsContainer;
    [Tooltip("行模板：运行时会克隆它。保持未激活即可，不会显示在界面上")]
    [SerializeField] private GameObject rowTemplate;
    [SerializeField] private Text foldLabel;
    [SerializeField] private Button foldButton;
    [SerializeField] private Button settingsButton;

    [Header("数据")]
    [Tooltip("最多显示几行。行高由 RowTemplate 的 LayoutElement 决定")]
    [SerializeField] private int maxRows = 5;
    [Range(0.2f, 2f)] [SerializeField] private float refreshInterval = 0.5f;

    [Header("运行时配色")]
    [SerializeField] private Color rank1Color = new Color(0.98f, 0.77f, 0.23f, 1f);
    [SerializeField] private Color rank2Color = new Color(0.72f, 0.80f, 0.90f, 1f);
    [SerializeField] private Color rank3Color = new Color(0.86f, 0.50f, 0.30f, 1f);
    [SerializeField] private Color rankDefaultColor = new Color(0.34f, 0.42f, 0.54f, 0.92f);
    [SerializeField] private Color localAccentColor = new Color(0.48f, 0.82f, 1f, 1f);
    [SerializeField] private Color hpGoodColor = new Color(0.24f, 0.90f, 0.58f, 1f);
    [SerializeField] private Color hpWarnColor = new Color(0.98f, 0.76f, 0.22f, 1f);
    [SerializeField] private Color hpBadColor = new Color(1f, 0.35f, 0.32f, 1f);

    private bool _collapsed;
    private int _sortedCount;
    private float _timer;
    private bool _warnedMissingRefs;
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

    private struct PlayerInfo
    {
        public string name;
        public float totalMass;
        public float totalHp;
        public float totalMaxHp;
        public Sprite skinSprite;
        public bool isLocal;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        ResolveReferences();
    }

    private void Start()
    {
        if (foldButton != null) foldButton.onClick.AddListener(OnFoldClicked);
        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveListener(OnSettingsClicked);
            settingsButton.onClick.AddListener(OnSettingsClicked);
        }

        var externalSettings = GameAudioManager.Instance != null
            ? GameAudioManager.Instance.settingsButton
            : null;
        if (externalSettings != null)
            externalSettings.gameObject.SetActive(false);

        SetCollapsed(false);
        ApplyVisibility();
    }

    private void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer < refreshInterval) return;
        _timer = 0f;
        Refresh();
    }

    // ---------------------------------------------------------------- 引用

    private void ResolveReferences()
    {
        if (panelRect == null) panelRect = transform as RectTransform;

        if (rowsContainer == null)
            rowsContainer = FindDeep<RectTransform>(transform, "RowsContainer");

        if (rowTemplate == null)
        {
            var holder = rowsContainer != null ? rowsContainer : (Transform)transform;
            var t = holder.Find("RowTemplate");
            if (t != null) rowTemplate = t.gameObject;
        }

        // 模板在编辑器里保持激活是为了让你能看到并调整行样式；
        // 进 Play 后立刻隐藏，只作为克隆源。
        if (rowTemplate != null && rowTemplate.activeSelf)
            rowTemplate.SetActive(false);

        if (foldLabel == null) foldLabel = FindDeep<Text>(transform, "FoldLabel");
        if (foldButton == null)
        {
            var t = FindDeep<Transform>(transform, "FoldBtn");
            if (t != null) foldButton = t.GetComponent<Button>();
        }
        if (settingsButton == null)
        {
            var t = FindDeep<Transform>(transform, "SettingsButton");
            if (t != null) settingsButton = t.GetComponent<Button>();
        }

        if (!_warnedMissingRefs && (rowsContainer == null || rowTemplate == null))
        {
            _warnedMissingRefs = true;
            Debug.LogError(
                "[Leaderboard] 缺少 RowsContainer 或 RowTemplate 引用，排行榜不会显示任何行。" +
                "请检查 HudCanvas/Leaderboard 下的层级是否完整。", this);
        }
    }

    private static T FindDeep<T>(Transform root, string name) where T : Component
    {
        var all = root.GetComponentsInChildren<T>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == name) return all[i];
        }
        return null;
    }

    // ---------------------------------------------------------------- 数据

    private void Refresh()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        if (rowsContainer == null || rowTemplate == null) return;

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

        int rowLimit = Mathf.Max(1, maxRows);
        if (sorted.Count > rowLimit)
            sorted.RemoveRange(rowLimit, sorted.Count - rowLimit);

        SyncRowCount(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
            UpdateRow(_rows[i], i + 1, sorted[i].Value);

        _sortedCount = sorted.Count;
        ApplyVisibility();
    }

    private void SyncRowCount(int count)
    {
        while (_rows.Count < count)
        {
            var go = Instantiate(rowTemplate, rowsContainer, false);
            go.name = $"Row_{_rows.Count + 1}";
            if (!go.activeSelf) go.SetActive(true);
            _rows.Add(BindRow(go));
        }

        while (_rows.Count > count)
        {
            var last = _rows[_rows.Count - 1];
            if (last.go != null) Destroy(last.go);
            _rows.RemoveAt(_rows.Count - 1);
        }
    }

    private static RowCtx BindRow(GameObject go)
    {
        var t = go.transform;
        return new RowCtx
        {
            go = go,
            rowBg = go.GetComponent<Image>(),
            localAccent = FindChildImage(t, "LocalAccent"),
            rankBadge = FindChildImage(t, "RankBadge"),
            rankText = FindChildText(t, "RankBadge/Rank"),
            skinImage = FindChildImage(t, "SkinBall"),
            nameText = FindChildText(t, "Name"),
            massText = FindChildText(t, "Mass"),
            hpFill = FindChildImage(t, "HpBg/HpFill"),
        };
    }

    private static Image FindChildImage(Transform root, string path)
    {
        var t = root.Find(path);
        return t != null ? t.GetComponent<Image>() : null;
    }

    private static Text FindChildText(Transform root, string path)
    {
        var t = root.Find(path);
        return t != null ? t.GetComponent<Text>() : null;
    }

    private void UpdateRow(RowCtx row, int rank, PlayerInfo info)
    {
        if (row.rankText != null) row.rankText.text = rank.ToString();
        if (row.nameText != null) row.nameText.text = Truncate(info.name, 13);
        if (row.massText != null)
        {
            row.massText.text = info.totalMass >= 1000f
                ? $"{info.totalMass / 1000f:F1}K"
                : Mathf.RoundToInt(info.totalMass).ToString();
        }

        Color rankColor = rank switch
        {
            1 => rank1Color,
            2 => rank2Color,
            3 => rank3Color,
            _ => rankDefaultColor
        };

        if (row.rankBadge != null) row.rankBadge.color = rankColor;
        if (row.rankText != null)
            row.rankText.color = rank <= 3 ? new Color(0.08f, 0.10f, 0.14f, 1f) : Color.white;

        if (row.nameText != null)
        {
            row.nameText.color = info.isLocal
                ? localAccentColor
                : new Color(0.94f, 0.97f, 1f, 1f);
        }

        if (row.rowBg != null)
        {
            row.rowBg.color = info.isLocal
                ? new Color(0.08f, 0.20f, 0.34f, 0.88f)
                : rank == 1
                    ? new Color(0.19f, 0.16f, 0.08f, 0.82f)
                    : new Color(0.10f, 0.14f, 0.22f, 0.70f);
        }

        if (row.localAccent != null)
        {
            row.localAccent.color = localAccentColor;
            row.localAccent.gameObject.SetActive(info.isLocal);
        }

        if (row.hpFill != null)
        {
            float ratio = info.totalMaxHp > 0f
                ? Mathf.Clamp01(info.totalHp / info.totalMaxHp)
                : 0f;
            row.hpFill.fillAmount = ratio;
            row.hpFill.color = ratio > 0.6f
                ? hpGoodColor
                : ratio > 0.3f
                    ? hpWarnColor
                    : hpBadColor;
        }

        if (row.skinImage == null) return;
        if (info.skinSprite != null)
        {
            row.skinImage.sprite = info.skinSprite;
            row.skinImage.color = Color.white;
        }
        else
        {
            row.skinImage.sprite = null;
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

    // ---------------------------------------------------------------- 交互

    private void OnFoldClicked() => SetCollapsed(!_collapsed);

    private void SetCollapsed(bool collapsed)
    {
        _collapsed = collapsed;
        if (foldLabel != null) foldLabel.text = collapsed ? "+" : "−";
        if (rowsContainer != null) rowsContainer.gameObject.SetActive(!collapsed);
        ApplyVisibility();
    }

    private static void OnSettingsClicked()
    {
        if (GameAudioManager.Instance != null)
            GameAudioManager.Instance.OpenSettings();
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "Player";
        return value.Length > max ? value[..max] + ".." : value;
    }
}
