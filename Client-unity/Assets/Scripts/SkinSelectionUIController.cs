using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 皮肤选择面板控制器。
/// 连接成功后自动显示，玩家必须选择皮肤并点击"进入游戏"。
/// 皮肤数据从 SkinManager.skins 读取，UI 由代码自动构建。
/// </summary>
public class SkinSelectionUIController : MonoBehaviour
{
    [Header("调试开关")]
    [Tooltip("开启=运行时用代码覆盖样式 | 关闭=保留预制体原始样式")]
    public bool useCodeStyles = true;

    [Header("主面板")]
    public GameObject skinPanel;

    [Header("皮肤网格容器")]
    [Tooltip("皮肤卡片将作为它的子物体动态生成。建议挂一个 GridLayoutGroup。")]
    public RectTransform skinGridContainer;

    [Header("皮肤卡片预制体")]
    [Tooltip("可选的卡片按钮预制体。留空则代码自动创建。")]
    public GameObject skinCardPrefab;

    [Header("UI 文本")]
    public Text titleText;             // 如 "选择你的皮肤"
    public Text selectedSkinNameText;   // 显示当前选中皮肤名
    public Text confirmButtonLabel;     // 确认按钮文字

    [Header("按钮")]
    public Button confirmButton;        // "进入游戏" 按钮
    public Button backButton;           // 返回按钮（可选）

    [Header("皮肤卡片样式参数")]
    public float cardSize = 120f;
    public float cardSpacing = 16f;
    public float cardSpritePadding = 12f;
    public Color cardNormalColor = new Color(0.15f, 0.15f, 0.22f, 1f);
    public Color cardSelectedColor = new Color(0.30f, 0.55f, 1f, 0.5f);
    public Color cardHighlightColor = new Color(0.22f, 0.22f, 0.32f, 1f);

    // 内部状态
    private List<Button> _skinCards = new List<Button>();
    private List<Image> _cardBgs = new List<Image>();
    private int _selectedIndex = -1;
    private SkinManager _skinManager;

    void Start()
    {
        _skinManager = SkinManager.Instance;
        if (_skinManager == null)
        {
            Debug.LogError("[SkinSelectionUI] SkinManager.Instance 为空！请在场景中挂载 SkinManager。");
            return;
        }

        if (useCodeStyles) ApplyVisualStyles();
        BindEvents();
        BuildSkinGrid();

        // 初始隐藏
        if (skinPanel != null) skinPanel.SetActive(false);

        // 注册连接成功回调
        SpacetimeDBNetworkManager.OnConnected += OnConnected;
    }

    void OnDestroy()
    {
        SpacetimeDBNetworkManager.OnConnected -= OnConnected;
    }

    // ============================================================
    //  🎨 视觉样式
    // ============================================================

    private static readonly Color DarkBg      = new Color(0.10f, 0.10f, 0.18f, 1f);
    private static readonly Color LightText   = new Color(0.90f, 0.90f, 0.95f, 1f);
    private static readonly Color AccentBlue  = new Color(0.30f, 0.55f, 1f, 1f);
    private static readonly Color AccentGreen = new Color(0.25f, 0.75f, 0.40f, 1f);
    private static readonly Color White       = Color.white;

    private void ApplyVisualStyles()
    {
        // 面板背景
        if (skinPanel != null)
        {
            var panelImg = skinPanel.GetComponent<Image>();
            if (panelImg == null)
                panelImg = skinPanel.AddComponent<Image>();
            panelImg.color = DarkBg;
        }

        // 标题
        if (titleText != null)
        {
            titleText.color = LightText;
            titleText.fontSize = 28;
            titleText.fontStyle = FontStyle.Bold;
        }

        // 选中名
        if (selectedSkinNameText != null)
            selectedSkinNameText.color = LightText;

        // 按钮
        if (confirmButton != null)
        {
            StyleButton(confirmButton, AccentGreen);
            confirmButton.interactable = false; // 初始禁用
        }
        if (backButton != null)
            StyleButton(backButton, new Color(0.4f, 0.4f, 0.5f, 1f));

        if (confirmButtonLabel != null)
            confirmButtonLabel.color = White;
    }

