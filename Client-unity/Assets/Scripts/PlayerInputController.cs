using UnityEngine;
using SpacetimeDB.Types;

public class PlayerInputController : MonoBehaviour
{
    private DbVector2 lastSendDir = new DbVector2(0, 0);
    private float _sendDirCooldown; // 防止同一帧内重复发送
    public static PlayerInputController Instance { get; private set; }
    public Vector2 CurrentDirection { get; private set; }
    public int SelectedAmmoType { get; private set; } = 0; // 0=普通, 1=分裂弹

    // ===== RTT 测量 =====
    /// <summary>最近一次方向发送的时间戳（用于 RTT 估算）</summary>
    public static float LastDirSendTime { get; private set; } = 0f;

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }
    private void Update()
    {
        if (SpacetimeDBNetworkManager.Instance?.Db == null) return;

        HandleMovementInput();
        HandleSplitInput();
        HandleShootInput();
        HandleAmmoSwitch();
    }

    private void HandleMovementInput()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        if (Mathf.Abs(moveX) < 0.01f) moveX = 0;
        if (Mathf.Abs(moveY) < 0.01f) moveY = 0;

        Vector2 rawDir = new Vector2(moveX, moveY);
        CurrentDirection = rawDir;

        DbVector2 curDir = new DbVector2(moveX, moveY);
        if (curDir.X == lastSendDir.X && curDir.Y == lastSendDir.Y) return;

        SpacetimeDBNetworkManager.Instance.Db.Reducers.UpdatePlayerDir(curDir);
        lastSendDir = curDir;
        LastDirSendTime = Time.time;  // 记录发送时间，用于 RTT 估算
    }

    private void HandleSplitInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            SpacetimeDBNetworkManager.Instance.Db.Reducers.SplitPlayer();
    }

    /// <summary>数字键 1-2 切换弹种</summary>
    private void HandleAmmoSwitch()
    {
        for (int i = 1; i <= 2; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i))
            {
                SelectedAmmoType = i - 1; // 0=普通, 1=分裂弹
                HudController.Instance?.SelectAmmo(i - 1);
            }
        }
    }

    private void HandleShootInput()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        var conn = SpacetimeDBNetworkManager.Instance?.Db;
        if (conn == null) return;

        Vector3 ballPos = GameManager.GetLocalMainBallPosition();
        if (ballPos == Vector3.zero) return;

        var cam = Camera.main;
        if (cam == null) return;

        Vector3 mouseScreen = Input.mousePosition;
        mouseScreen.z = -cam.transform.position.z;
        Vector3 mouseWorld = cam.ScreenToWorldPoint(mouseScreen);
        mouseWorld.z = 0f;

        float dirX = mouseWorld.x - ballPos.x;
        float dirY = mouseWorld.y - ballPos.y;
        if (Mathf.Abs(dirX) < 0.001f && Mathf.Abs(dirY) < 0.001f) return;

        // 传鼠标世界坐标，服务端为每球独立计算方向
        conn.Reducers.ShootBullet(mouseWorld.x, mouseWorld.y, SelectedAmmoType);
    }
}
