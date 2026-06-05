using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SpacetimeDB;

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
    
    //分裂动画相关
    private bool isSplitAnim = false;
    private float splitAnimTime = 0.3f; // 分裂弹出的时间
    private float splitAnimTimer;
    private Vector3 splitStartPos;

    //碰撞体组件缓存
    private CircleCollider2D col;
    // 关联的实体ID，修正碰撞挤压时需要用到，挤压时根据ID找到对应的GameObject进行位置修正
    public int entityId;
    private float syncCheckTimer;
    private const float SYNC_INTERVAL = 0.5f;   //每0.5秒检测一次偏差
    private const float POS_OFFSET_THRESHOLD = 0.12f; //偏差超过0.12才算物理挤压错位

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

    // 开始融合动画：向主球靠拢+缩小至0
    public void StartMergeAnim(Transform target)
    {
        isMergeAnim = true;
        isSplitAnim = false; // 互斥处理
        mergeTarget = target;
        animTimer = 0;
        // 关闭碰撞，不再卡住、互相阻挡
        if (col != null) col.enabled = false;
    }

    // 开始分裂动画：从母球位置发射并弹射到目标位置
    public void StartSplitAnim(Vector3 startPosition, Vector3 initialTargetPos)
    {
        isSplitAnim = true;
        isMergeAnim = false; // 互斥处理
        splitAnimTimer = 0f;
        splitStartPos = startPosition;
        SetTargetPos(initialTargetPos);
        transform.position = startPosition; // 重置小球的生成位置为母球位置
        
        // 动画期间关闭碰撞体，避免刚孵化时的相互干预及卡顿
        if (col != null) col.enabled = false;
    }

    public void Update()
    {
        syncCheckTimer += Time.deltaTime;
        if (syncCheckTimer >= SYNC_INTERVAL)
        {
            syncCheckTimer = 0;
            //当前本地真实坐标 vs 服务端下发权威目标坐标
            float diff = Vector3.Distance(transform.position, targetPos);
            //物理挤压偏移超标，上报服务器修正坐标
            if (diff > POS_OFFSET_THRESHOLD)
            {
                SpacetimeDBNetworkManager.Instance.Db.Reducers.SyncBallPos(
                    entityId,
                    new SpacetimeDB.Types.DbVector2(transform.position.x, transform.position.y)
                );
            }
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
            return; // 如果在融合动画中，不再执行后面的坐标更新干扰
        }

        // 位置控制
        if (isSplitAnim)
        {
            splitAnimTimer += Time.deltaTime;
            float rate = Mathf.Clamp01(splitAnimTimer / splitAnimTime);
            
            // 使用 Ease-Out 缓动函数（快速弹出，然后减速）
            float t = 1f - Mathf.Pow(1f - rate, 3f);
            transform.position = Vector3.Lerp(splitStartPos, targetPos, t);
            
            // 动画结束退回正常平滑阻尼状态
            if (splitAnimTimer >= splitAnimTime)
            {
                isSplitAnim = false;
                // 分裂动画结束，打开碰撞体
                if (col != null) col.enabled = true;
            }
        }
        else if (targetPos != Vector3.zero)
        {
            // 位置平滑阻尼
            transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref posVelocity, posSmoothTime);
        }

        // 缩放平滑阻尼
        if (targetScale != 1f)
        {
            float curScale = transform.localScale.x;
            float newS = Mathf.SmoothDamp(curScale, targetScale, ref scaleVelocity, scaleSmoothTime);
            transform.localScale = new Vector3(newS, newS, 1f);
        }
    }
}