    private static void StyleButton(Button btn, Color color)
    {
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = color;
        var cb = btn.colors;
        cb.normalColor = color;
        cb.highlightedColor = color * 1.15f;
        cb.pressedColor = color * 0.75f;
        cb.disabledColor = new Color(0.25f, 0.25f, 0.35f, 0.5f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.1f;
        btn.colors = cb;
        var label = btn.GetComponentInChildren<Text>();
        if (label != null) label.color = Color.white;
    }

    // ============================================================
    //  🔗 事件绑定
    // ============================================================

    private void BindEvents()
    {
        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmClicked);

        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);
    }

    // ============================================================
    //  🌐 网络回调 —— 连接成功后显示皮肤选择
    // ============================================================

    private void OnConnected()
    {
        Debug.Log("[SkinSelectionUI] 已连接，显示皮肤选择面板");
        Show();
    }

    // ============================================================
    //  🧱 构建皮肤网格
    // ============================================================

    private void BuildSkinGrid()
    {
        if (skinGridContainer == null || _skinManager == null) return;

        var skins = _skinManager.skins;
        if (skins.Count == 0)
        {
            Debug.LogWarning("[SkinSelectionUI] SkinManager.skins 为空！请在 SkinManager 中拖入皮肤 Sprite。");
            return;
        }

        // 如果没有在 Inspector 指定 prefab，则创建一个运行时默认模板
        if (skinCardPrefab == null)
            CreateRuntimeSkinCardPrefab();

        // 设置 GridLayoutGroup
        var gridLayout = skinGridContainer.GetComponent<GridLayoutGroup>();
        if (gridLayout == null)
            gridLayout = skinGridContainer.gameObject.AddComponent<GridLayoutGroup>();

        gridLayout.cellSize = new Vector2(cardSize, cardSize);
        gridLayout.spacing = new Vector2(cardSpacing, cardSpacing);
        gridLayout.padding = new RectOffset((int)cardSpacing, (int)cardSpacing, (int)cardSpacing, (int)cardSpacing);
        gridLayout.childAlignment = TextAnchor.MiddleCenter;
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = Mathf.Min(skins.Count, 6); // 每行最多6张

        // 清理旧项（如果有）
        _skinCards.Clear();
        _cardBgs.Clear();

        // 创建皮肤卡片
        for (int i = 0; i < skins.Count; i++)
        {
            CreateSkinCard(i, skins[i]);
        }
    }

    /// <summary>
    /// 当 Inspector 未设置 prefab 时，运行时创建一个合理的卡片模板。
    /// 模板不会保存到场景（HideFlags.DontSave）。
    /// </summary>
    private void CreateRuntimeSkinCardPrefab()
    {
        // 已经创建过则跳过
        if (skinCardPrefab != null) return;

        var prefab = new GameObject("SkinCard_Prefab_Runtime", typeof(RectTransform), typeof(Image), typeof(Button));
        prefab.hideFlags = HideFlags.DontSave;

        var rt = prefab.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(cardSize, cardSize);

        // 背景（Button 的 targetGraphic）
        var bg = prefab.GetComponent<Image>();
        bg.color = cardNormalColor;

        // Button 配置
        var btn = prefab.GetComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint;
        var colors = btn.colors;
        colors.normalColor = cardNormalColor;
        colors.highlightedColor = cardHighlightColor;
        colors.pressedColor = cardNormalColor * 0.8f;
        colors.disabledColor = new Color(0.25f, 0.25f, 0.35f, 0.5f);
        btn.colors = colors;

        // SpritePreview 子物体
        var previewGo = new GameObject("SpritePreview", typeof(RectTransform));
        previewGo.transform.SetParent(prefab.transform, false);
        var previewRt = previewGo.GetComponent<RectTransform>();
        previewRt.anchorMin = new Vector2(0.05f, 0.25f);
        previewRt.anchorMax = new Vector2(0.95f, 0.95f);
        previewRt.offsetMin = Vector2.zero;
        previewRt.offsetMax = Vector2.zero;
        var previewImg = previewGo.AddComponent<Image>();
        previewImg.preserveAspect = true;

        // Label 子物体
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(prefab.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0f, 0f);
        labelRt.anchorMax = new Vector2(1f, 0.25f);
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        var label = labelGo.AddComponent<Text>();
        label.text = "Skin";
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 12;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.9f, 0.9f, 0.95f);

        // 最后将运行时模板赋值给 skinCardPrefab（不会被序列化）
        skinCardPrefab = prefab;
    }

    private void CreateSkinCard(int index, SkinManager.SkinDef skinDef)
    {
        GameObject cardGo;

        if (skinCardPrefab != null)
        {
            // 使用 prefab 实例化（不直接依赖预制体在 Inspector 中的设置）
            cardGo = Instantiate(skinCardPrefab, skinGridContainer);
            cardGo.name = $"SkinCard_{index}";

            // 确保 RectTransform 大小匹配样式参数
            var rt = cardGo.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(cardSize, cardSize);

            // 尝试寻找并设置预览图片和标签
            var previewImg = cardGo.transform.Find("SpritePreview")?.GetComponent<Image>();
            if (previewImg != null)
            {
                previewImg.sprite = skinDef.sprite;
                previewImg.color = skinDef.tintColor;
                previewImg.preserveAspect = true;
            }

            var label = cardGo.transform.Find("Label")?.GetComponent<Text>();
            if (label != null)
            {
                label.text = skinDef.skinName;
                // 保证使用内置字体（如果未设置）
                if (label.font == null)
                    label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.color = new Color(0.9f, 0.9f, 0.95f);
            }

            // 如果启用了代码样式，应用统一样式到按钮与背景
            if (useCodeStyles)
            {
                var btn = cardGo.GetComponent<Button>();
                if (btn != null)
                    StyleButton(btn, new Color(0.18f, 0.18f, 0.26f, 1f)); // 稍暗的按钮色作为 base
                var bgImg = cardGo.GetComponent<Image>();
                if (bgImg != null)
                    bgImg.color = cardNormalColor;
            }
        }
        else
        {
            // 程序化创建卡片（原有逻辑）
            cardGo = new GameObject($"SkinCard_{index}", typeof(RectTransform));
            cardGo.transform.SetParent(skinGridContainer, false);

            var cardRt = cardGo.GetComponent<RectTransform>();
            cardRt.sizeDelta = new Vector2(cardSize, cardSize);

            // 背景
            var bgImg = cardGo.AddComponent<Image>();
            bgImg.color = cardNormalColor;

            // 按钮
            var btn = cardGo.AddComponent<Button>();
            var spritePreviewGo = new GameObject("SpritePreview", typeof(RectTransform));
            spritePreviewGo.transform.SetParent(cardGo.transform, false);

            var previewRt = spritePreviewGo.GetComponent<RectTransform>();
            previewRt.anchorMin = new Vector2(0.05f, 0.25f);  // 上方留给名称
            previewRt.anchorMax = new Vector2(0.95f, 0.95f);
            previewRt.offsetMin = Vector2.zero;
            previewRt.offsetMax = Vector2.zero;

            var previewImg = spritePreviewGo.AddComponent<Image>();
            previewImg.sprite = skinDef.sprite;
            previewImg.color = skinDef.tintColor;
            previewImg.preserveAspect = true;

            // 皮肤名称标签
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(cardGo.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(1f, 0.25f);
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            var label = labelGo.AddComponent<Text>();
            label.text = skinDef.skinName;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 12;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.9f, 0.9f, 0.95f);
        }

        // 获取 Button 和 Image 引用
        var button = cardGo.GetComponent<Button>();
        var cardBg = cardGo.GetComponent<Image>();

        if (button == null || cardBg == null)
        {
            Debug.LogWarning($"[SkinSelectionUI] SkinCard_{index} 缺少 Button 或 Image 组件。");
            return;
        }

        // 绑定点击
        int idx = index; // 闭包捕获
        button.onClick.AddListener(() => OnSkinCardClicked(idx));

        _skinCards.Add(button);
        _cardBgs.Add(cardBg);
    }

    // ============================================================
    //  🖱️ 交互回调
    // ============================================================

    private void OnSkinCardClicked(int index)
    {
        if (_skinManager == null) return;

        _skinManager.SelectSkin(index);
        _selectedIndex = index;

        // 更新卡片高亮
        UpdateCardHighlights();

        // 更新选中名称
        var skin = _skinManager.SelectedSkin;
        if (selectedSkinNameText != null && skin != null)
            selectedSkinNameText.text = $"已选择: {skin.skinName}";

        // 启用确认按钮
        if (confirmButton != null)
            confirmButton.interactable = true;
    }

    private void UpdateCardHighlights()
    {
        for (int i = 0; i < _cardBgs.Count; i++)
        {
            if (_cardBgs[i] == null) continue;
            _cardBgs[i].color = (i == _selectedIndex) ? cardSelectedColor : cardNormalColor;
        }
    }

    private void OnConfirmClicked()
    {
        if (!_skinManager.HasValidSelection) return;

        Debug.Log($"[SkinSelectionUI] 确认皮肤：{_skinManager.SelectedSkin.skinName}，进入游戏…");

        // 隐藏皮肤面板
        if (skinPanel != null) skinPanel.SetActive(false);

        // 回调大厅控制器执行 EnterGame
        LobbyUIController.Instance?.OnSkinConfirmed();
    }

    private void OnBackClicked()
    {
        Debug.Log("[SkinSelectionUI] 返回大厅");
        if (skinPanel != null) skinPanel.SetActive(false);

        // 显示大厅面板
        if (LobbyUIController.Instance != null && LobbyUIController.Instance.lobbyPanel != null)
            LobbyUIController.Instance.lobbyPanel.SetActive(true);
    }

    // ============================================================
    //  📺 显隐控制
    // ============================================================

    /// <summary>显示皮肤选择面板</summary>
    public void Show()
    {
        if (skinPanel != null)
        {
            skinPanel.SetActive(true);
            // 确保在最上层
            skinPanel.transform.SetAsLastSibling();
        }

        _selectedIndex = -1;
        UpdateCardHighlights();

        if (selectedSkinNameText != null)
            selectedSkinNameText.text = "请选择皮肤";

        if (confirmButton != null)
            confirmButton.interactable = false;
    }

    /// <summary>隐藏面板</summary>
    public void Hide()
    {
        if (skinPanel != null)
            skinPanel.SetActive(false);
    }
}
