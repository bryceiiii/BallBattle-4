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
    public float sizeSmoothSpeed = 2f;
    public float minSize = 5f; // 相机最小的正交视野大小
    public float padding = 2f; // 屏幕边缘留白的距离

    private Camera cam;

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

    void Update()
    {
        // 自动清理已经被销毁的物体引用
        followTargets.RemoveAll(t => t == null);
        
        if(followTargets.Count == 0) return;
        
        // --- 1. 相机平滑移动到所有球的几何中心 ---
        Vector2 center = Vector2.zero;
        foreach (var target in followTargets)
        {
            center += (Vector2)target.position;
        }
        center /= followTargets.Count;

        Vector3 targetPosition = new Vector3(center.x, center.y, transform.position.z);
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * smoothSpeed);

        // --- 2. 相机平滑缩放以包含所有球 ---
        if (cam != null && cam.orthographic)
        {
            float maxBounds = 0f;
            
            foreach (var target in followTargets)
            {
                // 用目标的缩放（直径）计算半径
                float radius = target.localScale.x / 2f;
                // 计算当前球外边缘距离几何中心的最大距离
                float distance = Vector2.Distance(center, target.position) + radius;

                if (distance > maxBounds)
                {
                    maxBounds = distance;
                }
            }

            // 根据屏幕宽高比计算相机达到包围球体需要的 orthographicSize
            float targetSizeY = maxBounds + padding; 
            float targetSizeX = (maxBounds + padding) / cam.aspect; 
            
            // 取最大值保证横向纵向都不会超出屏幕且不小于设定的最小值
            float targetSize = Mathf.Max(minSize, Mathf.Max(targetSizeY, targetSizeX));

            // 平滑修改相机的 orthographicSize
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetSize, Time.deltaTime * sizeSmoothSpeed);
        }
    }
}
