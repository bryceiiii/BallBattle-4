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
    public GameObject deathPanel;
    public Text deathTitleText;
    public Button respawnButton;

    private int _selectedAmmo = 0;

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

        // 自动创建死亡面板（预制体未提供时）
        if (deathPanel == null)
            BuildDeathPanel();

        SelectAmmo(0);
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
        var btnGo = new GameObject("RespawnBtn", typeof(Image), typeof(Button));
        btnGo.transform.SetParent(deathPanel.transform, false);
        respawnButton = btnGo.GetComponent<Button>();
        btnGo.GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.2f, 0.9f);
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.5f, 0.5f); btnRt.anchorMax = new Vector2(0.5f, 0.5f);
        btnRt.sizeDelta = new Vector2(200, 50);
        btnRt.anchoredPosition = new Vector2(0, -40);
        var btnTxtGo = new GameObject("BtnText", typeof(Text));
        btnTxtGo.transform.SetParent(btnGo.transform, false);
        var btnTxt = btnTxtGo.GetComponent<Text>();
        btnTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        btnTxt.fontSize = 24;
        btnTxt.alignment = TextAnchor.MiddleCenter;
        btnTxt.color = Color.white;
        btnTxt.text = "Respawn";
        var btnTxtRt = btnTxtGo.GetComponent<RectTransform>();
        btnTxtRt.anchorMin = Vector2.zero; btnTxtRt.anchorMax = Vector2.one;
        btnTxtRt.sizeDelta = Vector2.zero;

        respawnButton.onClick.AddListener(OnRespawnClicked);

        deathPanel.SetActive(false);
    }

    private void OnRespawnClicked()
    {
        HideDeathScreen();
        GameManager.Instance?.RespawnPlayer();
    }

    /// <summary>显示死亡画面</summary>
    public void ShowDeathScreen()
    {
        if (deathPanel != null) deathPanel.SetActive(true);
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
