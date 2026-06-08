using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SpacetimeDB;

[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class CircleController : MonoBehaviour
{
    public Text nameText;
    public bool isLocalPlayer = false;

    // ===== 服务端权威目标值 =====
    private Vector3 targetPos = Vector3.zero;
    private float targetScale = 1f;
    private bool hasReceivedFirstUpdate = false;

    // ===== 缩放平滑 =====
    private float scaleVelocity = 0f;

    public float remotePosSmoothTime = 0.12f;
    public float localPosSmoothTime = 0.05f;
    public float scaleSmoothTime = 0.1f;

    // ===== 世界边界 =====
    private const float WORLD_MIN = 0f;
    private const float WORLD_MAX = 50f;

    // ===== 合并动画 =====
    private bool isMergeAnim = false;
    private Transform mergeTarget;
    private float mergeAnimTime = 1.5f;
    private float animTimer;

    // ===== 分裂动画 =====
    private bool isSplitAnim = false;
    private float splitAnimTime = 0.3f;
    private float splitAnimTimer;
    private Vector3 splitStartPos;

    // ===== 分裂后沉降期 =====
    private bool isSettling = false;
    private float settleTimer;
    private const float SETTLE_DURATION = 0.8f;

    // ===== 物理组件 =====
    private CircleCollider2D col;
    private Rigidbody2D rb;
    private Vector2 physicsVelocity; // FixedUpdate 中使用的目标速度

    public int entityId;
    public int playerId;

    void Start()
    {
        col = GetComponent<CircleCollider2D>();
        rb = GetComponent<Rigidbody2D>();

        // 配置 Rigidbody2D 以支持 smooth physics-based collision（球球大作战风格）
        rb.gravityScale = 0f;
        rb.drag = 1.5f;               // 较高阻力：碰撞后快速衰减反弹，实现"贴边滑动"
        rb.angularDrag = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.isKinematic = false;       // 动态模式，Unity 物理处理碰撞反弹

        if (isLocalPlayer)
        {
            nameText.color = Color.green;
        }
    }

    public void SetTargetPos(Vector3 newPos)
    {
        targetPos = newPos;
        hasReceivedFirstUpdate = true;
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

    public void StartMergeAnim(Transform target)
    {
        isMergeAnim = true;
        isSplitAnim = false;
        isSettling = false;
        mergeTarget = target;
        animTimer = 0;
        rb.isKinematic = true;          // 动画期间切为运动学，手动控制位置
        if (col != null) col.enabled = false;
    }

    public void StartSplitAnim(Vector3 startPosition, Vector3 initialTargetPos)
    {
        isSplitAnim = true;
        isMergeAnim = false;
        isSettling = false;
        splitAnimTimer = 0f;
        splitStartPos = startPosition;
        targetPos = initialTargetPos;
        hasReceivedFirstUpdate = true;
        rb.isKinematic = true;          // 动画期间切为运动学
        rb.position = startPosition;
        if (col != null) col.enabled = false;
    }

    void Update()
    {
        // ===== 合并动画 =====
        if (isMergeAnim)
        {
            animTimer += Time.deltaTime;
            float rate = animTimer / mergeAnimTime;
            transform.position = Vector3.Lerp(transform.position, mergeTarget.position, rate);
            float shrinkScale = Mathf.Lerp(transform.localScale.x, 0, rate);
            transform.localScale = Vector3.one * shrinkScale;
            return;
        }

        // ===== 分裂动画 =====
        if (isSplitAnim)
        {
            splitAnimTimer += Time.deltaTime;
            float rate = Mathf.Clamp01(splitAnimTimer / splitAnimTime);
            float t = 1f - Mathf.Pow(1f - rate, 3f);
            transform.position = Vector3.Lerp(splitStartPos, targetPos, t);

            if (splitAnimTimer >= splitAnimTime)
            {
                isSplitAnim = false;
                isSettling = true;
                settleTimer = 0f;
                // 进入沉降期：切回动态物理模式，碰撞体仍禁用
                rb.isKinematic = false;
                rb.position = transform.position;
            }
        }
        // ===== 沉降期 =====
        else if (isSettling)
        {
            settleTimer += Time.deltaTime;
            if (settleTimer >= SETTLE_DURATION)
            {
                isSettling = false;
                if (col != null) col.enabled = true; // 沉降结束，启用碰撞体
            }
        }

        // ===== 正常/沉降状态：计算目标速度（在 FixedUpdate 中应用） =====
        if (!isMergeAnim && !isSplitAnim && hasReceivedFirstUpdate)
        {
            ComputeDesiredVelocity();
        }

        // ===== 缩放平滑 =====
        if (targetScale > 0.01f)
        {
            float curScale = transform.localScale.x;
            float newS = Mathf.SmoothDamp(curScale, targetScale, ref scaleVelocity, scaleSmoothTime);
            transform.localScale = new Vector3(newS, newS, 1f);
        }
    }

    /// <summary>
    /// 计算向服务端目标位置的速度向量。
    /// 不使用 SmoothDamp（会直接设置 transform.position，与物理引擎冲突）。
    /// 改为速度驱动：Rigidbody2D 的速度在 FixedUpdate 中生效，
    /// Unity 物理系统自然地处理碰撞反弹，实现无抖动贴边滑动。
    /// </summary>
    private void ComputeDesiredVelocity()
    {
        float smoothTime = isLocalPlayer ? localPosSmoothTime : remotePosSmoothTime;
        physicsVelocity = ((Vector2)targetPos - rb.position) / smoothTime;
    }

    void FixedUpdate()
    {
        if (isMergeAnim || isSplitAnim) return;
        if (!hasReceivedFirstUpdate) return;

        // 应用速度：物理引擎在 FixedUpdate 中处理碰撞/反弹/阻力
        rb.velocity = physicsVelocity;

        // 钳制到世界边界
        ClampToWorldBounds();
    }

    /// <summary>
    /// 钳制到世界边界（考虑球半径）
    /// </summary>
    private void ClampToWorldBounds()
    {
        float radius = transform.localScale.x * 0.5f;
        Vector2 pos = rb.position;

        pos.x = Mathf.Clamp(pos.x, WORLD_MIN + radius, WORLD_MAX - radius);
        pos.y = Mathf.Clamp(pos.y, WORLD_MIN + radius, WORLD_MAX - radius);

        rb.position = pos;
    }
}
