using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 联机大厅UI控制器。
/// 模式选择用3个互斥Button替代Dropdown，避免模板克隆带来的样式问题。
/// </summary>
public class LobbyUIController : MonoBehaviour
{
    public static LobbyUIController Instance { get; private set; }

    [Header("调试开关")]
    [Tooltip("开启=运行时用代码覆盖样式 | 关闭=保留预制体原始样式")]
    public bool useCodeStyles = true;

    [Header("主面板")]
    public GameObject lobbyPanel;

    [Header("玩家名")]
    public InputField playerNameInput;

    [Header("连接模式 — 3个互斥按钮")]
    public Button modeLocalBtn;
    public Button modeLANBtn;
    public Button modeCloudBtn;
    public Text modeLocalLabel;
    public Text modeLANLabel;
    public Text modeCloudLabel;

    [Header("局域网设置（仅LAN模式显示）")]
    public GameObject lanSettingsGroup;
    public InputField ipInput;
    public InputField portInput;

    [Header("状态与按钮")]
    public Text statusText;
    public Button connectButton;
    public Button enterGameButton;
    public Text connectButtonLabel;

    [Header("本机IP提示")]
    public Text hostAddressHint;

    private bool _isConnected = false;
    private int _currentMode = 0; // 0=本机, 1=LAN, 2=云端

    void Awake()
    {
        if (Instance == null) { Instance = this; }
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        if (useCodeStyles) ApplyVisualStyles();
        BindEvents();
        ApplyDefaults();

        SelectMode(0);
    }

    void Update()
    {
        // 兜底轮询：不依赖事件，直接检测连接状态
        var net = SpacetimeDBNetworkManager.Instance;
        if (net != null && _isConnected != net.IsConnected)
        {
            _isConnected = net.IsConnected;
            if (_isConnected)
                OnNetworkConnected();
            else
                OnNetworkConnectFailed("");
        }
    }

    void OnDestroy()
    {
        SpacetimeDBNetworkManager.OnConnected -= OnNetworkConnected;
        SpacetimeDBNetworkManager.OnConnectFailed -= OnNetworkConnectFailed;
    }

    // ============================================================
    //  🎨 视觉样式
    // ============================================================

    private static readonly Color DarkBg   = new Color(0.12f, 0.12f, 0.20f, 1f);
    private static readonly Color LightText = new Color(0.90f, 0.90f, 0.95f, 1f);
    private static readonly Color HintText  = new Color(0.50f, 0.55f, 0.65f, 1f);
    private static readonly Color Blue      = new Color(0.30f, 0.55f, 1f, 1f);
    private static readonly Color Green     = new Color(0.25f, 0.75f, 0.40f, 1f);
    private static readonly Color ActiveMode = new Color(0.25f, 0.45f, 0.85f, 1f);
    private static readonly Color InactiveMode = new Color(0.15f, 0.15f, 0.22f, 1f);
    private static readonly Color White     = Color.white;

    private void ApplyVisualStyles()
    {
        StyleInputField(playerNameInput);
        StyleInputField(ipInput);
        StyleInputField(portInput);

        if (connectButton != null) StyleButton(connectButton, Blue);
        if (enterGameButton != null) StyleButton(enterGameButton, Green);

        if (statusText != null) statusText.color = HintText;
        if (connectButtonLabel != null) connectButtonLabel.color = White;
        if (hostAddressHint != null) hostAddressHint.color = HintText;
    }

    private static void StyleInputField(InputField input)
    {
        if (input == null) return;
        var img = input.GetComponent<Image>();
        if (img != null) img.color = DarkBg;
        if (input.textComponent != null) input.textComponent.color = LightText;
        if (input.placeholder != null)
        {
            var ph = input.placeholder.GetComponent<Text>();
            if (ph != null) ph.color = HintText;
        }
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
        if (label != null) label.color = White;
    }

    // ============================================================
    //  🔘 模式选择（3个互斥按钮）
    // ============================================================

    private void SelectMode(int mode)
    {
        _currentMode = mode;

        // 给3个按钮应用选中/未选中样式
        SetModeButtonStyle(modeLocalBtn,  modeLocalLabel,  mode == 0);
        SetModeButtonStyle(modeLANBtn,    modeLANLabel,    mode == 1);
        SetModeButtonStyle(modeCloudBtn,  modeCloudLabel,  mode == 2);

        // 显示/隐藏局域网设置
        if (lanSettingsGroup != null)
            lanSettingsGroup.SetActive(mode == 1);

        UpdateConnectButtonLabel(mode);
        if (statusText != null) statusText.text = "";

        if (mode == 0) UpdateHostAddressHint();
    }

