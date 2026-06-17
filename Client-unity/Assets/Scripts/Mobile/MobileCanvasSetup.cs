using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 自动创建手机端完整的触屏操作 UI（摇杆 + 按钮）。
/// 运行时动态构建，不依赖预制体。
/// 挂到场景中任意需要时激活的 GameObject 上，或直接挂到 MobileInputController。
/// </summary>
public class MobileCanvasSetup : MonoBehaviour
{
    [Header("自动构建")]
    public bool buildOnStart = true;
    public string canvasName = "MobileTouchCanvas";

    [Header("颜色主题")]
    public Color joystickBgColor = new Color(1f, 1f, 1f, 0.15f);
    public Color joystickHandleColor = new Color(1f, 1f, 1f, 0.4f);
    public Color shootButtonColor = new Color(0.9f, 0.2f, 0.2f, 0.5f);
    public Color splitButtonColor = new Color(0.2f, 0.5f, 0.9f, 0.5f);
    public Color ammoButtonColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
    public Color ammoActiveColor = new Color(1f, 1f, 0.5f, 0.5f);

    private Canvas _canvas;
    private void Start()
    {
        if (buildOnStart)
            BuildMobileUI();
    }

    /// <summary>构建完整的手机端触屏UI</summary>
    public MobileInputController BuildMobileUI()
    {
        // 1. 创建 Canvas
        var canvasGo = new GameObject(canvasName);
        canvasGo.transform.SetParent(transform, false); // 放在当前对象下

        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100; // 确保在最上层

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); // 16:9横屏基准
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f; // 按宽度缩放（横屏优先）

        canvasGo.AddComponent<GraphicRaycaster>();

        // 2. 确保有 EventSystem
        if (UnityEngine.EventSystems.EventSystem.current == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // 3. 创建摇杆区域（左下角）
        var joystickCtrl = CreateJoystick(canvasGo.transform);

        // 4. 创建射击按钮（右下角，大）
        var shootBtn = CreateShootButton(canvasGo.transform);

        // 5. 创建分裂按钮（射击按钮上方）
        var splitBtn = CreateSplitButton(canvasGo.transform);

        // 6. 创建弹药切换按钮（射击按钮旁边）
        var ammoBtns = CreateAmmoButtons(canvasGo.transform);

        // 7. 组装到 MobileInputController
        var mobileInput = GetComponent<MobileInputController>();
        if (mobileInput == null)
            mobileInput = gameObject.AddComponent<MobileInputController>();

        mobileInput.moveJoystick = joystickCtrl;
        mobileInput.shootButton = shootBtn;
        mobileInput.splitButton = splitBtn;
        mobileInput.ammoButtons = ammoBtns;

        // 创建高亮引用数组
        var highlights = new Image[ammoBtns.Length];
        for (int i = 0; i < ammoBtns.Length; i++)
            highlights[i] = ammoBtns[i].GetComponent<Image>();
        mobileInput.ammoButtonHighlights = highlights;

        Debug.Log($"[MobileCanvasSetup] 手机端触屏UI已构建完成 (Canvas: {canvasName})");
        return mobileInput;
    }

    // ===== 摇杆 =====
    private VirtualJoystick CreateJoystick(Transform parent)
    {
        // 背景
        var bgGo = new GameObject("JoystickBg");
        bgGo.transform.SetParent(parent, false);
        var bgRt = bgGo.AddComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0.15f, 0.2f);
        bgRt.anchorMax = new Vector2(0.15f, 0.2f);
        bgRt.sizeDelta = new Vector2(280, 280);
        bgRt.anchoredPosition = Vector2.zero;

        var bgImg = bgGo.AddComponent<Image>();
        bgImg.color = joystickBgColor;
        // 圆形遮罩 (通过 Sprite 创建)
        bgImg.sprite = CreateCircleSprite(128, joystickBgColor);

        // 手柄
        var handleGo = new GameObject("JoystickHandle");
        handleGo.transform.SetParent(bgGo.transform, false);
        var handleRt = handleGo.AddComponent<RectTransform>();
        handleRt.sizeDelta = new Vector2(140, 140);

        var handleImg = handleGo.AddComponent<Image>();
        handleImg.color = joystickHandleColor;
        handleImg.sprite = CreateCircleSprite(64, joystickHandleColor);

        var joystick = bgGo.AddComponent<VirtualJoystick>();
        joystick.backgroundRect = bgRt;
        joystick.handleRect = handleRt;
        joystick.handleMoveRange = 0.85f;
        joystick.deadZone = 0.08f;
        joystick.followFinger = false; // 固定位置摇杆（更稳定）

        return joystick;
    }

    // ===== 射击按钮 =====
    private Button CreateShootButton(Transform parent)
    {
        var go = new GameObject("ShootButton");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.85f, 0.18f);
        rt.anchorMax = new Vector2(0.85f, 0.18f);
        rt.sizeDelta = new Vector2(200, 200);
        rt.anchoredPosition = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = shootButtonColor;
        img.sprite = CreateCircleSprite(100, shootButtonColor);

        // 文字
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = labelGo.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero; labelRt.anchorMax = Vector2.one;
        labelRt.sizeDelta = Vector2.zero;
        var label = labelGo.AddComponent<Text>();
        label.text = "射击";
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 40;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;

        return go.AddComponent<Button>();
    }

    // ===== 分裂按钮 =====
    private Button CreateSplitButton(Transform parent)
    {
        var go = new GameObject("SplitButton");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.75f, 0.4f);
        rt.anchorMax = new Vector2(0.75f, 0.4f);
        rt.sizeDelta = new Vector2(130, 130);
        rt.anchoredPosition = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = splitButtonColor;
        img.sprite = CreateCircleSprite(65, splitButtonColor);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = labelGo.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero; labelRt.anchorMax = Vector2.one;
        labelRt.sizeDelta = Vector2.zero;
        var label = labelGo.AddComponent<Text>();
        label.text = "分裂";
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 28;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;

        return go.AddComponent<Button>();
    }

    // ===== 弹药按钮 =====
    private Button[] CreateAmmoButtons(Transform parent)
    {
        string[] names = { "普通弹", "分裂弹" };
        Button[] btns = new Button[names.Length];

        for (int i = 0; i < names.Length; i++)
        {
            var go = new GameObject($"AmmoBtn_{i}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();

            // 排列在射击按钮上方
            rt.anchorMin = new Vector2(0.78f, 0.55f + i * 0.11f);
            rt.anchorMax = new Vector2(0.78f, 0.55f + i * 0.11f);
            rt.sizeDelta = new Vector2(160, 70);
            rt.anchoredPosition = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = ammoButtonColor;

            // 文字
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero; labelRt.anchorMax = Vector2.one;
            labelRt.sizeDelta = Vector2.zero;
            var label = labelGo.AddComponent<Text>();
            label.text = names[i];
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 24;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;

            btns[i] = go.AddComponent<Button>();
        }

        return btns;
    }

    /// <summary>生成圆形 Sprite（纯色，运行时）</summary>
    public static Sprite CreateCircleSprite(int size, Color color)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = size / 2f;
        float radius = center - 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center, dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist <= radius)
                {
                    // 边缘渐变
                    float alpha = 1f - Mathf.Clamp01((dist - radius + 4f) / 4f);
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
