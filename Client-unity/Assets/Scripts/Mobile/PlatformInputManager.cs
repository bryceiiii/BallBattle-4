using UnityEngine;

/// <summary>
/// 平台自适应输入管理器。
/// 检测当前平台（PC / Mobile），自动激活对应的输入控制器，
/// 并禁用不需要的那个。挂在场景中的持久化 GameObject 上。
///
/// 部署指南：
/// - 场景中保留原 PlayerInputController（PC用）
/// - 场景中添加本组件，它会在 Start() 时创建/激活 MobileInputController
/// - Mobile 平台：隐藏 PC HUD 布局，显示触屏UI
/// - PC 平台：不创建触屏UI，使用原有输入
/// </summary>
public class PlatformInputManager : MonoBehaviour
{
    public static PlatformInputManager Instance { get; private set; }

    [Header("触屏UI（拖拽预制体，空则自动构建）")]
    public GameObject mobileCanvasPrefab;    // 可选预制体

    [Header("PC 输入控制器引用")]
    public PlayerInputController pcInputController;

    [Header("调试")]
    public bool forceMobileInEditor = false; // Editor 中模拟手机模式

    /// <summary>当前是否在移动模式</summary>
    public bool IsMobileMode { get; private set; }

    private MobileInputController _mobileInput;
    private MobileInputController _bakedMobileInput; // 场景烘焙的 MobileInputController
    private MobileCanvasSetup _canvasSetup;

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }

        // 平台判断提前到 Awake，确保 MobileInputController.Start 能读到正确结果
#if UNITY_ANDROID || UNITY_IOS
        IsMobileMode = true;
#else
        IsMobileMode = forceMobileInEditor;
#endif

        // 缓存场景烘焙的 MobileInputController（可能与本组件在同一 GameObject）
        _bakedMobileInput = GetComponent<MobileInputController>();
    }

    void Start()
    {
        // 自动查找 PC 输入控制器（如果未手动拖拽）
        if (pcInputController == null)
            pcInputController = FindObjectOfType<PlayerInputController>();

        ApplyPlatformMode();
    }

    private void ApplyPlatformMode()
    {
        if (IsMobileMode)
        {
            Debug.Log("[PlatformInputManager] 检测到移动平台，激活触屏输入");
            SetupMobileInput();
            DisablePCInput();
        }
        else
        {
            Debug.Log("[PlatformInputManager] PC平台，使用键鼠输入");
            DisableMobileInput();
        }

        // 同步 HudController 的弹药切换选中
        if (pcInputController != null)
            HudController.Instance?.SelectAmmo(pcInputController.SelectedAmmoType);
    }

    private void SetupMobileInput()
    {
        // 优先使用场景烘焙的 MobileInputController
        if (_bakedMobileInput != null)
        {
            _mobileInput = _bakedMobileInput;
        }
        else if (mobileCanvasPrefab != null)
        {
            // 使用预制体
            var canvasGo = Instantiate(mobileCanvasPrefab, transform);
            _mobileInput = canvasGo.GetComponentInChildren<MobileInputController>();
            if (_mobileInput == null)
                _mobileInput = canvasGo.AddComponent<MobileInputController>();
        }
        else
        {
            // 自动构建
            _canvasSetup = gameObject.AddComponent<MobileCanvasSetup>();
            _canvasSetup.buildOnStart = false; // 阻止 Start() 二次构建
            _mobileInput = _canvasSetup.BuildMobileUI();
        }

        // 强制触屏模式并启用/显示
        if (_mobileInput != null)
        {
            _mobileInput.forceMobileMode = true;
            _mobileInput.enabled = true;
            _mobileInput.ShowMobileUI(true);
        }

        // 恢复 MobileTouchCanvas
        var canvas = GameObject.Find("MobileTouchCanvas");
        if (canvas != null) canvas.SetActive(true);
    }

    private void DisablePCInput()
    {
        if (pcInputController != null)
            pcInputController.enabled = false;
    }

    private void DisableMobileInput()
    {
        // 禁用场景烘焙的 MobileInputController 并隐藏触屏 UI
        if (_bakedMobileInput != null)
        {
            _bakedMobileInput.enabled = false;
            _bakedMobileInput.ShowMobileUI(false);
        }
        else if (_mobileInput != null)
        {
            _mobileInput.enabled = false;
        }

        // 隐藏 MobileTouchCanvas（场景烘焙的触屏画布）
        var canvas = GameObject.Find("MobileTouchCanvas");
        if (canvas != null)
            canvas.SetActive(false);
    }

    // ===== 公开接口 =====
    /// <summary>Editor中切换到手机模式</summary>
    [ContextMenu("切换到手机模式")]
    public void SwitchToMobile()
    {
        IsMobileMode = true;
        ApplyPlatformMode();
    }

    /// <summary>Editor中切换回PC模式</summary>
    [ContextMenu("切换到PC模式")]
    public void SwitchToPC()
    {
        IsMobileMode = false;
        ApplyPlatformMode();
    }

    /// <summary>获取当前激活的输入方向（供外部查询）</summary>
    public Vector2 GetCurrentDirection()
    {
        if (IsMobileMode && _mobileInput != null)
            return _mobileInput.CurrentDirection;
        if (pcInputController != null)
            return pcInputController.CurrentDirection;
        return Vector2.zero;
    }

    /// <summary>获取当前选中弹药类型</summary>
    public int GetSelectedAmmoType()
    {
        if (IsMobileMode && _mobileInput != null)
            return _mobileInput.SelectedAmmoType;
        if (pcInputController != null)
            return pcInputController.SelectedAmmoType;
        return 0;
    }
}
