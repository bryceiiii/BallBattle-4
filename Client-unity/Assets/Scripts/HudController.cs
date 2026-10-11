using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD 控制器。预制体模式：手动布局 UI 子物体，拖拽引用到对应字段。
/// </summary>
public class HudController : MonoBehaviour
{
    public static HudController Instance { get; private set; }

    // ===== HP 条引用（从预制体拖拽） =====
    [Header("HP 条")]
    public Image hpFillImage;
    public Image shieldFillImage;  // 护盾填充条
    public Text hpText;
    public Text massText;

    // ===== 弹药栏引用 =====
    [Header("弹药栏 — 高亮框")]
    public Image[] ammoHighlights;
    [Header("弹药栏 — 数量角标")]
    public Text[] ammoCounts;

    // ===== Buff 状态 =====
    [Header("Buff 图标")]
    public Image[] buffIcons;
    public Text[] buffTimers;

    // ===== 死亡面板 =====
    [Header("死亡面板（可选拖拽，空则自动生成）")]
    [Tooltip("死亡面板 Prefab（如 DeathPanel.prefab）。运行时实例化到本 HUD 下；留空则用代码自动生成")]
    public GameObject deathPanelPrefab;
    public GameObject deathPanel;
    public Text deathTitleText;
    public Button respawnButton;
    [Tooltip("点击后返回皮肤选择界面（可选拖拽，空则自动生成）")]
    public Button backToSkinButton;

    private int _selectedAmmo = 0;
    private double _shieldExpireMs;  // 护盾过期时间戳
    private double _speedExpireMs;   // 加速过期时间戳

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        if (hpFillImage != null)
        {
            hpFillImage.type = Image.Type.Filled;
            hpFillImage.fillMethod = Image.FillMethod.Horizontal;
            hpFillImage.fillOrigin = 0;
            hpFillImage.fillAmount = 1f;
        }
        if (shieldFillImage != null)
        {
            shieldFillImage.type = Image.Type.Filled;
            shieldFillImage.fillMethod = Image.FillMethod.Horizontal;
            shieldFillImage.fillOrigin = 0;
            shieldFillImage.fillAmount = 0;
        }

        // 死亡面板解析优先级：
        //   1) Inspector 已拖好 deathPanel 引用
        //   2) 本 HUD 子节点下已存在名为 "DeathPanel" 的面板（场景里手动摆的 Prefab 实例）
        //   3) 拖入的 deathPanelPrefab 实例化
        //   4) 代码自动生成
        if (deathPanel == null)
            deathPanel = FindExistingDeathPanel();

        if (deathPanel == null && deathPanelPrefab != null)
            InstantiateDeathPanelPrefab();

        if (deathPanel == null)
            BuildDeathPanel();

        // 无论面板来自预制体还是代码生成，都在这里补绑按钮事件
        // （预制体拖拽路径不会走 BuildDeathPanel，必须在此兜底，否则点了没反应）
        ResolveDeathPanelRefs();
        BindDeathPanelButtons();

