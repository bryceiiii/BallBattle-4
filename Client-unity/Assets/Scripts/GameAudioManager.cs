using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Central audio and settings manager.
/// It owns background music, short sound effects, volume persistence, and the global settings panel.
/// </summary>
[DefaultExecutionOrder(-10000)]
public class GameAudioManager : MonoBehaviour
{
    private const string MusicVolumeKey = "settings.audio.music_volume";
    private const string DefaultBackgroundMusicPath = "Audio/BackgroundMusic";
    private const string AlternateBackgroundMusicPath = "BackgroundMusic";

    private static GameAudioManager _instance;

    public static GameAudioManager Instance
    {
        get
        {
            if (_instance != null) return _instance;

            _instance = FindObjectOfType<GameAudioManager>();
            if (_instance == null)
            {
                var go = new GameObject("[GameAudioManager]");
                _instance = go.AddComponent<GameAudioManager>();
            }
            return _instance;
        }
    }

    [Header("设置入口 Settings Entry")]
    [InspectorName("设置按钮")]
    [Tooltip("可直接在 Scene 中拖动 SettingsButton 调整位置。留空时运行时自动生成。")]
    public Button settingsButton;

    [Header("背景音乐 BGM")]
    [InspectorName("背景音乐")]
    [Tooltip("循环播放的背景音乐。留空时会尝试从 Resources/Audio/BackgroundMusic 加载。")]
    public AudioClip backgroundMusic;
    [InspectorName("自动加载背景音乐")]
    [Tooltip("允许从 Resources/Audio/BackgroundMusic 自动查找背景音乐。")]
    public bool loadBackgroundMusicFromResources = true;
    [InspectorName("默认音量")]
    [Tooltip("首次启动时的默认背景音乐音量。")]
    [Range(0f, 1f)] public float defaultMusicVolume = 0.7f;

    [Header("音效 SFX")]
    [InspectorName("射击音效")]
    [Tooltip("射击音效")]
    public AudioClip shootClip;
    [InspectorName("命中音效")]
    [Tooltip("命中音效")]
    public AudioClip hitClip;
    [InspectorName("空枪音效")]
    [Tooltip("射速冷却时的空枪音效")]
    public AudioClip emptyClip;
    [InspectorName("分裂音效")]
    [Tooltip("分裂音效")]
    public AudioClip splitClip;
    [InspectorName("拾取音效")]
    [Tooltip("护盾、加速和特殊弹药拾取音效")]
    public AudioClip pickupClip;
    [InspectorName("UI 点击音效")]
    [Tooltip("设置面板按钮点击音效")]
    public AudioClip uiClickClip;
    [InspectorName("音效总音量")]
    [Tooltip("所有游戏音效的总音量。")]
    [Range(0f, 1f)] public float sfxVolume = 1f;
    [InspectorName("UI 点击音量")]
    [Tooltip("设置面板按钮点击音的额外音量。")]
    [Range(0f, 1f)] public float uiClickVolume = 0.62f;

    private AudioSource _musicSource;
    private readonly List<AudioSource> _sfxSources = new List<AudioSource>();
    private float _musicVolume = 0.7f;

    private GameObject _settingsRoot;
    private GameObject _settingsButton;
    private Slider _musicSlider;
    private Text _musicValueText;
    private CanvasGroup _settingsCanvasGroup;
    private RectTransform _settingsPanelRect;
    private Coroutine _settingsTransition;

    private Sprite _roundedPanelSprite;
    private Sprite _roundedSurfaceSprite;
    private Sprite _roundedPillSprite;
    private Sprite _softCircleSprite;
    private Sprite _gridSprite;
    private Sprite _sheenSprite;

