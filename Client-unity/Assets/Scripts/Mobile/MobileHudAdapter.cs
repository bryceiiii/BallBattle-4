using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 手机端 HUD 布局适配器。
/// 在 HudController 基础上，针对手机屏幕调整 HP条/弹药栏/Buff 图标的大小和位置。
/// 挂在 HudController 同级的 GameObject 上，自动调整其子元素。
/// </summary>
public class MobileHudAdapter : MonoBehaviour
{
    public static MobileHudAdapter Instance { get; private set; }

    [Header("手机端缩放系数")]
    [Range(1f, 3f)]
    public float hpBarScale = 1.8f;      // HP条整体放大
    [Range(1f, 3f)]
    public float ammoBarScale = 1.5f;    // 弹药栏放大
    [Range(1f, 3f)]
    public float massTextScale = 1.5f;   // 质量文字放大

    [Header("间距")]
    public float marginTop = 60f;        // 距屏幕顶部
    public float marginSide = 40f;       // 距屏幕左右

    [Header("弹药栏位置（手机）")]
    public bool repositionAmmoBar = true; // 移到屏幕底部（靠近射击按钮）

    public HudController hud;
    private bool _applied;

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        hud = GetComponent<HudController>();

#if UNITY_ANDROID || UNITY_IOS
        ApplyMobileLayout();
#else
        // Editor中也可能需要测试手机布局，检查MobileInputController的设置
        if (MobileInputController.Instance != null)
            ApplyMobileLayout();
#endif
    }

    public void ApplyMobileLayout()
    {
        if (_applied) return;
        _applied = true;

        if (hud == null)
        {
            Debug.LogWarning("[MobileHudAdapter] 未找到 HudController，跳过手机布局适配");
            return;
        }

        // 1. HP 条放大
        ScaleRectTransform(hud.hpFillImage?.GetComponent<RectTransform>(), hpBarScale);
        ScaleText(hud.hpText, hpBarScale);

        // 2. 护盾条放大
        ScaleRectTransform(hud.shieldFillImage?.GetComponent<RectTransform>(), hpBarScale); 
        // 3. 质量文字放大
        ScaleText(hud.massText, massTextScale);

        // 4. 弹药栏放大
        if (hud.ammoHighlights != null)
        {
            foreach (var img in hud.ammoHighlights)
                ScaleRectTransform(img?.GetComponent<RectTransform>(), ammoBarScale);
        }
        if (hud.ammoCounts != null)
        {
            foreach (var txt in hud.ammoCounts)
                ScaleText(txt, ammoBarScale);
        }

        // 5. 移动到屏幕顶部（从左上角锚定）
        MoveToTop(hud.hpFillImage?.GetComponentInParent<RectTransform>());

        // 6. 移动弹药栏到屏幕底部
        if (repositionAmmoBar && hud.ammoHighlights != null && hud.ammoHighlights.Length > 0)
        {
            foreach (var img in hud.ammoHighlights)
                MoveToBottom(img?.GetComponent<RectTransform>());
        }
        if (repositionAmmoBar && hud.ammoCounts != null && hud.ammoCounts.Length > 0)
        {
            foreach (var txt in hud.ammoCounts)
                MoveToBottom(txt?.GetComponent<RectTransform>());
        }

        // 7. Buff 图标放大
        if (hud.buffIcons != null)
        {
            foreach (var img in hud.buffIcons)
                ScaleRectTransform(img?.GetComponent<RectTransform>(), 1.5f);
        }

        Debug.Log("[MobileHudAdapter] 手机端 HUD 布局已应用");
    }

    private static void ScaleRectTransform(RectTransform rt, float scale)
    {
        if (rt == null) return;
        rt.sizeDelta *= scale;
    }

    private static void ScaleText(Text txt, float scale)
    {
        if (txt == null) return;
        txt.fontSize = Mathf.RoundToInt(txt.fontSize * scale);
    }

    private void MoveToTop(RectTransform rt)
    {
        if (rt == null) return;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, -marginTop);
    }

    private void MoveToBottom(RectTransform rt)
    {
        if (rt == null) return;
        // 保留锚点和相对父节点的位置，仅微调
        rt.anchorMin = new Vector2(rt.anchorMin.x, 0f);
        rt.anchorMax = new Vector2(rt.anchorMax.x, 0f);
        rt.pivot = new Vector2(rt.pivot.x, 0f);
    }

    // ===== 公开接口用于手动触发 =====
    [ContextMenu("测试应用手机布局")]
    public void TestApply()
    {
        _applied = false;
        ApplyMobileLayout();
    }
}
