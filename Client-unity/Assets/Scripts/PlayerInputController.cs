using UnityEngine;
using SpacetimeDB.Types;
public class PlayerInputController : MonoBehaviour
{
    private float sendDirTimer;
    private readonly float sendInterval = 0.025f; //25ms发一次，≤服务端50ms逻辑帧
    private DbVector2 lastSendDir = new DbVector2(0, 0);

    private void Update()
    {
        if (SpacetimeDBNetworkManager.Instance?.Db == null) return;

        sendDirTimer += Time.deltaTime;
        //到达发送间隔
        if (sendDirTimer >= sendInterval)
        {
            HandleMovementInput();
            sendDirTimer = 0;
        }
        HandleSplitInput(); //分裂按键不用节流
    }
    private void HandleMovementInput()
    {
        float moveX = Input.GetAxis("Horizontal");
        float moveY = Input.GetAxis("Vertical");
        //微小输入归零
        if (Mathf.Abs(moveX) < 0.01f) moveX = 0;
        if (Mathf.Abs(moveY) < 0.01f) moveY = 0;
        DbVector2 curDir = new DbVector2(moveX, moveY);
        //方向没变直接跳过，减少无效网络包
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