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

    // ===== HP 条调优参数（可在 Unity Inspector 中调整） =====
    [Header("HP 条位置调优")]
    public float hpBarTopGap = 0.08f;             // 球顶到 HP 条底部的间距（世界单位）
    public float hpBarZDepth = -0.01f;            // Z 轴偏移（-值=在球前方）
    public float hpBarWidthRatio = 1.0f;          // HP 条宽度占球直径的比例（1.0=和球一样宽）
    public float hpBarMinWidth = 0.3f;            // HP 条最小宽度
    [Header("HP 条高度（随球增大而变高）")]
    public float hpBarBaseHeight = 0.12f;          // 基础高度（世界单位）
    public float hpBarHeightGrowth = 0.02f;        // 每单位直径增加的高度（0=不变）

    /// <summary>
    /// 根据球的当前直径计算 HP 条高度。
    /// 小球时 ≈ hpBarBaseHeight，大球时缓慢增长。
    /// </summary>
    public float GetHpBarHeight(float diameter)
    {
        // 参考直径 = 出生球直径 (mass=5 → sqrt(5)/2 ≈ 1.12)
        const float REF_DIAM = 1.12f;
        float extra = Mathf.Max(0f, diameter - REF_DIAM) * hpBarHeightGrowth;
        return hpBarBaseHeight + extra;
    }

    // ===== HP 相关（Inspector 可观察） =====
    [Header("HP 状态（运行时观察）")]
    public float debugHp = 0f;          // 当前 HP（Inspector 只读观察）
    public float debugMaxHp = 0f;       // 最大 HP
    public float debugHpRatio = 0f;     // 血量百分比 0~1
    private float hp = 100f;
    private float maxHp = 100f;
    private Image hpFillImage;           // HP 填充条
    private RectTransform hpCanvasRect;  // Canvas 的 RectTransform（控制位置+尺寸+显隐）
    private bool hpBarCreated = false;

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
    private Vector3 splitAnimEndPos; // 分裂动画的固定终点，不受服务端位置更新干扰

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

    // ===== HP 条 =====
    /// <summary>
    /// 创建 HP 条（World Space Canvas，球顶上方）。
    /// 在首次调用 SetHp 时懒创建，避免 Start 时预制体还未完全实例化。
    /// </summary>
    private void CreateHpBar()
    {
        if (hpBarCreated) return;
        hpBarCreated = true;

        // 创建 Canvas 作为子物体
        var canvasGo = new GameObject("HpCanvas");
        canvasGo.transform.SetParent(transform, false);
        // 取消继承父级缩放，Canvas 尺寸由 sizeDelta 独立控制
        float initDiam = Mathf.Max(transform.localScale.x, 0.001f);
        float initH = GetHpBarHeight(initDiam);
        float initOffset = 0.5f + (initH * 0.5f + hpBarTopGap) / initDiam;
        canvasGo.transform.localPosition = new Vector3(0f, initOffset, hpBarZDepth);
        canvasGo.transform.localScale = Vector3.one;
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        // 不添加 GraphicRaycaster（不需要交互）

        // Canvas 的 RectTransform — 局部尺寸 = hpBarWidthRatio（乘父级缩放后=球直径）
        var canvasRect = canvasGo.GetComponent<RectTransform>();
        hpCanvasRect = canvasRect;
        canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
        canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
        canvasRect.pivot = new Vector2(0.5f, 0.5f);
        float initialLocalWidth = Mathf.Max(hpBarWidthRatio, hpBarMinWidth / initDiam);
        canvasRect.sizeDelta = new Vector2(initialLocalWidth, initH / initDiam);

        // HP 条背景（深灰色）— 填满 Canvas
        var bgGo = new GameObject("HpBarBg");
        bgGo.transform.SetParent(canvasGo.transform, false);
        var bgImage = bgGo.AddComponent<Image>();
        bgImage.color = new Color(0.15f, 0.15f, 0.15f, 0.85f);
        var bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;
        bgRect.anchoredPosition = Vector2.zero;

        // BG 添加 Mask，用来裁剪内部的 Fill 超出部分
        bgGo.AddComponent<Mask>().showMaskGraphic = true;

        // HP 填充条（用宽度控制填充比例，比 Image.Filled 更可靠）
        var fillGo = new GameObject("HpBarFill");
        fillGo.transform.SetParent(bgGo.transform, false);
        hpFillImage = fillGo.AddComponent<Image>();
        hpFillImage.color = Color.green;
        hpFillImage.type = Image.Type.Simple; // 普通模式，不依赖 Filled
        var fillRect = hpFillImage.GetComponent<RectTransform>();
        // 锚定在左侧（0,0）到（0,1），pivot 在左侧
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.sizeDelta = new Vector2(100f, 0f); // 初始宽100%，SetHp 会调整

        // 创建时全隐藏，SetHp 会按血量决定是否显示
        canvasGo.SetActive(false);
    }

    /// <summary>
    /// 由 GameManager 每帧 OnEntityUpdated 调用，同步服务端 HP 数据。
    /// </summary>
    public void SetHp(float currentHp, float maxHpValue)
    {
        hp = currentHp;
        maxHp = maxHpValue;

        // 更新调试观察字段
        debugHp = currentHp;
        debugMaxHp = maxHpValue;

        if (!hpBarCreated) CreateHpBar();

        var fillRect = hpFillImage != null ? hpFillImage.GetComponent<RectTransform>() : null;
        if (fillRect == null || hp <= 0 || maxHp <= 0) return;

        float ratio = Mathf.Clamp01(hp / maxHp);
        debugHpRatio = ratio;

        // 用宽度控制填充比例（比 Image.Filled.fillAmount 更可靠）
        // BG 的宽度由 Canvas sizeDelta 决定，这里取 BG 宽度 = Canvas 宽度
        float barFullWidth = hpCanvasRect.sizeDelta.x;
        float fillWidth = barFullWidth * ratio;
        fillRect.sizeDelta = new Vector2(fillWidth, 0f);

        // 颜色：绿(>60%) → 黄(30-60%) → 红(<30%)
        if (ratio > 0.6f)
            hpFillImage.color = Color.green;
        else if (ratio > 0.3f)
            hpFillImage.color = Color.yellow;
        else
            hpFillImage.color = Color.red;

        // 控制 Canvas 级显隐：满血隐藏，受伤显示
        if (hpCanvasRect != null)
        {
            hpCanvasRect.gameObject.SetActive(ratio < 1f);
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
        splitAnimEndPos = initialTargetPos; // 锁定终点，动画期间不随服务端位置更新而变
        targetPos = initialTargetPos;
        hasReceivedFirstUpdate = true;
        rb.isKinematic = true;
        rb.position = startPosition;
        if (col != null) col.enabled = false;
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
            float t = 1f - Mathf.Pow(1f - rate, 3f); // ease-out cubic
            Vector2 pos = Vector2.Lerp((Vector2)splitStartPos, (Vector2)splitAnimEndPos, t);
            rb.position = pos; // 用 rb.position 保持物理同步，避免动画结束时 snap

            if (splitAnimTimer >= splitAnimTime)
            {
                isSplitAnim = false;
                rb.isKinematic = false;
                rb.position = (Vector2)splitAnimEndPos; // 精确落位
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

        // ===== HP 条位置跟随（直接控制 Canvas 的 localPosition） =====
        if (hpCanvasRect != null)
        {
            float diameter = transform.localScale.x;
            if (diameter < 0.001f) diameter = 0.001f;
            float curH = GetHpBarHeight(diameter);

            // === 世界单位 → 父级局部空间换算 ===
            // Canvas 是球的子物体，ballScale 倍率会应用于 Canvas 的 localPosition/sizeDelta
            // 所以需要把世界单位的目标值除以 ballScale 得到局部值

            // 位置：球顶在局部 Y = 0.5（因为 ballScale.x = 直径）
            // 加上条高/间距的局部偏移
            float localOffset = 0.5f + (curH * 0.5f + hpBarTopGap) / diameter;
            hpCanvasRect.localPosition = new Vector3(0f, localOffset, hpBarZDepth);

            // 取消继承父球缩放，仅通过 sizeDelta 传递父级缩放
            hpCanvasRect.localScale = Vector3.one;

            // 宽度（局部）：设为 hpBarWidthRatio，乘父级缩放后 = 球直径 × 比例
            float localWidth = hpBarWidthRatio;
            // 最小宽度保护也要换算到局部空间
            float minLocalWidth = hpBarMinWidth / diameter;
            localWidth = Mathf.Max(localWidth, minLocalWidth);
            hpCanvasRect.sizeDelta = new Vector2(localWidth, curH / diameter);

            // Canvas 宽度变化时同步更新 Fill 宽度（保持血量比例）
            if (hpFillImage != null)
            {
                float ratio = Mathf.Clamp01(hp / maxHp);
                float fillLocalWidth = localWidth * ratio;
                hpFillImage.GetComponent<RectTransform>().sizeDelta = new Vector2(fillLocalWidth, 0f);
            }
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
