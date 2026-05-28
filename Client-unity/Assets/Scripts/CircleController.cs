using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CircleController : MonoBehaviour
{
    // Start is called before the first frame update
    public Text nameText;
    public bool isLocalPlayer = false;
    private Vector3 targetPos = Vector3.zero;
    private float targetScale = 1f;
    void Start()
    {
        if (isLocalPlayer)
        {
            nameText.color = Color.green; // 本地玩家名字显示为绿色
        }
    }
    public void SetTargetPos(Vector3 newPos)
    {
        targetPos = newPos;
    }
    public void SetTargetScale(float newMass)
    {
        targetScale = PrefabsManager.Instance.MassToDiameter(newMass);
    }
    public void UpdateName(string name)
    {

        if (nameText != null)
        {
            nameText.text = name;
        }
    }
    public void Update()
    {
        if (targetPos != Vector3.zero)
        {
            // 如果不是本地玩家，平滑移动到目标位置
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * 15);
        }
        if(targetScale != 1f)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, new Vector3(targetScale, targetScale, 1f), Time.deltaTime * 5f);
        }
        CircleMove();
    }

    private void CircleMove()
    {
        if(isLocalPlayer)
        {
            Debug.Log("移动了");
            float moveX = Input.GetAxis("Horizontal");
            float moveY = Input.GetAxis("Vertical");
            
            // 将输入的移动方向发送给服务器
            SpacetimeDBNetworkManager.Instance?.Db.Reducers.UpdatePlayerDir(new SpacetimeDB.Types.DbVector2(moveX, moveY));    
        }
    }
   
}
