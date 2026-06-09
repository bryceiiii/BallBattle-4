using UnityEngine;
using SpacetimeDB.Types;

public class PlayerInputController : MonoBehaviour
{
    private float sendDirTimer;
    private readonly float sendInterval = 0.025f; // 25ms发一次，≤服务端50ms逻辑帧
    private DbVector2 lastSendDir = new DbVector2(0, 0);

    /// <summary>
    /// 当前移动方向（公开属性，供其他组件读取用于死推算等）
    /// </summary>
    public Vector2 CurrentDirection { get; private set; }

    private     void Update()
    {
        if (SpacetimeDBNetworkManager.Instance?.Db == null) return;

        sendDirTimer += Time.deltaTime;
        if (sendDirTimer >= sendInterval)
        {
            HandleMovementInput();
            sendDirTimer = 0;
        }
        HandleSplitInput();
        HandleShootInput();
    }

    private void HandleMovementInput()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");

        // 微小输入归零，防止手柄漂移
        if (Mathf.Abs(moveX) < 0.01f) moveX = 0;
        if (Mathf.Abs(moveY) < 0.01f) moveY = 0;

        CurrentDirection = new Vector2(moveX, moveY);
        DbVector2 curDir = new DbVector2(moveX, moveY);

        // 方向没变直接跳过，减少无效网络包
        if (curDir.X == lastSendDir.X && curDir.Y == lastSendDir.Y) return;

        SpacetimeDBNetworkManager.Instance.Db.Reducers.UpdatePlayerDir(curDir);
        lastSendDir = curDir;
    }

    private void HandleSplitInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SpacetimeDBNetworkManager.Instance.Db.Reducers.SplitPlayer();
        }
    }

    private void HandleShootInput()
    {
        if (!Input.GetMouseButtonDown(0)) return; // 鼠标左键

        var conn = SpacetimeDBNetworkManager.Instance?.Db;
        if (conn == null) return;

        // 计算从本地玩家主球到鼠标的方向
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

        // 零方向不发射
        if (Mathf.Abs(dirX) < 0.001f && Mathf.Abs(dirY) < 0.001f) return;

        conn.Reducers.ShootBullet(dirX, dirY);
    }
}
