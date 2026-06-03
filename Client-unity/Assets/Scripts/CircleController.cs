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
    //新增缓存变量
    private Vector3 posVelocity = Vector3.zero;
    private float scaleVelocity = 0f;
    public float posSmoothTime = 0.08f; //越小越快，0.06~0.1可调
    public float scaleSmoothTime = 0.1f;
    //合并动画相关
    private bool isMergeAnim = false;
    private Transform mergeTarget;
    private float mergeAnimTime = 1.5f; //和服务端100ms销毁对齐
    private float animTimer;
    //碰撞体组件缓存
    private CircleCollider2D col;
    void Start()
    {
        col = GetComponent<CircleCollider2D>();
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
    //开始融合动画：向主球靠拢+缩小至0
    public void StartMergeAnim(Transform target)
    {
        isMergeAnim = true;
        mergeTarget = target;
        animTimer = 0;
        // 关闭碰撞，不再卡住、互相阻挡
        if (col != null) col.enabled = false;
    }
    public void Update()
    {
        //位置平滑阻尼
        if (targetPos != Vector3.zero)
        {
            transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref posVelocity, posSmoothTime);
        }
        //缩放平滑阻尼
        if (targetScale != 1f)
        {
            float curScale = transform.localScale.x;
            float newS = Mathf.SmoothDamp(curScale, targetScale, ref scaleVelocity, scaleSmoothTime);
            transform.localScale = new Vector3(newS, newS, 1f);
        }
        if (isMergeAnim)
        {
            animTimer += Time.deltaTime;
            float rate = animTimer / mergeAnimTime;
            //位移飞向主球
            transform.position = Vector3.Lerp(transform.position, mergeTarget.position, rate);
            //球体不断缩小
            float shrinkScale = Mathf.Lerp(transform.localScale.x, 0, rate);
            transform.localScale = Vector3.one * shrinkScale;
        }
    }
}

