using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CircleController : MonoBehaviour
{
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
            // 平滑移动到目标位置
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * 15);
        }
        if (targetScale != 1f)
        {
            // 平滑缩放
            transform.localScale = Vector3.Lerp(transform.localScale, new Vector3(targetScale, targetScale, 1f), Time.deltaTime * 5f);
        }
    }
}
