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

    private void Update()
    {
        if (SpacetimeDBNetworkManager.Instance?.Db == null) return;

        sendDirTimer += Time.deltaTime;
        if (sendDirTimer >= sendInterval)
        {
            HandleMovementInput();
            sendDirTimer = 0;
        }
        HandleSplitInput();
    }

    private void HandleMovementInput()
    {
        float moveX = Input.GetAxis("Horizontal");
        float moveY = Input.GetAxis("Vertical");

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
}
