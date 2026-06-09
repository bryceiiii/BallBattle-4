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

    // ===== 位置平滑 =====
    private Vector2 posVelocity = Vector2.zero;

    public float remotePosSmoothTime = 0.15f;
    public float localPosSmoothTime = 0.08f;
    public float scaleSmoothTime = 0.1f;

    // ===== 世界边界 =====
    private const float WORLD_MIN = 0f;
    private const float WORLD_MAX = 50f;

    // ===== 动画状态 =====
    private bool isMergeAnim = false;
    private Transform mergeTarget;
    private float mergeAnimTime = 0.8f;
    private float animTimer;
    private bool mergeAnimFinished = false; // 标记动画是否已播完（只触发一次 FinishMerge）

    private bool isSplitAnim = false;
    private float splitAnimTime = 0.3f;
    private float splitAnimTimer;
    private Vector3 splitStartPos;

    // ===== 物理组件 =====
    private CircleCollider2D col;
    private Rigidbody2D rb;

    public int entityId;
    public int playerId;

    void Awake()
    {
        // 确保 Rigidbody2D 存在：预制体可能尚未添加 (RequireComponent 仅在 Editor 添加脚本时生效)
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
        }

        // 配置 Rigidbody2D：球球大作战风格物理碰撞
        rb.gravityScale = 0f;
        rb.drag = 0.3f;
        rb.angularDrag = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete; // 球速低无需连续检测
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.isKinematic = false;

        col = GetComponent<CircleCollider2D>();
    }

    void Start()
    {
        // 此时 isLocalPlayer 可能尚未被 GameManager 设置（Instantiate 在赋值前执行 Start）
        // 由 GameManager 在设置 isLocalPlayer 后调用 ApplyLocalPlayerVisual() 补充
        ApplyLocalPlayerVisual();
    }

    /// <summary>
    /// 应用本地玩家视觉（名字绿色）。
    /// 由 GameManager 在设置 isLocalPlayer 后调用，
    /// 因为 Instantiate 时 Start() 先于 isLocalPlayer 赋值执行。
    /// </summary>
    public void ApplyLocalPlayerVisual()
    {
        if (isLocalPlayer && nameText != null)
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
        mergeTarget = target;
        animTimer = 0;
        mergeAnimFinished = false;
        rb.isKinematic = true;
        if (col != null) col.enabled = false;
    }

    public void StartSplitAnim(Vector3 startPosition, Vector3 initialTargetPos)
    {
        isSplitAnim = true;
        isMergeAnim = false;
        splitAnimTimer = 0f;
        splitStartPos = startPosition;
        targetPos = initialTargetPos;
        hasReceivedFirstUpdate = true;
        rb.isKinematic = true;
        rb.position = startPosition;
        if (col != null) col.enabled = false; // 动画期间禁用，避免从母球位置弹出时物理挤压
    }

    void Update()
    {
        // ===== 合并动画 =====
        if (isMergeAnim)
        {
            animTimer += Time.deltaTime;
            float rate = Mathf.Clamp01(animTimer / mergeAnimTime);
            transform.position = Vector3.Lerp(transform.position, mergeTarget.position, rate);
            float shrinkScale = Mathf.Lerp(transform.localScale.x, 0, rate);
            transform.localScale = Vector3.one * shrinkScale;

            if (animTimer >= mergeAnimTime && !mergeAnimFinished)
            {
                mergeAnimFinished = true;
                isMergeAnim = false;

                // 通知服务端：动画完成，可以真正删除此球
                var conn = SpacetimeDBNetworkManager.Instance?.Db;
                if (conn != null && entityId != 0)
                {
                    conn.Reducers.FinishMerge(entityId);
                }

                // 动画完成后的超时保护：如果服务端因某种原因没删除（如网络延迟），
                // 超过 1.5 秒后恢复物理状态
                StartCoroutine(MergeTimeoutRecovery());
            }
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
                // 分裂完成：切回动态物理 + 启用碰撞体
                rb.isKinematic = false;
                rb.position = transform.position;
                if (col != null) col.enabled = true;
            }
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
    /// 物理步驱动位置：用 MovePosition + SmoothDamp 代替 rb.velocity。
    /// 
    /// 为什么不用 rb.velocity：
    ///   Update() 设 velocity → FixedUpdate() 物理碰撞修改 velocity → 下一帧 Update() 又覆盖
    ///   → 两个系统抢改同一个变量 → 振荡抖动
    /// 
    /// MovePosition 的优势：
    ///   告诉物理引擎"我要移到这里"，引擎在移动过程中自然处理碰撞推挤，
    ///   碰撞后的位置偏移被 drag 衰减，下个 SmoothDamp 步自然收敛。
    /// </summary>
    void FixedUpdate()
    {
        if (isMergeAnim || isSplitAnim) return;
        if (!hasReceivedFirstUpdate) return;

        float smoothTime = isLocalPlayer ? localPosSmoothTime : remotePosSmoothTime;
        Vector2 desiredPos = Vector2.SmoothDamp(rb.position, (Vector2)targetPos, ref posVelocity, smoothTime);
        rb.MovePosition(desiredPos);
        ClampToWorldBounds();
    }

    /// <summary>
    /// 钳制到世界边界。使用 MovePosition 而非直接赋值 position，
    /// </summary>
    private void ClampToWorldBounds()
    {
        float radius = transform.localScale.x * 0.5f;
        Vector2 pos = rb.position;

        float clampedX = Mathf.Clamp(pos.x, WORLD_MIN + radius, WORLD_MAX - radius);
        float clampedY = Mathf.Clamp(pos.y, WORLD_MIN + radius, WORLD_MAX - radius);

        if (clampedX != pos.x || clampedY != pos.y)
        {
            rb.MovePosition(new Vector2(clampedX, clampedY));
        }
    }

    /// <summary>
    /// 合并动画超时恢复：若服务端因网络延迟未及时删除此球，
    /// 超过 1.5 秒后恢复物理状态，避免球"卡死"在不可交互状态。
    /// 正常情况下服务端会在此之前通过 OnCircleDeleted 销毁此 GameObject。
    /// </summary>
    private IEnumerator MergeTimeoutRecovery()
    {
        yield return new WaitForSeconds(1.5f);
        // 如果此 GameObject 还没被服务端删除（说明 FinishMerge 未生效）
        if (gameObject != null && isMergeAnim == false)
        {
            rb.isKinematic = false;
            if (col != null)
            {
                Physics2D.SyncTransforms();
                col.enabled = true;
            }
        }
    }
}
