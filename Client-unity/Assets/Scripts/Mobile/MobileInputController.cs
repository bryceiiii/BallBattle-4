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
    [Range(0.05f, 0.9f)] public float aimShootThreshold = 0.3f; // 右摇杆力度超过此值才开始射击

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
#if UNITY_ANDROID || UNITY_IOS
        _isMobile = true;
#else
        _isMobile = forceMobileMode;
#endif

        if (_isMobile)
            ShowMobileUI(true);
        else
            ShowMobileUI(false);

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
            AimDirection = aimJoystick.Direction.normalized;

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

        Vector3 ballPos = GameManager.GetLocalMainBallPosition();
        if (ballPos == Vector3.zero) return;

        float targetX = ballPos.x + AimDirection.x * 10f;
        float targetY = ballPos.y + AimDirection.y * 10f;

        conn.Reducers.ShootBullet(targetX, targetY, SelectedAmmoType);
    }

    // ============================================================
    //  分裂
    // ============================================================
    public void SplitPlayer()
    {
        SpacetimeDBNetworkManager.Instance?.Db?.Reducers.SplitPlayer();
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
