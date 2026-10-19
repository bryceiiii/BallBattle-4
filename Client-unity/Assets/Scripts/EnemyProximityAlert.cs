using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shows a short directional arrow when a nearby enemy enters the alert radius.
/// The arrow tracks the nearest threatening enemy and fades out automatically.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-500)]
public class EnemyProximityAlert : MonoBehaviour
{
    [Header("Detection")]
    [InspectorName("警戒半径")]
    [Min(0.5f)] public float alertRadius = 8f;
    [InspectorName("解除警戒半径")]
    [Min(0.5f)] public float clearRadius = 10f;
    [InspectorName("检测间隔")]
    [Min(0.03f)] public float checkInterval = 0.12f;

    [Header("Alert")]
    [InspectorName("显示时长")]
    [Min(0.2f)] public float displayDuration = 1.35f;
    [InspectorName("同一敌人冷却")]
    [Min(0f)] public float rearmCooldown = 3f;
    [InspectorName("箭头距离")]
    [Min(40f)] public float arrowDistance = 190f;
    [InspectorName("箭头颜色")]
    public Color arrowColor = new Color(1f, 0.34f, 0.12f, 1f);
    [InspectorName("脉冲速度")]
    [Min(0f)] public float pulseSpeed = 11f;
    [InspectorName("调试日志")]
    public bool logDebug;

    private static EnemyProximityAlert _instance;

    private readonly HashSet<int> _nearbyEnemyIds = new HashSet<int>();
    private readonly HashSet<int> _nearbyNow = new HashSet<int>();
    private readonly Dictionary<int, float> _nextAllowedTime = new Dictionary<int, float>();

    private Transform _alertLocal;
    private Transform _alertEnemy;
    private int _alertEnemyId;
    private float _alertStartedAt;
    private float _alertEndsAt;
    private float _nextCheckAt;

    private Camera _camera;
    private RectTransform _canvasRect;
    private RectTransform _arrowRoot;
    private CanvasGroup _arrowGroup;
    private Sprite _arrowSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;

        _instance = FindObjectOfType<EnemyProximityAlert>();
        if (_instance == null)
        {
            var go = new GameObject("[EnemyProximityAlert]");
            _instance = go.AddComponent<EnemyProximityAlert>();
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            enabled = false;
            Destroy(this);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        EnsureArrowUI();
    }

    private void Update()
    {
        float now = Time.unscaledTime;

        if (_arrowRoot != null && _arrowRoot.gameObject.activeSelf)
            UpdateActiveAlert(now);

        if (now < _nextCheckAt) return;
        _nextCheckAt = now + Mathf.Max(0.03f, checkInterval);
        CheckForApproachingEnemies(now);
    }

    private void CheckForApproachingEnemies(float now)
    {
        _nearbyNow.Clear();

        CircleController nearestLocal = null;
        CircleController nearestEnemy = null;
        float nearestDistance = float.MaxValue;

        foreach (var enemyPair in GameManager.Circles)
        {
            var enemy = enemyPair.Value != null ? enemyPair.Value.GetComponent<CircleController>() : null;
            if (enemy == null || enemy.isLocalPlayer) continue;

            CircleController closestLocal = null;
            float closestDistance = float.MaxValue;
            Vector3 enemyPosition = enemy.transform.position;

            foreach (var localPair in GameManager.Circles)
            {
                var local = localPair.Value != null ? localPair.Value.GetComponent<CircleController>() : null;
                if (local == null || !local.isLocalPlayer) continue;

                float distance = Vector2.Distance(enemyPosition, local.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestLocal = local;
                }
            }

            if (closestLocal == null) continue;
            if (closestDistance <= clearRadius)
                _nearbyNow.Add(enemy.entityId);

            bool enteringAlert = !_nearbyEnemyIds.Contains(enemy.entityId) && closestDistance <= alertRadius;
            float nextAllowed;
            _nextAllowedTime.TryGetValue(enemy.entityId, out nextAllowed);
            if (enteringAlert && now >= nextAllowed && closestDistance < nearestDistance)
            {
                nearestDistance = closestDistance;
                nearestLocal = closestLocal;
                nearestEnemy = enemy;
            }
        }

        _nearbyEnemyIds.Clear();
        foreach (int enemyId in _nearbyNow)
            _nearbyEnemyIds.Add(enemyId);

        if (nearestEnemy != null && nearestLocal != null)
            TriggerAlert(nearestLocal.transform, nearestEnemy.transform, nearestEnemy.entityId, now);
    }

