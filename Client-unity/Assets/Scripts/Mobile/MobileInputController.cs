using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using SpacetimeDB.Types;

/// <summary>
/// 手机端输入控制器。左摇杆控制移动，右摇杆控制瞄准+射击。
/// 同时保留 PC 输入兼容性（自动检测平台）。
/// </summary>
public class MobileInputController : MonoBehaviour
{
    public static MobileInputController Instance { get; private set; }

    // ===== 虚拟摇杆 =====
    [Header("摇杆")]
    public VirtualJoystick moveJoystick;   // 左侧：移动
    public VirtualJoystick aimJoystick;    // 右侧：瞄准 + 射击

    // ===== 射击参数 =====
    [Header("射击")]
    public float shootRepeatInterval = 0.35f;
    [Range(0.05f, 0.9f)] public float aimShootThreshold = 0.18f; // 右摇杆力度超过此值才开始射击（调低让轻推也能开火）

    // ===== 功能按钮 =====
    [Header("功能按钮")]
    public Button splitButton;

    // ===== 弹药切换 =====
    [Header("弹药切换")]
    public Button[] ammoButtons;
    public Image[] ammoButtonHighlights;

    // ===== PC 兼容 =====
    [Header("PC兼容")]
    public bool forceMobileMode = true;

    public Vector2 CurrentDirection { get; private set; }
    public int SelectedAmmoType { get; private set; } = 0;
    public Vector2 AimDirection { get; private set; }

    private float _lastShootTime;
    private bool _isMobile;

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        // 若场景中存在 PlatformInputManager，则由它统一决定 PC/移动模式，避免双输入抢占
        if (PlatformInputManager.Instance != null)
        {
            _isMobile = PlatformInputManager.Instance.IsMobileMode;
            enabled = _isMobile; // PC 模式下禁用自身
            ShowMobileUI(_isMobile);
        }
        else
        {
#if UNITY_ANDROID || UNITY_IOS
            _isMobile = true;
#else
            _isMobile = forceMobileMode;
#endif
            ShowMobileUI(_isMobile);
        }

        BindButtons();
        SelectAmmo(0);
    }

    void Update()
    {
        if (SpacetimeDBNetworkManager.Instance?.Db == null) return;

        if (_isMobile)
            HandleMobileInput();
        else
            HandlePCInput();
    }

    // ============================================================
    //  移动端输入
    // ============================================================
    private void HandleMobileInput()
    {
        // 移动：左摇杆
        if (moveJoystick != null)
        {
            CurrentDirection = moveJoystick.Direction;
            SendDirection(CurrentDirection);
        }

        // 瞄准 + 射击：右摇杆
        if (aimJoystick != null && aimJoystick.IsActive)
        {
            // 用 RawDirection（未平滑）计算瞄准方向，避免移动摇杆那套 SmoothDamp 造成瞄准延迟
            Vector2 raw = aimJoystick.RawDirection;
            if (raw.sqrMagnitude > 0.0001f)
                AimDirection = raw.normalized;
            else
                AimDirection = CurrentDirection;

            // 摇杆推过阈值 → 自动连射
            if (aimJoystick.Magnitude >= aimShootThreshold
                && Time.time - _lastShootTime >= shootRepeatInterval)
            {
                Shoot();
                _lastShootTime = Time.time;
            }
        }
        else
        {
            // 右摇杆松手时：瞄准方向回退到移动方向
            AimDirection = CurrentDirection;
        }
    }

    // ============================================================
    //  PC 输入（保留原生 WASD + 鼠标）
    // ============================================================
    private void HandlePCInput()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        if (Mathf.Abs(moveX) < 0.01f) moveX = 0;
        if (Mathf.Abs(moveY) < 0.01f) moveY = 0;
        CurrentDirection = new Vector2(moveX, moveY);
        SendDirection(CurrentDirection);

        Vector3 ballPos = GameManager.GetLocalMainBallPosition();
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 mouseScreen = Input.mousePosition;
            mouseScreen.z = -cam.transform.position.z;
            Vector3 mouseWorld = cam.ScreenToWorldPoint(mouseScreen);
            mouseWorld.z = 0f;
            AimDirection = new Vector2(mouseWorld.x - ballPos.x, mouseWorld.y - ballPos.y).normalized;
        }

        if (Input.GetMouseButtonDown(0))
            Shoot();

        if (Input.GetKeyDown(KeyCode.Space))
            SplitPlayer();

        for (int i = 0; i <= 1; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                SelectAmmo(i);
        }
    }

    // ============================================================
    //  发送方向（去重）
    // ============================================================
    private DbVector2 _lastSentDir = new DbVector2(0, 0);

    private void SendDirection(Vector2 dir)
    {
        DbVector2 curDir = new DbVector2(dir.x, dir.y);
        if (Mathf.Approximately(curDir.X, _lastSentDir.X) &&
            Mathf.Approximately(curDir.Y, _lastSentDir.Y))
            return;
        _lastSentDir = curDir;
        SpacetimeDBNetworkManager.Instance?.Db?.Reducers.UpdatePlayerDir(curDir);
    }

    // ============================================================
    //  射击
    // ============================================================
    public void Shoot()
    {
        var conn = SpacetimeDBNetworkManager.Instance?.Db;
        if (conn == null) return;

        Vector2 dir = AimDirection;
        if (dir.sqrMagnitude < 0.001f) return;
        dir.Normalize();

        // 开火手感反馈（纯客户端表现）
        GameFeel.Instance?.OnFire(dir);

        // 发送归一化方向向量（基于本地主球），服务端所有球统一朝此方向平行射击
        conn.Reducers.ShootBullet(dir.x, dir.y, SelectedAmmoType);
    }

    // ============================================================
    //  分裂
    // ============================================================
    public void SplitPlayer()
    {
        SpacetimeDBNetworkManager.Instance?.Db?.Reducers.SplitPlayer();
        GameFeel.Instance?.OnSplit();
    }

    // ============================================================
    //  弹药切换
    // ============================================================
    public void SelectAmmo(int index)
    {
        SelectedAmmoType = index;

        if (ammoButtonHighlights != null)
        {
            for (int i = 0; i < ammoButtonHighlights.Length; i++)
            {
                if (ammoButtonHighlights[i] != null)
                    ammoButtonHighlights[i].color = (i == index)
                        ? new Color(1f, 1f, 0.5f, 0.5f)
                        : new Color(0f, 0f, 0f, 0.3f);
            }
        }

        HudController.Instance?.SelectAmmo(index);
    }

    // ============================================================
    //  按钮绑定
    // ============================================================
    private void BindButtons()
    {
        if (splitButton != null)
            splitButton.onClick.AddListener(SplitPlayer);

        if (ammoButtons != null)
        {
            for (int i = 0; i < ammoButtons.Length; i++)
            {
                int idx = i;
                ammoButtons[i].onClick.AddListener(() => SelectAmmo(idx));
            }
        }
    }

    // ============================================================
    //  UI 显隐
    // ============================================================
    public void ShowMobileUI(bool show)
    {
        if (moveJoystick != null)
            moveJoystick.gameObject.SetActive(show);
        if (aimJoystick != null)
            aimJoystick.gameObject.SetActive(show);
        if (splitButton != null)
            splitButton.gameObject.SetActive(show);

        if (ammoButtons != null)
        {
            foreach (var btn in ammoButtons)
                if (btn != null) btn.gameObject.SetActive(show);
        }
    }
}
