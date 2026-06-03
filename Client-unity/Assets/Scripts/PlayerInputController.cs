using UnityEngine;
using SpacetimeDB.Types;

public class PlayerInputController : MonoBehaviour
{
    private void Update()
    {
        // 确保网络已经连接再进行操作
        if (SpacetimeDBNetworkManager.Instance?.Db == null) return;

        HandleMovementInput();
        HandleSplitInput();
    }

    private void HandleMovementInput()
    {
        float moveX = Input.GetAxis("Horizontal");
        float moveY = Input.GetAxis("Vertical");
        if (Mathf.Abs(moveX) < 0.01f && Mathf.Abs(moveY) < 0.01f)
        {
            return;
        }

        // 将输入的移动方向发送给服务器
        SpacetimeDBNetworkManager.Instance.Db.Reducers.UpdatePlayerDir(new DbVector2(moveX, moveY));
    }

    private void HandleSplitInput()
    {
        // 按下空格键触发分裂
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SpacetimeDBNetworkManager.Instance.Db.Reducers.SplitPlayer();
        }
    }
}