    private void TriggerAlert(Transform local, Transform enemy, int enemyId, float now)
    {
        EnsureArrowUI();
        if (_arrowRoot == null || local == null || enemy == null) return;

        _alertLocal = local;
        _alertEnemy = enemy;
        _alertEnemyId = enemyId;
        _alertStartedAt = now;
        _alertEndsAt = now + Mathf.Max(0.2f, displayDuration);
        _nextAllowedTime[enemyId] = now + Mathf.Max(0f, rearmCooldown);

        _arrowRoot.gameObject.SetActive(true);
        _arrowRoot.SetAsLastSibling();
        UpdateArrowDirectionAndPosition();

        if (logDebug)
            Debug.Log("[EnemyProximityAlert] Nearby enemy alert: " + enemyId);
    }

    private void UpdateActiveAlert(float now)
    {
        if (now >= _alertEndsAt || _alertLocal == null || _alertEnemy == null)
        {
            HideArrow();
            return;
        }

        UpdateArrowDirectionAndPosition();

        float elapsed = now - _alertStartedAt;
        float remaining = _alertEndsAt - now;
        float fadeIn = Mathf.Clamp01(elapsed / 0.09f);
        float fadeOut = Mathf.Clamp01(remaining / 0.24f);
        float alpha = Mathf.Min(fadeIn, fadeOut);
        _arrowGroup.alpha = alpha;

        float pulse = 1f + Mathf.Sin(now * Mathf.Max(0f, pulseSpeed)) * 0.075f;
        _arrowRoot.localScale = new Vector3(pulse, pulse, 1f);
    }

    private void UpdateArrowDirectionAndPosition()
    {
        if (_arrowRoot == null || _canvasRect == null || _alertLocal == null || _alertEnemy == null) return;
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return;

        Vector3 localScreen = _camera.WorldToScreenPoint(_alertLocal.position);
        Vector3 enemyScreen = _camera.WorldToScreenPoint(_alertEnemy.position);
        Vector2 direction = (Vector2)(enemyScreen - localScreen);
        if (direction.sqrMagnitude < 0.001f)
            direction = Vector2.up;
        direction.Normalize();

        Vector2 localCanvasPosition;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect, localScreen, null, out localCanvasPosition);

