using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using SpacetimeDB.Types;

/// <summary>
/// 手机端输入控制器。使用虚拟摇杆 + 触屏射击替代 PC 的 WASD + 鼠标控制。
/// 同时保留 PC 输入兼容性（自动检测平台）。
/// </summary>
public class MobileInputController : MonoBehaviour
{
    public static MobileInputController Instance { get; private set; }

    // ===== 虚拟摇杆引用 =====
    [Header("摇杆")]
    public VirtualJoystick moveJoystick;

    // ===== 射击相关 =====
    [Header("射击")]
    public Button shootButton;
    public float shootRepeatInterval = 0.35f; // 按住时连射间隔（略大于服务器冷却，避免无效请求）

    // ===== 功能按钮 =====
    [Header("功能按钮")]
    public Button splitButton;           // 分裂按钮

    // ===== 弹药切换 =====
    [Header("弹药切换")]
    public Button[] ammoButtons;         // [0]=普通弹 [1]=分裂弹
    public Image[] ammoButtonHighlights; // 弹药选中高亮

    // ===== PC 兼容 =====
    [Header("PC兼容")]
    public bool forceMobileMode = true;  // 在 Editor 中也强制使用触屏输入（测试用）

    /// <summary>当前移动方向（归一化）</summary>
    public Vector2 CurrentDirection { get; private set; }

    /// <summary>当前选中弹药类型 0=普通, 1=分裂弹</summary>
    public int SelectedAmmoType { get; private set; } = 0;

    /// <summary>当前瞄准方向（用于射击，默认为移动方向）</summary>
    public Vector2 AimDirection { get; private set; }

    private float _lastShootTime;
    private bool _isShootHeld;
    private bool _isMobile; // 运行时检测

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        // 平台检测
#if UNITY_ANDROID || UNITY_IOS
        _isMobile = true;
#else
        _isMobile = forceMobileMode;
#endif

        // 仅在移动端激活触屏UI
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
        // 移动
        if (moveJoystick != null)
        {
            CurrentDirection = moveJoystick.Direction;
            AimDirection = CurrentDirection; // 瞄准方向 = 移动方向
        }

        // 向服务端发送移动方向
        SendDirection(CurrentDirection);

        // 按住射击按钮时连射
        if (_isShootHeld && Time.time - _lastShootTime >= shootRepeatInterval)
        {
            ShootInAimDirection();
            _lastShootTime = Time.time;
        }
    }

    // ============================================================
    //  PC 输入（保留原生 WASD + 鼠标）
    // ============================================================
    private void HandlePCInput()
    {
        // 移动
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        if (Mathf.Abs(moveX) < 0.01f) moveX = 0;
        if (Mathf.Abs(moveY) < 0.01f) moveY = 0;
        CurrentDirection = new Vector2(moveX, moveY);

        // 发送移动方向
        SendDirection(CurrentDirection);

        // 瞄准 = 鼠标位置
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

        // 射击
        if (Input.GetMouseButtonDown(0))
            Shoot();

        // 分裂
        if (Input.GetKeyDown(KeyCode.Space))
            SplitPlayer();

        // 弹药切换
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
    //  射击逻辑
    // ============================================================
    /// <summary>触屏单发射击（按钮回调）</summary>
    public void OnShootButtonDown()
    {
        if (!_isMobile) return;
        _isShootHeld = true;
        Shoot(); // 立即发射第一发
        _lastShootTime = Time.time;
    }

    public void OnShootButtonUp()
    {
        _isShootHeld = false;
    }

    public void Shoot()
    {
        var conn = SpacetimeDBNetworkManager.Instance?.Db;
        if (conn == null) return;

        Vector3 ballPos = GameManager.GetLocalMainBallPosition();
        if (ballPos == Vector3.zero) return;

        // 计算世界坐标瞄准点
        float targetX = ballPos.x + AimDirection.x * 10f;
        float targetY = ballPos.y + AimDirection.y * 10f;

        conn.Reducers.ShootBullet(targetX, targetY, SelectedAmmoType);
    }

    private void ShootInAimDirection()
    {
        Shoot();
    }

    // ============================================================
    //  裂分
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

        // 高亮选中的弹药按钮
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

        // 同步到 HudController
        HudController.Instance?.SelectAmmo(index);
    }

    // ============================================================
    //  按钮绑定
    // ============================================================
    private void BindButtons()
    {
        if (shootButton != null)
        {
            var downEvt = new EventTrigger.Entry();
            downEvt.eventID = EventTriggerType.PointerDown;
            downEvt.callback.AddListener(delegate(BaseEventData evt) { OnShootButtonDown(); });

            var upEvt = new EventTrigger.Entry();
            upEvt.eventID = EventTriggerType.PointerUp;
            upEvt.callback.AddListener(delegate(BaseEventData evt) { OnShootButtonUp(); });

            var trigger = shootButton.gameObject.AddComponent<EventTrigger>();
            trigger.triggers.Add(downEvt);
            trigger.triggers.Add(upEvt);
        }

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
        if (shootButton != null)
            shootButton.gameObject.SetActive(show);
        if (splitButton != null)
            splitButton.gameObject.SetActive(show);

        if (ammoButtons != null)
        {
            foreach (var btn in ammoButtons)
                if (btn != null) btn.gameObject.SetActive(show);
        }
    }
}