    public float MusicVolume => _musicVolume;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        _ = Instance;
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
        InitializeAudio();
    }

    private void Start()
    {
        EnsureSettingsUI();
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause) SaveSettings();
    }

    private void OnApplicationQuit()
    {
        SaveSettings();
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private void InitializeAudio()
    {
        _musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, defaultMusicVolume));

        _musicSource = gameObject.AddComponent<AudioSource>();
        _musicSource.playOnAwake = false;
        _musicSource.loop = true;
        _musicSource.spatialBlend = 0f;
        _musicSource.volume = _musicVolume;
        _musicSource.priority = 0;

        AudioClip clip = backgroundMusic;
        if (clip == null && loadBackgroundMusicFromResources)
        {
            clip = Resources.Load<AudioClip>(DefaultBackgroundMusicPath);
            if (clip == null)
                clip = Resources.Load<AudioClip>(AlternateBackgroundMusicPath);
        }

        backgroundMusic = clip;
        shootClip = LoadClipIfMissing(shootClip, "Audio/SFX_Shoot");
        hitClip = LoadClipIfMissing(hitClip, "Audio/SFX_Hit");
        emptyClip = LoadClipIfMissing(emptyClip, "Audio/SFX_Empty");
        splitClip = LoadClipIfMissing(splitClip, "Audio/SFX_Split");
        pickupClip = LoadClipIfMissing(pickupClip, "Audio/SFX_Pickup");
        uiClickClip = LoadClipIfMissing(uiClickClip, "Audio/SFX_UI_Click");

        if (clip != null)
            PlayBackgroundMusic(clip, true);
    }

    [ContextMenu("自动填充 Resources 音频")]
    private void AutoFillAudioFromResources()
    {
        if (backgroundMusic == null)
        {
            backgroundMusic = Resources.Load<AudioClip>(DefaultBackgroundMusicPath);
            if (backgroundMusic == null)
                backgroundMusic = Resources.Load<AudioClip>(AlternateBackgroundMusicPath);
        }
        shootClip = LoadClipIfMissing(shootClip, "Audio/SFX_Shoot");
        hitClip = LoadClipIfMissing(hitClip, "Audio/SFX_Hit");
        emptyClip = LoadClipIfMissing(emptyClip, "Audio/SFX_Empty");
        splitClip = LoadClipIfMissing(splitClip, "Audio/SFX_Split");
        pickupClip = LoadClipIfMissing(pickupClip, "Audio/SFX_Pickup");
        uiClickClip = LoadClipIfMissing(uiClickClip, "Audio/SFX_UI_Click");
    }

    public void SetBackgroundMusic(AudioClip clip, bool restart = true)
    {
        backgroundMusic = clip;
        if (_musicSource == null) return;

        bool changedClip = _musicSource.clip != clip;
        _musicSource.clip = clip;

        if (clip == null)
        {
            _musicSource.Stop();
            return;
        }

        if (restart || changedClip || !_musicSource.isPlaying)
            PlayBackgroundMusic(clip, restart);
    }

    public void PlayBackgroundMusic(AudioClip clip, bool restart = true)
    {
        if (_musicSource == null) return;

        if (clip != null)
            _musicSource.clip = clip;
        if (_musicSource.clip == null) return;

        if (restart)
            _musicSource.Stop();
        if (!_musicSource.isPlaying)
            _musicSource.Play();
    }

    public void StopBackgroundMusic()
    {
        if (_musicSource != null)
            _musicSource.Stop();
    }

    public void SetMusicVolume(float volume, bool save = false)
    {
        _musicVolume = Mathf.Clamp01(volume);
        if (_musicSource != null)
            _musicSource.volume = _musicVolume;

        PlayerPrefs.SetFloat(MusicVolumeKey, _musicVolume);
        if (_musicSlider != null && !Mathf.Approximately(_musicSlider.value, _musicVolume))
            _musicSlider.SetValueWithoutNotify(_musicVolume);
        UpdateMusicValueText();

        if (save)
            SaveSettings();
    }

    public void SetSfxVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
    }

    private static AudioClip LoadClipIfMissing(AudioClip clip, string resourcePath)
    {
        return clip != null ? clip : Resources.Load<AudioClip>(resourcePath);
    }

    public void PlaySfx(AudioClip clip, float volumeScale = 1f, float pitch = 1f)
    {
        if (clip == null) return;

        AudioSource source = GetAvailableSfxSource();
        source.clip = clip;
        source.volume = Mathf.Clamp01(sfxVolume * volumeScale);
        source.pitch = Mathf.Clamp(pitch, 0.25f, 3f);
        source.Play();
    }

    public void SaveSettings()
    {
        PlayerPrefs.SetFloat(MusicVolumeKey, _musicVolume);
        PlayerPrefs.Save();
    }

    private AudioSource GetAvailableSfxSource()
    {
        for (int i = 0; i < _sfxSources.Count; i++)
        {
            if (_sfxSources[i] != null && !_sfxSources[i].isPlaying)
                return _sfxSources[i];
        }

        var go = new GameObject("SfxSource");
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        _sfxSources.Add(source);
        return source;
    }

    private void EnsureSettingsUI()
    {
        if (_settingsRoot != null) return;

        EnsureEventSystem();
        EnsureUiSprites();

        Canvas canvas = ResolveSettingsCanvas();
        if (canvas == null)
        {
            canvas = CreateSettingsCanvas();
        }

        if (settingsButton == null && canvas != null)
        {
            var buttonTransform = canvas.transform.Find("SettingsButton");
            if (buttonTransform != null)
                settingsButton = buttonTransform.GetComponent<Button>();
        }

        if (settingsButton != null)
        {
            _settingsButton = settingsButton.gameObject;
            ApplySettingsButtonStyle(settingsButton);
        }
        else
        {
            _settingsButton = CreateSettingsButton(canvas.transform);
            settingsButton = _settingsButton.GetComponent<Button>();
        }

        _settingsRoot = CreateSettingsPanel(canvas.transform);
        _settingsRoot.SetActive(false);
    }

    private Canvas ResolveSettingsCanvas()
    {
        if (settingsButton != null)
        {
            var parentCanvas = settingsButton.GetComponentInParent<Canvas>();
            if (parentCanvas != null)
                return ConfigureSettingsCanvas(parentCanvas.rootCanvas);
        }

        var existingCanvasGo = GameObject.Find("GlobalSettingsCanvas");
        if (existingCanvasGo != null)
        {
            var existingCanvas = existingCanvasGo.GetComponent<Canvas>();
            if (existingCanvas != null)
                return ConfigureSettingsCanvas(existingCanvas);
        }
        return null;
    }

    private Canvas CreateSettingsCanvas()
    {
        var canvasGo = new GameObject("GlobalSettingsCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        return ConfigureSettingsCanvas(canvas);
    }

    private Canvas ConfigureSettingsCanvas(Canvas canvas)
    {
        if (canvas == null) return null;

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        if (canvas.sortingOrder < 5000)
            canvas.sortingOrder = 5000;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private void EnsureUiSprites()
    {
        if (_roundedPanelSprite != null) return;

        _roundedPanelSprite = CreateRoundedGradientSprite(192, 192, 30, 34, true);
        _roundedSurfaceSprite = CreateRoundedGradientSprite(128, 128, 22, 24, false);
        _roundedPillSprite = CreateRoundedGradientSprite(128, 48, 20, 20, false);
        _softCircleSprite = CreateSoftCircleSprite(64);
        _gridSprite = CreateGridSprite(64);
        _sheenSprite = CreateSheenSprite(256, 128);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        var eventSystemGo = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(eventSystemGo);
    }

    private GameObject CreateSettingsButton(Transform parent)
    {
        Button button = CreateButton(
            "SettingsButton",
            parent,
            "设置",
            new Color(0.055f, 0.09f, 0.16f, 0.96f),
            new Color(0.08f, 0.24f, 0.36f, 1f));

        var shadow = button.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0.08f, 0.2f, 0.55f);
        shadow.effectDistance = new Vector2(0f, -3f);

        ApplySettingsButtonStyle(button);
        return button.gameObject;
    }

    private void ApplySettingsButtonStyle(Button button)
    {
        if (button == null) return;

        var image = button.GetComponent<Image>();
        if (image == null)
            image = button.gameObject.AddComponent<Image>();
        image.sprite = _roundedSurfaceSprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.045f, 0.15f, 0.23f, 0.98f);

        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = new Color(0.045f, 0.15f, 0.23f, 0.98f);
        colors.highlightedColor = new Color(0.07f, 0.30f, 0.40f, 1f);
        colors.pressedColor = new Color(0.05f, 0.16f, 0.26f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.2f, 0.22f, 0.28f, 0.5f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        var outline = button.GetComponent<Outline>();
        if (outline == null)
            outline = button.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.2f, 0.78f, 0.96f, 0.25f);
        outline.effectDistance = new Vector2(1f, 1f);

        if (button.GetComponent<Shadow>() == null)
        {
            var shadow = button.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0.08f, 0.2f, 0.55f);
            shadow.effectDistance = new Vector2(0f, -3f);
        }

        var label = button.GetComponentInChildren<Text>(true);
        if (label == null)
        {
            label = CreateText("Label", button.transform, "设置", 20, FontStyle.Bold, TextAnchor.MiddleCenter);
        }
        label.text = "设置";
        label.fontSize = 20;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.88f, 0.96f, 1f, 1f);
        SetRect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            new Vector2(15f, 0f), new Vector2(-30f, 0f));

        EnsureSettingsButtonLayout(button);

        button.onClick.RemoveListener(OpenSettings);
        button.onClick.AddListener(OpenSettings);
    }

    private void EnsureSettingsButtonLayout(Button button)
    {
        var rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-24f, -390f);
        rect.sizeDelta = new Vector2(176f, 58f);

        var plateTransform = button.transform.Find("MenuPlate");
        Image plate;
        if (plateTransform == null)
        {
            var go = new GameObject("MenuPlate", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(button.transform, false);
            plate = go.GetComponent<Image>();
        }
        else
        {
            plate = plateTransform.GetComponent<Image>();
        }

        plate.sprite = _softCircleSprite;
        plate.color = new Color(0.20f, 0.78f, 0.96f, 0.18f);
        plate.raycastTarget = false;
        SetRect(plate.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(13f, 0f), new Vector2(36f, 36f));

        for (int i = 0; i < 3; i++)
        {
            string partName = $"MenuLine_{i}";
            var partTransform = plate.transform.Find(partName);
            Image line;
            if (partTransform == null)
            {
                var go = new GameObject(partName, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(plate.transform, false);
                line = go.GetComponent<Image>();
            }
            else
            {
                line = partTransform.GetComponent<Image>();
            }
            line.sprite = _roundedPillSprite;
            line.type = Image.Type.Sliced;
            line.color = new Color(0.72f, 0.94f, 1f, 1f);
            line.raycastTarget = false;
            SetRect(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 6f - i * 6f), new Vector2(14f, 2f));
        }
    }

    private GameObject CreateSettingsPanel(Transform parent)
    {
        var root = new GameObject("SettingsPanel", typeof(RectTransform), typeof(Image), typeof(Button));
        root.transform.SetParent(parent, false);

        var rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        var overlay = root.GetComponent<Image>();
        overlay.color = new Color(0.01f, 0.02f, 0.05f, 0.72f);

        _settingsCanvasGroup = root.AddComponent<CanvasGroup>();
        _settingsCanvasGroup.alpha = 1f;
        _settingsCanvasGroup.interactable = true;
        _settingsCanvasGroup.blocksRaycasts = true;

        var overlayButton = root.GetComponent<Button>();
        overlayButton.transition = Selectable.Transition.None;
        overlayButton.onClick.AddListener(CloseSettings);

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Button));
        panel.transform.SetParent(root.transform, false);

        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta = new Vector2(580f, 354f);
        _settingsPanelRect = panelRt;

        var panelImage = panel.GetComponent<Image>();
        panelImage.sprite = _roundedPanelSprite;
        panelImage.type = Image.Type.Sliced;
        panelImage.color = new Color(0.055f, 0.075f, 0.125f, 0.985f);

        var panelButton = panel.GetComponent<Button>();
        panelButton.transition = Selectable.Transition.None;
        panelButton.targetGraphic = panelImage;

        var panelOutline = panel.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0.16f, 0.52f, 1f, 0.42f);
        panelOutline.effectDistance = new Vector2(2f, -2f);

        var panelShadow = panel.AddComponent<Shadow>();
        panelShadow.effectColor = new Color(0f, 0f, 0f, 0.62f);
        panelShadow.effectDistance = new Vector2(0f, -8f);

        var mask = panel.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        var gridGo = new GameObject("TextureGrid", typeof(RectTransform), typeof(Image));
        gridGo.transform.SetParent(panel.transform, false);
        var gridRt = gridGo.GetComponent<RectTransform>();
        gridRt.anchorMin = Vector2.zero;
        gridRt.anchorMax = Vector2.one;
        gridRt.offsetMin = new Vector2(18f, 18f);
        gridRt.offsetMax = new Vector2(-18f, -18f);
        var grid = gridGo.GetComponent<Image>();
        grid.sprite = _gridSprite;
        grid.type = Image.Type.Tiled;
        grid.color = new Color(0.28f, 0.58f, 1f, 0.055f);
        grid.raycastTarget = false;

        var sheenGo = new GameObject("GlassSheen", typeof(RectTransform), typeof(Image));
        sheenGo.transform.SetParent(panel.transform, false);
        var sheenRt = sheenGo.GetComponent<RectTransform>();
        sheenRt.anchorMin = Vector2.zero;
        sheenRt.anchorMax = Vector2.one;
        sheenRt.offsetMin = Vector2.zero;
        sheenRt.offsetMax = Vector2.zero;
        var sheen = sheenGo.GetComponent<Image>();
        sheen.sprite = _sheenSprite;
        sheen.type = Image.Type.Simple;
        sheen.color = new Color(0.42f, 0.72f, 1f, 0.12f);
        sheen.raycastTarget = false;

        var titleAccentGo = new GameObject("TitleAccent", typeof(RectTransform), typeof(Image));
        titleAccentGo.transform.SetParent(panel.transform, false);
        var titleAccentRt = titleAccentGo.GetComponent<RectTransform>();
        titleAccentRt.anchorMin = new Vector2(0f, 1f);
        titleAccentRt.anchorMax = new Vector2(0f, 1f);
        titleAccentRt.pivot = new Vector2(0f, 1f);
        titleAccentRt.anchoredPosition = new Vector2(27f, -29f);
        titleAccentRt.sizeDelta = new Vector2(4f, 40f);
        var titleAccent = titleAccentGo.GetComponent<Image>();
        titleAccent.color = new Color(0.20f, 0.66f, 1f, 0.92f);
        titleAccent.raycastTarget = false;

        var topLineGo = new GameObject("TopGlowLine", typeof(RectTransform), typeof(Image));
        topLineGo.transform.SetParent(panel.transform, false);
        var topLineRt = topLineGo.GetComponent<RectTransform>();
        topLineRt.anchorMin = new Vector2(0.5f, 1f);
        topLineRt.anchorMax = new Vector2(0.5f, 1f);
        topLineRt.pivot = new Vector2(0.5f, 1f);
        topLineRt.anchoredPosition = new Vector2(0f, -2f);
        topLineRt.sizeDelta = new Vector2(360f, 2f);
        var topLine = topLineGo.GetComponent<Image>();
        topLine.color = new Color(0.18f, 0.58f, 1f, 0.52f);
        topLine.raycastTarget = false;

        var musicCardGo = new GameObject("MusicCard", typeof(RectTransform), typeof(Image));
        musicCardGo.transform.SetParent(panel.transform, false);
        var musicCardRt = musicCardGo.GetComponent<RectTransform>();
        musicCardRt.anchorMin = new Vector2(0f, 1f);
        musicCardRt.anchorMax = new Vector2(1f, 1f);
        musicCardRt.pivot = new Vector2(0.5f, 1f);
        musicCardRt.anchoredPosition = new Vector2(0f, -96f);
        musicCardRt.sizeDelta = new Vector2(-52f, 118f);
        var musicCard = musicCardGo.GetComponent<Image>();
        musicCard.sprite = _roundedSurfaceSprite;
        musicCard.type = Image.Type.Sliced;
        musicCard.color = new Color(0.035f, 0.06f, 0.105f, 0.76f);
        musicCard.raycastTarget = false;

        Text title = CreateText("Title", panel.transform, "设置", 34, FontStyle.Bold, TextAnchor.MiddleLeft);
        SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(46f, -24f), new Vector2(260f, 48f));
        title.color = new Color(0.88f, 0.95f, 1f);

        Button closeButton = CreateButton(
            "CloseButton",
            panel.transform,
            "关闭",
            new Color(0.105f, 0.15f, 0.23f, 0.96f),
            new Color(0.25f, 0.50f, 0.75f, 1f));
        SetRect(closeButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(-24f, -22f), new Vector2(88f, 42f));
        closeButton.onClick.AddListener(CloseSettings);

        Text musicLabel = CreateText(
            "MusicLabel", panel.transform, "背景音乐音量", 24, FontStyle.Normal, TextAnchor.MiddleLeft);
        SetRect(musicLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(48f, -116f), new Vector2(300f, 36f));
        musicLabel.color = new Color(0.80f, 0.88f, 0.98f);

        _musicValueText = CreateText(
            "MusicValue", panel.transform, "70%", 22, FontStyle.Bold, TextAnchor.MiddleRight);
        SetRect(_musicValueText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(-48f, -116f), new Vector2(100f, 36f));
        _musicValueText.color = new Color(0.42f, 0.78f, 1f);

        _musicSlider = CreateSlider(panel.transform);
        SetRect(_musicSlider.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(48f, -174f), new Vector2(484f, 42f));
        _musicSlider.minValue = 0f;
        _musicSlider.maxValue = 1f;
        _musicSlider.wholeNumbers = false;
        _musicSlider.value = _musicVolume;
        _musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);

        Button quitButton = CreateButton(
            "QuitButton",
            panel.transform,
            "退出游戏",
            new Color(0.48f, 0.10f, 0.15f, 0.98f),
            new Color(0.86f, 0.22f, 0.28f, 1f));
        SetRect(quitButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(250f, 54f));
        quitButton.onClick.AddListener(QuitGame);

        UpdateMusicValueText();
        return root;
    }

    private Slider CreateSlider(Transform parent)
    {
        var sliderGo = new GameObject("MusicSlider", typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(parent, false);
        var slider = sliderGo.GetComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;

        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(sliderGo.transform, false);
        var backgroundRt = background.GetComponent<RectTransform>();
        backgroundRt.anchorMin = new Vector2(0f, 0.38f);
        backgroundRt.anchorMax = new Vector2(1f, 0.62f);
        backgroundRt.offsetMin = Vector2.zero;
        backgroundRt.offsetMax = Vector2.zero;
        var backgroundImage = background.GetComponent<Image>();
        backgroundImage.sprite = _roundedPillSprite;
        backgroundImage.type = Image.Type.Sliced;
        backgroundImage.color = new Color(0.035f, 0.055f, 0.09f, 0.96f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        var fillAreaRt = fillArea.GetComponent<RectTransform>();
        fillAreaRt.anchorMin = new Vector2(0f, 0.38f);
        fillAreaRt.anchorMax = new Vector2(1f, 0.62f);
        fillAreaRt.offsetMin = new Vector2(8f, 0f);
        fillAreaRt.offsetMax = new Vector2(-8f, 0f);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        var fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        var fillImage = fill.GetComponent<Image>();
        fillImage.sprite = _roundedPillSprite;
        fillImage.type = Image.Type.Sliced;
        fillImage.color = new Color(0.18f, 0.58f, 1f);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGo.transform, false);
        var handleAreaRt = handleArea.GetComponent<RectTransform>();
        handleAreaRt.anchorMin = Vector2.zero;
        handleAreaRt.anchorMax = Vector2.one;
        handleAreaRt.offsetMin = new Vector2(8f, 0f);
        handleAreaRt.offsetMax = new Vector2(-8f, 0f);

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        var handleRt = handle.GetComponent<RectTransform>();
        handleRt.anchorMin = new Vector2(0f, 0.5f);
        handleRt.anchorMax = new Vector2(0f, 0.5f);
        handleRt.pivot = new Vector2(0.5f, 0.5f);
        handleRt.sizeDelta = new Vector2(32f, 32f);
        var handleImage = handle.GetComponent<Image>();
        handleImage.sprite = _softCircleSprite;
        handleImage.color = new Color(0.82f, 0.96f, 1f, 1f);
        var handleShadow = handle.AddComponent<Shadow>();
        handleShadow.effectColor = new Color(0.1f, 0.65f, 1f, 0.5f);
        handleShadow.effectDistance = new Vector2(0f, 0f);

        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handleImage;
        return slider;
    }

    private void OnMusicSliderChanged(float value)
    {
        SetMusicVolume(value, false);
    }

    private void UpdateMusicValueText()
    {
        if (_musicValueText != null)
            _musicValueText.text = $"{Mathf.RoundToInt(_musicVolume * 100f)}%";
    }

    public void OpenSettings()
    {
        EnsureSettingsUI();
        if (_settingsRoot == null) return;

        PlayUiClick();
        if (_musicSlider != null)
            _musicSlider.SetValueWithoutNotify(_musicVolume);
        UpdateMusicValueText();

        if (_settingsTransition != null)
            StopCoroutine(_settingsTransition);

        _settingsRoot.SetActive(true);
        _settingsRoot.transform.SetAsLastSibling();
        if (_settingsButton != null)
            _settingsButton.SetActive(false);

        if (_settingsCanvasGroup != null)
        {
            _settingsCanvasGroup.alpha = 0f;
            _settingsCanvasGroup.interactable = false;
            _settingsCanvasGroup.blocksRaycasts = true;
        }
        if (_settingsPanelRect != null)
            _settingsPanelRect.localScale = Vector3.one * 0.94f;

        _settingsTransition = StartCoroutine(AnimateSettingsPanel(true));
    }

    public void CloseSettings()
    {
        if (_settingsRoot == null || !_settingsRoot.activeSelf) return;

        PlayUiClick();
        SaveSettings();

        if (_settingsTransition != null)
            StopCoroutine(_settingsTransition);
        if (_settingsCanvasGroup != null)
            _settingsCanvasGroup.interactable = false;

        _settingsTransition = StartCoroutine(AnimateSettingsPanel(false));
    }

    public void QuitGame()
    {
        PlayUiClick();
        SaveSettings();
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private IEnumerator AnimateSettingsPanel(bool opening)
    {
        float duration = opening ? 0.18f : 0.12f;
        float elapsed = 0f;
        float startAlpha = _settingsCanvasGroup != null ? _settingsCanvasGroup.alpha : opening ? 0f : 1f;
        float targetAlpha = opening ? 1f : 0f;
        Vector3 startScale = _settingsPanelRect != null ? _settingsPanelRect.localScale : Vector3.one;
        Vector3 targetScale = opening ? Vector3.one : Vector3.one * 0.97f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            if (_settingsCanvasGroup != null)
                _settingsCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, eased);
            if (_settingsPanelRect != null)
                _settingsPanelRect.localScale = Vector3.LerpUnclamped(startScale, targetScale, eased);
            yield return null;
        }

        if (_settingsCanvasGroup != null)
        {
            _settingsCanvasGroup.alpha = targetAlpha;
            _settingsCanvasGroup.interactable = opening;
            _settingsCanvasGroup.blocksRaycasts = opening;
        }
        if (_settingsPanelRect != null)
            _settingsPanelRect.localScale = targetScale;

        if (!opening)
        {
            _settingsRoot.SetActive(false);
            if (_settingsButton != null)
                _settingsButton.SetActive(true);
        }

        _settingsTransition = null;
    }

    private void PlayUiClick()
    {
        if (uiClickClip != null)
            PlaySfx(uiClickClip, uiClickVolume, Random.Range(0.98f, 1.03f));
    }

    private static Text CreateText(
        string name,
        Transform parent,
        string content,
        int fontSize,
        FontStyle fontStyle,
        TextAnchor alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.color = new Color(0.92f, 0.94f, 0.98f);
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;

        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.42f);
        shadow.effectDistance = new Vector2(1f, -1f);
        return text;
    }

    private Button CreateButton(
        string name,
        Transform parent,
        string label,
        Color normalColor,
        Color highlightedColor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var image = go.GetComponent<Image>();
        image.sprite = _roundedSurfaceSprite;
        image.type = Image.Type.Sliced;
        image.color = normalColor;

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;

        var colors = button.colors;
        colors.normalColor = normalColor;
        colors.highlightedColor = highlightedColor;
        colors.pressedColor = highlightedColor * 0.8f;
        colors.selectedColor = highlightedColor;
        colors.disabledColor = new Color(0.25f, 0.25f, 0.3f, 0.5f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.42f);
        shadow.effectDistance = new Vector2(0f, -3f);

        Text text = CreateText("Label", go.transform, label, 22, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        return button;
    }

    private static void SetRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 sizeDelta)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }

    private static Sprite CreateRoundedGradientSprite(
        int width,
        int height,
        int radius,
        int border,
        bool addGlassHighlight)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = "Generated_RoundedGlass";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float coverage = RoundedRectCoverage(x, y, width, height, radius);
                float vertical = y / (float)(height - 1);
                float noise = Mathf.PerlinNoise((x + 17) * 0.061f, (y + 31) * 0.061f) - 0.5f;
                float value = Mathf.Lerp(0.70f, 0.96f, vertical) + noise * 0.035f;

                if (addGlassHighlight)
                {
                    float diagonal = Mathf.Clamp01(1f - Mathf.Abs((x / (float)width) + (y / (float)height) - 0.72f) * 3.2f);
                    value += diagonal * 0.08f;
                }

                float edge = Mathf.Clamp01(1f - Mathf.Abs(coverage - 0.72f) * 3f);
                value += edge * 0.10f;
                value = Mathf.Clamp01(value);

                byte v = (byte)Mathf.RoundToInt(value * 255f);
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(coverage) * 255f);
                pixels[y * width + x] = new Color32(v, v, v, a);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
    }

    private static Sprite CreateSoftCircleSprite(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "Generated_SoftCircle";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        float radius = size * 0.48f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = Mathf.Clamp01(1f - Mathf.SmoothStep(radius * 0.62f, radius, distance));
                float rim = Mathf.Clamp01(1f - Mathf.Abs(distance - radius * 0.74f) * 6f);
                byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.74f + rim * 0.26f) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255,
                    (byte)Mathf.RoundToInt(alpha * 255f));
                if (alpha > 0f)
                    pixels[y * size + x].r = value;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite CreateGridSprite(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "Generated_UI_Grid";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Repeat;

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool line = x < 1 || y < 1;
                bool cross = (x > size / 2 && x < size / 2 + 1) || (y > size / 2 && y < size / 2 + 1);
                float alpha = line ? 0.28f : cross ? 0.08f : 0f;
                pixels[y * size + x] = new Color32(255, 255, 255,
                    (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite CreateSheenSprite(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = "Generated_GlassSheen";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float diagonal = Mathf.Abs((x / (float)width) + (y / (float)height) - 0.86f);
                float band = Mathf.Clamp01(1f - diagonal * 4.2f);
                float fade = Mathf.SmoothStep(0f, 1f, y / (float)height);
                pixels[y * width + x] = new Color32(255, 255, 255,
                    (byte)Mathf.RoundToInt(band * fade * 0.20f * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
    }

    private static float RoundedRectCoverage(int x, int y, int width, int height, int radius)
    {
        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float px = Mathf.Abs(x + 0.5f - halfW) - (halfW - radius);
        float py = Mathf.Abs(y + 0.5f - halfH) - (halfH - radius);
        float outsideX = Mathf.Max(px, 0f);
        float outsideY = Mathf.Max(py, 0f);
        float signedDistance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY)
                               + Mathf.Min(Mathf.Max(px, py), 0f)
                               - radius;
        return Mathf.Clamp01(0.5f - signedDistance);
    }
}