        SelectAmmo(0);
    }

    /// <summary>在本 HUD 的子节点里查找已存在的死亡面板（场景手动摆的 Prefab 实例）。</summary>
    private GameObject FindExistingDeathPanel()
    {
        // 按名字精确找
        var t = transform.Find("DeathPanel");
        if (t != null) return t.gameObject;

        // 兜底：递归找一个带名为 RespawnBtn 子节点的面板
        foreach (var rt in GetComponentsInChildren<RectTransform>(true))
        {
            if (rt == transform) continue;
            if (rt.Find("RespawnBtn") != null)
            {
                Debug.Log($"[Hud] 自动识别到死亡面板: {rt.name}");
                return rt.gameObject;
            }
        }
        return null;
    }

    /// <summary>从死亡面板里解析出标题文本与两个按钮的引用。</summary>
    private void ResolveDeathPanelRefs()
    {
        if (deathPanel == null) return;

        if (deathTitleText == null)
        {
            var t = deathPanel.transform.Find("Title");
            if (t != null) deathTitleText = t.GetComponent<Text>();
        }

        if (respawnButton == null)
        {
            var rb = deathPanel.transform.Find("RespawnBtn");
            if (rb != null) respawnButton = rb.GetComponent<Button>();
        }

        // 换皮肤按钮：面板里没有就运行时补一个（放在 Respawn 按钮正下方，避免重叠）
        if (backToSkinButton == null)
        {
            var bb = deathPanel.transform.Find("BackToSkinBtn");
            if (bb != null) backToSkinButton = bb.GetComponent<Button>();
            else
            {
                Vector2 pos = new Vector2(0, -100);
                if (respawnButton != null)
                {
                    // 跟随 Respawn 按钮：X 对齐，Y 再往下挪一个按钮高度 + 间距
                    var rrt = respawnButton.GetComponent<RectTransform>();
                    float h = (rrt != null && rrt.sizeDelta.y > 0f) ? rrt.sizeDelta.y : 50f;
                    pos = new Vector2(rrt != null ? rrt.anchoredPosition.x : 0f,
                                      (rrt != null ? rrt.anchoredPosition.y : 0f) - h - 12f);
                }
                backToSkinButton = CreateDeathButton("BackToSkinBtn", "换皮肤",
                    new Color(0.25f, 0.35f, 0.6f, 0.9f), pos);
            }
        }
    }

    /// <summary>把 deathPanelPrefab 实例化到 HUD 下。</summary>
    private void InstantiateDeathPanelPrefab()
    {
        deathPanel = Instantiate(deathPanelPrefab, transform, false);
        deathPanel.name = "DeathPanel(Instance)";
        deathPanel.SetActive(false);
        Debug.Log("[Hud] 已从 deathPanelPrefab 实例化死亡面板");
    }

    void Update()
    {
        // 护盾倒计时
        if (_shieldExpireMs > 0 && buffTimers != null && buffTimers.Length > 0 && buffTimers[0] != null)
        {
            double remaining = (_shieldExpireMs - NowMs()) / 1000.0;
            if (remaining <= 0)
            {
                _shieldExpireMs = 0;
                ClearBuff(0);
            }
            else
            {
                buffTimers[0].text = $"{remaining:F1}s";
            }
        }
        // 加速倒计时
        if (_speedExpireMs > 0 && buffTimers != null && buffTimers.Length > 1 && buffTimers[1] != null)
        {
            double remaining = (_speedExpireMs - NowMs()) / 1000.0;
            if (remaining <= 0)
            {
                _speedExpireMs = 0;
                ClearBuff(1);
            }
            else
            {
                buffTimers[1].text = $"{remaining:F1}s";
            }
        }
    }

    private static double NowMs()
    {
        return System.DateTime.UtcNow.Subtract(new System.DateTime(1970, 1, 1)).TotalMilliseconds;
    }

    /// <summary>设置护盾（只激活图标和记录过期时间，倒计时由 Update 驱动）</summary>
    public void SetShield(double expireAtMs)
    {
        _shieldExpireMs = expireAtMs;
        if (buffIcons != null && buffIcons.Length > 0 && buffIcons[0] != null)
            buffIcons[0].gameObject.SetActive(true);
    }

    /// <summary>更新护盾条显示</summary>
    public void SetShieldBar(float shieldHp, float shieldMax)
    {
        if (shieldFillImage == null) return;
        if (shieldHp <= 0 || shieldMax <= 0)
        {
            shieldFillImage.fillAmount = 0;
            return;
        }
        shieldFillImage.fillAmount = Mathf.Clamp01(shieldHp / shieldMax);
    }

    /// <summary>清除护盾（过期或被打碎）</summary>
    public void ClearShield()
    {
        _shieldExpireMs = 0;
        SetShieldBar(0, 1);
        ClearBuff(0);
    }

    /// <summary>设置加速（激活图标和记录过期时间）</summary>
    public void SetSpeed(double expireAtMs)
    {
        _speedExpireMs = expireAtMs;
        if (buffIcons != null && buffIcons.Length > 1 && buffIcons[1] != null)
            buffIcons[1].gameObject.SetActive(true);
    }

    /// <summary>清除加速</summary>
    public void ClearSpeed()
    {
        _speedExpireMs = 0;
        ClearBuff(1);
    }

    private void BuildDeathPanel()
    {
        deathPanel = new GameObject("DeathPanel", typeof(RectTransform));
        deathPanel.transform.SetParent(transform, false);
        var rt = deathPanel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        // 半透明黑色遮罩
        var overlay = deathPanel.AddComponent<Image>();
        overlay.color = new Color(0, 0, 0, 0.7f);

        // 标题 "You Died"
        var titleGo = new GameObject("Title", typeof(Text));
        titleGo.transform.SetParent(deathPanel.transform, false);
        deathTitleText = titleGo.GetComponent<Text>();
        deathTitleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        deathTitleText.fontSize = 64;
        deathTitleText.alignment = TextAnchor.MiddleCenter;
        deathTitleText.color = Color.red;
        deathTitleText.text = "You Died";
        var titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 0.5f); titleRt.anchorMax = new Vector2(0.5f, 0.5f);
        titleRt.sizeDelta = new Vector2(400, 80);
        titleRt.anchoredPosition = new Vector2(0, 40);

        // 重新开始按钮
        respawnButton = CreateDeathButton("RespawnBtn", "Respawn",
            new Color(0.2f, 0.6f, 0.2f, 0.9f), new Vector2(0, -40));

        // 返回皮肤选择界面按钮
        backToSkinButton = CreateDeathButton("BackToSkinBtn", "换皮肤",
            new Color(0.25f, 0.35f, 0.6f, 0.9f), new Vector2(0, -100));

        // 事件绑定统一交给 BindDeathPanelButtons()，避免与预制体路径重复绑定
        deathPanel.SetActive(false);
    }

    /// <summary>
    /// 给死亡面板按钮补绑事件。预制体拖拽路径不会走 BuildDeathPanel，
    /// 若不在 Start 里统一绑定，Inspector 里只拖字段、没连 onClick 的按钮会点了没反应。
    /// </summary>
    public void BindDeathPanelButtons()
    {
        if (respawnButton != null)
        {
            respawnButton.onClick.RemoveListener(OnRespawnClicked);
            respawnButton.onClick.AddListener(OnRespawnClicked);
            Debug.Log($"[Hud] 已绑定 Respawn 按钮: {respawnButton.name}");
        }
        else Debug.LogWarning("[Hud] respawnButton 未赋值，Rebirth 按钮不会响应");

        if (backToSkinButton != null)
        {
            backToSkinButton.onClick.RemoveListener(OnBackToSkinClicked);
            backToSkinButton.onClick.AddListener(OnBackToSkinClicked);
            Debug.Log($"[Hud] 已绑定 换皮肤 按钮: {backToSkinButton.name}");
        }
        else Debug.LogWarning("[Hud] backToSkinButton 未赋值，换皮肤按钮不会响应");
    }

    /// <summary>死亡面板里造一个居中按钮（程序化兜底路径用）。</summary>
    private Button CreateDeathButton(string goName, string label, Color bgColor, Vector2 anchoredPos)
    {
        var btnGo = new GameObject(goName, typeof(Image), typeof(Button));
        btnGo.transform.SetParent(deathPanel.transform, false);
        btnGo.GetComponent<Image>().color = bgColor;
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.5f, 0.5f); btnRt.anchorMax = new Vector2(0.5f, 0.5f);
        btnRt.sizeDelta = new Vector2(200, 50);
        btnRt.anchoredPosition = anchoredPos;

        var btnTxtGo = new GameObject("BtnText", typeof(Text));
        btnTxtGo.transform.SetParent(btnGo.transform, false);
        var btnTxt = btnTxtGo.GetComponent<Text>();
        btnTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        btnTxt.fontSize = 24;
        btnTxt.alignment = TextAnchor.MiddleCenter;
        btnTxt.color = Color.white;
        btnTxt.text = label;
        var btnTxtRt = btnTxtGo.GetComponent<RectTransform>();
        btnTxtRt.anchorMin = Vector2.zero; btnTxtRt.anchorMax = Vector2.one;
        btnTxtRt.sizeDelta = Vector2.zero;

        return btnGo.GetComponent<Button>();
    }

    private void OnRespawnClicked()
    {
        Debug.Log("[Hud] 点击 Respawn");
        HideDeathScreen();
        GameManager.Instance?.RespawnPlayer();
    }

    /// <summary>死亡面板「换皮肤」：关掉游戏 HUD，回到皮肤选择界面。</summary>
    private void OnBackToSkinClicked()
    {
        Debug.Log("[Hud] 点击 换皮肤");
        HideDeathScreen();

        // 隐藏游戏内 HUD（含死亡面板），否则会叠在皮肤面板上
        var gm = GameManager.Instance;
        if (gm != null && gm.canvasGo != null)
            gm.canvasGo.SetActive(false);

        // 显示皮肤选择界面（优先走静态单例，兜底走 LobbyUIController 引用）
        var skinUI = SkinSelectionUIController.Instance
                     ?? (LobbyUIController.Instance != null ? LobbyUIController.Instance.skinSelectionUI : null);
        if (skinUI == null)
            Debug.LogError("[Hud] 找不到 SkinSelectionUIController，无法返回皮肤界面");
        else
            skinUI.Show();
    }

    /// <summary>显示死亡画面</summary>
    public void ShowDeathScreen()
    {
        if (deathPanel != null)
        {
            deathPanel.SetActive(true);
            deathPanel.transform.SetAsLastSibling(); // 置顶，避免被 HUD 其它元素遮挡点击
            Debug.Log($"[Hud] ShowDeathScreen: active={deathPanel.activeInHierarchy}, " +
                      $"respawn={respawnButton != null}, backSkin={backToSkinButton != null}, " +
                      $"parent={deathPanel.transform.parent?.name}");
        }
        else Debug.LogWarning("[Hud] ShowDeathScreen 但 deathPanel 为空！");
    }

    /// <summary>隐藏死亡画面</summary>
    public void HideDeathScreen()
    {
        if (deathPanel != null) deathPanel.SetActive(false);
    }

    // ==================== 公开更新接口 ====================

    /// <summary>更新 HP 条</summary>
    public void SetHp(float hp, float maxHp)
    {
        if (hpFillImage != null)
        {
            float ratio = Mathf.Clamp01(hp / maxHp);
            hpFillImage.fillAmount = ratio;
            hpFillImage.color = ratio > 0.6f ? Color.green : ratio > 0.3f ? Color.yellow : Color.red;
        }
        if (hpText != null)
            hpText.text = $"{hp:F1} / {maxHp:F1} HP";
    }

    /// <summary>更新质量显示</summary>
    public void SetMass(float mass)
    {
        if (massText != null)
            massText.text = $"Mass: {mass:F1}";
    }

    /// <summary>切换当前选中弹种（0~4）</summary>
    public void SelectAmmo(int index)
    {
        if (ammoHighlights == null) return;
        for (int i = 0; i < ammoHighlights.Length; i++)
        {
            if (ammoHighlights[i] != null)
                ammoHighlights[i].color = i == index ? new Color(1f, 1f, 0.5f, 0.3f) : Color.clear;
        }
        _selectedAmmo = index;
    }

    /// <summary>更新弹药栏某弹种的剩余数量</summary>
    public void SetAmmoCount(int index, int count)
    {
        if (ammoCounts == null || index < 0 || index >= ammoCounts.Length) return;
        if (ammoCounts[index] != null)
            ammoCounts[index].text = index == 0 ? "∞" : count.ToString();
    }

    /// <summary>更新弹药栏数量（带上限显示）</summary>
    public void SetAmmoCountMax(int index, int count, int max)
    {
        if (ammoCounts == null || index < 0 || index >= ammoCounts.Length) return;
        if (ammoCounts[index] != null)
            ammoCounts[index].text = index == 0 ? "∞" : $"{count}/{max}";
    }

    /// <summary>显示/更新 Buff 状态</summary>
    public void SetBuff(int slot, Sprite icon, float remainingSeconds)
    {
        if (buffIcons == null || slot < 0 || slot >= buffIcons.Length) return;
        if (buffIcons[slot] == null) return;
        buffIcons[slot].sprite = icon;
        buffIcons[slot].gameObject.SetActive(true);
        if (buffTimers != null && slot < buffTimers.Length && buffTimers[slot] != null)
            buffTimers[slot].text = $"{remainingSeconds:F1}s";
    }

    /// <summary>隐藏指定 Buff 槽</summary>
    public void ClearBuff(int slot)
    {
        if (buffIcons == null || slot < 0 || slot >= buffIcons.Length) return;
        if (buffIcons[slot] != null) buffIcons[slot].gameObject.SetActive(false);
    }
}