    private void SetModeButtonStyle(Button btn, Text label, bool active)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = active ? ActiveMode : InactiveMode;
        if (label != null)
        {
            label.color = active ? White : HintText;
            label.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
        }
    }

    private void UpdateConnectButtonLabel(int mode)
    {
        if (connectButtonLabel == null) return;
        switch (mode)
        {
            case 0: connectButtonLabel.text = "启动本机服务器"; break;
            case 1: connectButtonLabel.text = "连接到主机"; break;
            case 2: connectButtonLabel.text = "连接到云端"; break;
        }
    }

    // ============================================================
    //  🔗 事件
    // ============================================================

    private void BindEvents()
    {
        if (modeLocalBtn != null)  modeLocalBtn.onClick.AddListener(() => SelectMode(0));
        if (modeLANBtn != null)    modeLANBtn.onClick.AddListener(() => SelectMode(1));
        if (modeCloudBtn != null)  modeCloudBtn.onClick.AddListener(() => SelectMode(2));

        if (connectButton != null) connectButton.onClick.AddListener(OnConnectClicked);
        if (enterGameButton != null) enterGameButton.onClick.AddListener(OnEnterGameClicked);

        SpacetimeDBNetworkManager.OnConnected += OnNetworkConnected;
        SpacetimeDBNetworkManager.OnConnectFailed += OnNetworkConnectFailed;
    }

    private void ApplyDefaults()
    {
        if (ipInput != null) ipInput.text = "192.168.1.100";
        if (portInput != null) portInput.text = "3000";
        if (enterGameButton != null) enterGameButton.interactable = false;
        UpdateHostAddressHint();
    }

    // ============================================================
    //  🖱️ 回调
    // ============================================================

    private void OnConnectClicked()
    {
        var net = SpacetimeDBNetworkManager.Instance;
        if (net == null)
        {
            if (statusText != null) statusText.text = "网络管理器未初始化！";
            return;
        }

        if (statusText != null) statusText.text = "正在连接...";
        if (connectButton != null) connectButton.interactable = false;

        switch (_currentMode)
        {
            case 0: net.ConnectLocal(); break;
            case 1:
                string ip = ipInput != null ? ipInput.text.Trim() : "127.0.0.1";
                if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";
                int port = 3000;
                if (portInput != null) int.TryParse(portInput.text, out port);
                if (port <= 0) port = 3000;
                net.ConnectToLAN(ip, port);
                break;
            case 2: net.ConnectCloud(); break;
        }
    }

    private void OnEnterGameClicked()
    {
        if (!_isConnected) return;
        var conn = SpacetimeDBNetworkManager.Instance?.Db;
        string playerName = playerNameInput != null ? playerNameInput.text.Trim() : "Player";
        if (string.IsNullOrEmpty(playerName)) playerName = "Player" + Random.Range(100, 999);
        if (conn != null)
        {
            if (lobbyPanel != null) lobbyPanel.SetActive(false);
            if (GameManager.Instance != null && GameManager.Instance.canvasGo != null)
                GameManager.Instance.canvasGo.SetActive(false);
            conn.Reducers.EnterGame(playerName);
        }
    }

    // ============================================================
    //  🌐 网络回调
    // ============================================================

    private void OnNetworkConnected()
    {
        _isConnected = true;
        if (statusText != null) { statusText.text = "已连接到服务器！"; statusText.color = Green; }
        if (enterGameButton != null) enterGameButton.interactable = true;
        if (connectButton != null) connectButton.interactable = false;
        UpdateHostAddressHint();
    }

    private void OnNetworkConnectFailed(string error)
    {
        _isConnected = false;
        if (statusText != null) { statusText.text = $"连接失败: {error}"; statusText.color = new Color(1f, 0.35f, 0.35f, 1f); }
        if (connectButton != null) connectButton.interactable = true;
        if (enterGameButton != null) enterGameButton.interactable = false;
    }

    // ============================================================
    //  🛠️
    // ============================================================

    private void UpdateHostAddressHint()
    {
        if (hostAddressHint == null) return;
        string ip = GetLocalIPAddress();
        hostAddressHint.text = !string.IsNullOrEmpty(ip)
            ? $"你的局域网IP: {ip}:3000\n把这个地址发给朋友，让他们填入上面的IP栏"
            : "无法获取局域网IP，请检查网络连接";
    }

    private static string GetLocalIPAddress()
    {
        try
        {
            foreach (var ip in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return ip.ToString();
        }
        catch { }
        return "127.0.0.1";
    }
}
