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
    public Image[] buffIcons;     // 最多 3 个 Buff 图标
    public Text[] buffTimers;     // 对应的倒计时文本

    private int _selectedAmmo = 0;

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        // 确保 HP 填充条用 Filled 模式（用户预制体可能没配）
        if (hpFillImage != null)
        {
            hpFillImage.type = Image.Type.Filled;
            hpFillImage.fillMethod = Image.FillMethod.Horizontal;
            hpFillImage.fillOrigin = 0;
            hpFillImage.fillAmount = 1f;
        }

        SelectAmmo(0);
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
                ammoHighlights[i].color = i == index ? new Color(1f, 1f, 0.5f, 0.8f) : Color.clear;
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
