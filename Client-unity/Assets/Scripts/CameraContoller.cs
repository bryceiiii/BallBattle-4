using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraContoller : MonoBehaviour
{
    public static CameraContoller Instance { get; private set; }

    // 使用列表来存储多个目标
    private List<Transform> followTargets = new List<Transform>();

    [Header("Movement")]
    public float smoothSpeed = 3.5f;

    [Header("Zoom")]
    public float sizeSmoothSpeed = 4f; // 缩放平滑（分裂瞬间球数突变时更跟手，时间常数≈0.25s）
    public float minSize = 3f; // 相机最小的正交视野大小（小球阶段视野更聚焦，出生球看得清）
    public float padding = 2f; // 屏幕边缘留白的距离

    [Header("Diameter-based view")]
    [Tooltip("基于最大球直径的额外视野比例（0.22 表示在半径基础上再增加 22% 的''呼吸''空间）")]
    public float diameterMargin = 0.22f; // 大球额外视野比例（0.4→0.22，大球在屏幕里更紧凑）
    [Tooltip("视野上限（防止极端情况下放得过远），<= 0 则不生效")]
    public float maxSize = 35f; // 视野上限（防止分裂多球时拉得太远）

    private Camera cam;

    // 世界尺寸（与服务端 Lib.cs 的 WORLD_SIZE 保持一致）
    private const float WORLD_SIZE = 100f;

    // ===== 屏幕震动（游戏手感） =====
    private Vector3 _logicalPos;         // 相机平滑后的逻辑位置（不含震动，避免震动被平滑"吃掉"）
    private bool _logicalInit;
    private float _shakeTimer;
    private float _shakeDuration;
    private float _shakeIntensity;
    private Vector2 _shakeOffset;

    /// <summary>屏幕震动。intensity=振幅(世界单位)，duration=时长(秒)。开火用 0.05~0.1，命中用 0.2~0.3。</summary>
    public void Shake(float intensity, float duration)
    {
        if (intensity <= 0f) return;
        // 已有更强震动时不覆盖，避免弱震动把强震动打断
        if (_shakeTimer > 0f && intensity < _shakeIntensity) return;
        _shakeIntensity = intensity;
        _shakeDuration = Mathf.Max(0.01f, duration);
        _shakeTimer = _shakeDuration;
    }

    private Vector2 UpdateShake()
    {
        if (_shakeTimer > 0f)
        {
            _shakeTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(_shakeTimer / _shakeDuration);
            _shakeOffset = Random.insideUnitCircle * (_shakeIntensity * t);
        }
        else
        {
            _shakeOffset = Vector2.zero;
        }
        return _shakeOffset;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        // 获取身上的 Camera 组件
        cam = GetComponent<Camera>();
    }

    // 添加新的跟随目标
    public void AddFollowTarget(Transform target)
    {
        if (!followTargets.Contains(target))
        {
            followTargets.Add(target);
        }
    }

    void LateUpdate()
    {
        // 自动清理已经被销毁的物体引用
        followTargets.RemoveAll(t => t == null);

        if (followTargets.Count == 0) return;

        // --- 1. 相机平滑移动到所有球的质量加权中心 ---
        // 大球主导视野，远处小球只轻微拉动，避免一个远距小球把中心硬拉偏。
        // 权重 = 质量 ∝ 直径?（MassToDiameter = sqrt(mass)/2 → mass = 4×diameter?）
        Vector2 center = Vector2.zero;
        float totalWeight = 0f;
        foreach (var target in followTargets)
        {
            float diameter = target.localScale.x;
            float weight = Mathf.Max(0.001f, diameter * diameter);
            center += (Vector2)target.position * weight;
            totalWeight += weight;
        }
        if (totalWeight > 0f) center /= totalWeight;

        // 边界钳制：球贴边时相机中心不越界，避免显示地图外空白
        // 视野比地图还大时（halfW/halfH 超过地图半宽/半高），相机锁定在地图中心
        float halfH = cam != null ? cam.orthographicSize : minSize;
        float halfW = halfH * (cam != null ? cam.aspect : 1f);
        float clampX = halfW >= WORLD_SIZE * 0.5f ? WORLD_SIZE * 0.5f : Mathf.Clamp(center.x, halfW, WORLD_SIZE - halfW);
        float clampY = halfH >= WORLD_SIZE * 0.5f ? WORLD_SIZE * 0.5f : Mathf.Clamp(center.y, halfH, WORLD_SIZE - halfH);

        Vector3 targetPosition = new Vector3(clampX, clampY, transform.position.z);
        if (!_logicalInit) { _logicalPos = transform.position; _logicalInit = true; }
        _logicalPos = Vector3.Lerp(_logicalPos, targetPosition, Time.deltaTime * smoothSpeed);
        transform.position = _logicalPos + (Vector3)UpdateShake();

        // --- 2. 相机平滑缩放以包含所有球 ---
        if (cam != null && cam.orthographic)
        {
            float maxBounds = 0f;
            float largestDiameter = 0f;

            foreach (var target in followTargets)
            {
                // 用目标的缩放（直径）计算半径
                float diameter = target.localScale.x;
                float radius = diameter / 2f;

                // 记录最大直径（用于保证大球周围的呼吸空间）
                if (diameter > largestDiameter) largestDiameter = diameter;

                // 计算当前球外边缘距离几何中心的最大距离
                float distance = Vector2.Distance(center, target.position) + radius;

                if (distance > maxBounds)
                {
                    maxBounds = distance;
                }
            }

            // 根据屏幕宽高比计算相机达到包围球体需要的 orthographicSize（half-height）
            float targetSizeY = maxBounds + padding;
            float targetSizeX = (maxBounds + padding) / cam.aspect;

            // 基于最大直径再计算一个“舒适”视野：以半径为基准，乘以 (1 + diameterMargin)
            float diameterBasedSize = (largestDiameter * 0.5f) * (1f + Mathf.Max(0f, diameterMargin));

            // 取最大值保证横向纵向都不会超出屏幕、不会小于设定的最小值，且满足大球的舒适视野。
            float targetSize = Mathf.Max(minSize, Mathf.Max(targetSizeY, Mathf.Max(targetSizeX, diameterBasedSize)));

            // 可选：最大视野限制（设置为 >0 则生效）
            if (maxSize > 0f)
                targetSize = Mathf.Min(targetSize, maxSize);

            // 平滑修改相机的 orthographicSize
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetSize, Time.deltaTime * sizeSmoothSpeed);
        }
    }
}