        Vector2 targetPosition = localCanvasPosition + direction * Mathf.Max(40f, arrowDistance);
        float halfWidth = _canvasRect.rect.width * 0.5f - 92f;
        float halfHeight = _canvasRect.rect.height * 0.5f - 74f;
        targetPosition.x = Mathf.Clamp(targetPosition.x, -halfWidth, halfWidth);
        targetPosition.y = Mathf.Clamp(targetPosition.y, -halfHeight, halfHeight);
        _arrowRoot.anchoredPosition = targetPosition;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        _arrowRoot.localEulerAngles = new Vector3(0f, 0f, angle);
    }

    private void HideArrow()
    {
        if (_arrowRoot != null)
            _arrowRoot.gameObject.SetActive(false);

        _alertLocal = null;
        _alertEnemy = null;
        _alertEnemyId = 0;
    }

    private void EnsureArrowUI()
    {
        if (_arrowRoot != null) return;

        _arrowSprite = CreateArrowSprite(192, 128);

        var canvasGo = new GameObject("EnemyAlertCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4500;

        var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        _canvasRect = canvasGo.GetComponent<RectTransform>();

        var rootGo = new GameObject("AlertArrow", typeof(RectTransform), typeof(CanvasGroup));
        rootGo.transform.SetParent(canvasGo.transform, false);
        _arrowRoot = rootGo.GetComponent<RectTransform>();
        _arrowRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _arrowRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _arrowRoot.pivot = new Vector2(0.5f, 0.5f);
        _arrowRoot.sizeDelta = new Vector2(156f, 104f);
        _arrowGroup = rootGo.GetComponent<CanvasGroup>();
        _arrowGroup.alpha = 0f;
        _arrowGroup.blocksRaycasts = false;

        var glowGo = new GameObject("Glow", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        glowGo.transform.SetParent(rootGo.transform, false);
        var glowRt = glowGo.GetComponent<RectTransform>();
        glowRt.anchorMin = Vector2.zero;
        glowRt.anchorMax = Vector2.one;
        glowRt.offsetMin = new Vector2(-10f, -10f);
        glowRt.offsetMax = new Vector2(10f, 10f);
        var glowImage = glowGo.GetComponent<UnityEngine.UI.Image>();
        glowImage.sprite = _arrowSprite;
        glowImage.preserveAspect = true;
        glowImage.raycastTarget = false;
        glowImage.color = new Color(arrowColor.r, arrowColor.g, arrowColor.b, 0.14f);

        var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        arrowGo.transform.SetParent(rootGo.transform, false);
        var arrowRt = arrowGo.GetComponent<RectTransform>();
        arrowRt.anchorMin = Vector2.zero;
        arrowRt.anchorMax = Vector2.one;
        arrowRt.offsetMin = Vector2.zero;
        arrowRt.offsetMax = Vector2.zero;

        var arrowImage = arrowGo.GetComponent<UnityEngine.UI.Image>();
        arrowImage.sprite = _arrowSprite;
        arrowImage.preserveAspect = true;
        arrowImage.raycastTarget = false;
        arrowImage.color = arrowColor;

        var outline = arrowGo.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(1f, 0.13f, 0.04f, 0.48f);
        outline.effectDistance = new Vector2(4f, 4f);

        rootGo.SetActive(false);
    }

    private static Sprite CreateArrowSprite(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = "Generated_EnemyAlertArrow";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        Vector2[] chevronA =
        {
            new Vector2(20f, 14f), new Vector2(62f, 14f), new Vector2(111f, 64f),
            new Vector2(62f, 114f), new Vector2(20f, 114f), new Vector2(69f, 64f)
        };
        Vector2[] chevronB =
        {
            new Vector2(82f, 14f), new Vector2(124f, 14f), new Vector2(173f, 64f),
            new Vector2(124f, 114f), new Vector2(82f, 114f), new Vector2(131f, 64f)
        };

        var pixels = new Color32[width * height];
        const int samplesPerAxis = 3;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int insideCount = 0;
                for (int sy = 0; sy < samplesPerAxis; sy++)
                {
                    for (int sx = 0; sx < samplesPerAxis; sx++)
                    {
                        float px = x + (sx + 0.5f) / samplesPerAxis;
                        float py = y + (sy + 0.5f) / samplesPerAxis;
                        var point = new Vector2(px, py);
                        if (PointInPolygon(point, chevronA) || PointInPolygon(point, chevronB))
                            insideCount++;
                    }
                }

                float alpha = insideCount / (float)(samplesPerAxis * samplesPerAxis);
                pixels[y * width + x] = new Color32(255, 255, 255,
                    (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
    }

    private static bool PointInPolygon(Vector2 point, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];
            bool crosses = (a.y > point.y) != (b.y > point.y)
                           && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
            if (crosses)
                inside = !inside;
        }
        return inside;
    }

    private void OnValidate()
    {
        clearRadius = Mathf.Max(clearRadius, alertRadius + 0.1f);
    }
}
