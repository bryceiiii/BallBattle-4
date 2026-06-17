using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 手机端联机大厅UI适配器。
/// 在 LobbyUIController 基础上放大所有交互元素，确保手指可点击。
/// </summary>
public class MobileLobbyAdapter : MonoBehaviour
{
    public static MobileLobbyAdapter Instance { get; private set; }

    [Header("缩放系数")]
    [Range(1f, 4f)]
    public float buttonScale = 2.5f;       // 按钮整体放大
    [Range(1f, 3f)]
    public float inputFieldScale = 2f;     // 输入框放大
    [Range(1f, 3f)]
    public float textScale = 2f;           // 文字放大

    [Header("间距（像素）")]
    public float buttonSpacing = 30f;
    public float topPadding = 80f;

    private LobbyUIController _lobby;
    private bool _applied;

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        _lobby = GetComponent<LobbyUIController>();

#if UNITY_ANDROID || UNITY_IOS
        ApplyMobileLayout();
#else
        if (MobileInputController.Instance != null)
            ApplyMobileLayout();
#endif
    }

    public void ApplyMobileLayout()
    {
        if (_applied) return;
        _applied = true;

        if (_lobby == null)
        {
            Debug.LogWarning("[MobileLobbyAdapter] 未找到 LobbyUIController");
            return;
        }

        // 放大所有交互元素
        ScaleButton(_lobby.modeLocalBtn, buttonScale);
        ScaleButton(_lobby.modeLANBtn, buttonScale);
        ScaleButton(_lobby.modeCloudBtn, buttonScale);
        ScaleButton(_lobby.connectButton, buttonScale);
        ScaleButton(_lobby.enterGameButton, buttonScale);

        ScaleInputField(_lobby.playerNameInput, inputFieldScale);
        ScaleInputField(_lobby.ipInput, inputFieldScale);
        ScaleInputField(_lobby.portInput, inputFieldScale);

        // 放大文字
        ScaleText(_lobby.modeLocalLabel, textScale);
        ScaleText(_lobby.modeLANLabel, textScale);
        ScaleText(_lobby.modeCloudLabel, textScale);
        ScaleText(_lobby.statusText, textScale);
        ScaleText(_lobby.connectButtonLabel, textScale);
        ScaleText(_lobby.hostAddressHint, textScale);

        // 调整面板间距（VerticalLayoutGroup）
        var panel = _lobby.lobbyPanel;
        if (panel != null)
        {
            var vlg = panel.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) vlg.spacing = buttonSpacing;

            var rt = panel.GetComponent<RectTransform>();
            if (rt != null)
            {
                // 顶部留出安全区域
                rt.offsetMax = new Vector2(rt.offsetMax.x, -topPadding);
            }
        }

        Debug.Log("[MobileLobbyAdapter] 手机端大厅布局已应用");
    }

    private static void ScaleButton(Button btn, float scale)
    {
        if (btn == null) return;
        var rt = btn.GetComponent<RectTransform>();
        if (rt != null)
            rt.sizeDelta = new Vector2(rt.sizeDelta.x * scale, rt.sizeDelta.y * scale);
        var label = btn.GetComponentInChildren<Text>();
        if (label != null)
            label.fontSize = Mathf.RoundToInt(label.fontSize * scale);
    }

    private static void ScaleInputField(InputField input, float scale)
    {
        if (input == null) return;
        var rt = input.GetComponent<RectTransform>();
        if (rt != null)
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, rt.sizeDelta.y * scale);
        if (input.textComponent != null)
            input.textComponent.fontSize = Mathf.RoundToInt(input.textComponent.fontSize * scale);
    }

    private static void ScaleText(Text txt, float scale)
    {
        if (txt == null) return;
        txt.fontSize = Mathf.RoundToInt(txt.fontSize * scale);
    }
}
