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

    // ===== 动画状态 =====
    private bool isMergeAnim = false;
    private Transform mergeTarget;
    private float mergeAnimTime = 1.5f;
    private float animTimer;

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
        rb.drag = 1.5f;
        rb.angularDrag = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
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

            // 合并动画超时保护：若服务端尚未删除该球，重新启用碰撞体恢复交互
            if (animTimer >= mergeAnimTime)
            {
                isMergeAnim = false;
                rb.isKinematic = false;
                if (col != null)
                {
                    Physics2D.SyncTransforms();
                    col.enabled = true;
                }
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

        // ===== 速度驱动：在 Update 中设速度（而非 FixedUpdate） =====
        // 原因：SpacetimeDBNetworkManager.Update() → FrameTick() → SetTargetPos()
        // 发生在所有 FixedUpdate 之后。若在 FixedUpdate 设速度，读到的是上一帧的 targetPos。
        // 在 Update 中设速，保证每次都用最新的目标位置。
        if (!isMergeAnim && !isSplitAnim && hasReceivedFirstUpdate)
        {
            float smoothTime = isLocalPlayer ? localPosSmoothTime : remotePosSmoothTime;
            rb.velocity = ((Vector2)targetPos - rb.position) / smoothTime;
            ClampToWorldBounds();
        }
    }

    /// <summary>
    /// 钳制到世界边界。使用 MovePosition 而非直接赋值 position，
    /// 让物理引擎知道这是一次有意的位移而非瞬移，避免边界微抖动。
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
}
