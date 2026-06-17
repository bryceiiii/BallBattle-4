using UnityEngine;

/// <summary>
/// 方向指示器。在球体边缘放置箭头指向鼠标位置。
/// 箭头由脚本自动创建为子物体，用户只需提供 Sprite 纹理。
/// </summary>
public class AimIndicator : MonoBehaviour
{
    [Header("方向指示器")]
    public bool isLocalPlayer = false;

    [Header("箭头 Sprite（拖拽素材）")]
    public Sprite arrowSprite;      // 箭头纹理（默认朝右，脚本自动旋转和定位）

    [Header("参数")]
    public float offsetFromEdge = 0.4f;  // 箭头距球体边缘的间距（世界单位）
    public float arrowSize = 0.5f;       // 箭头大小
    public Color arrowColor = Color.white;

    private Camera mainCam;
    private Transform _arrow;    // 箭头子物体的 Transform
    private SpriteRenderer _sr;

    void Start()
    {
        mainCam = Camera.main;

        // 创建箭头子物体
        var go = new GameObject("AimArrow");
        go.transform.SetParent(transform, false);
        _arrow = go.transform;
        _sr = go.AddComponent<SpriteRenderer>();

        if (arrowSprite != null)
            _sr.sprite = arrowSprite;
        else
        {
            // 默认白色三角箭头
            var tex = new Texture2D(32, 16);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 32; x++)
                {
                    float cx = x - 16f, cy = y - 7.5f;
                    bool inside = (cx >= -8 && cx <= 8 && cy >= -6 && cy <= 6
                        && Mathf.Abs(cy) <= 6f * (1f - Mathf.Abs(cx) / 8f));
                    tex.SetPixel(x, y, inside ? Color.white : Color.clear);
                }
            tex.Apply();
            _sr.sprite = Sprite.Create(tex, new Rect(0, 0, 32, 16), new Vector2(0f, 0.5f));
        }
        _sr.color = arrowColor;

        SetActive(isLocalPlayer);
    }

    void Update()
    {
        if (!isLocalPlayer || _arrow == null || mainCam == null) return;

        Vector3 dir;

        // 手机模式：使用当前的瞄准方向（= 移动方向）
        if (PlatformInputManager.Instance != null && PlatformInputManager.Instance.IsMobileMode)
        {
            Vector2 aimDir = PlatformInputManager.Instance.GetCurrentDirection();
            if (aimDir.sqrMagnitude < 0.001f)
            {
                _arrow.gameObject.SetActive(false);
                return;
            }
            _arrow.gameObject.SetActive(true);
            dir = new Vector3(aimDir.x, aimDir.y, 0f);
        }
        else
        {
            // PC模式：鼠标世界坐标
            Vector3 mouseScreen = Input.mousePosition;
            mouseScreen.z = -mainCam.transform.position.z;
            Vector3 mouseWorld = mainCam.ScreenToWorldPoint(mouseScreen);
            mouseWorld.z = 0f;
            dir = mouseWorld - transform.position;
            dir.z = 0f;
        }

        if (dir.sqrMagnitude < 0.001f) return;
        dir.Normalize();

        // 位置：球体边缘 + 间距（世界坐标）
        float ballRadius = transform.localScale.x * 0.5f;
        float dist = ballRadius + offsetFromEdge;
        _arrow.position = transform.position + dir * dist;

        // 旋转指向瞄准方向
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        _arrow.rotation = Quaternion.Euler(0, 0, angle);

        // 大小
        _arrow.localScale = new Vector3(arrowSize, arrowSize, 1f);
    }

    public void SetActive(bool active)
    {
        isLocalPlayer = active;
        if (_arrow != null) _arrow.gameObject.SetActive(active);
    }
}
