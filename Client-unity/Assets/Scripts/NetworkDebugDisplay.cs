using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// 网络调试覆盖层。显示实时连接状态、RTT估算、重连信息。
/// 挂载到任意 Canvas 下即可。按 F3 切换显示/隐藏。
/// </summary>
public class NetworkDebugDisplay : MonoBehaviour
{
    [Header("显示设置")]
    public bool showByDefault = true;
    public KeyCode toggleKey = KeyCode.F3;
    public TextAnchor anchor = TextAnchor.UpperLeft;
    public Vector2 offset = new Vector2(10, 10);
    public int fontSize = 14;
    public Color textColor = Color.white;
    public Color goodColor = Color.green;
    public Color warnColor = Color.yellow;
    public Color badColor = Color.red;
    public float bgAlpha = 0.6f;

    private bool _visible;
    private string _displayText = "";
    private Rect _displayRect;
    private GUIStyle _style;
    private GUIStyle _bgStyle;

    // RTT 估算（基于 UpdatePlayerDir reducer 往返时间）
    private float _estimatedRtt = 0f;

    void Start()
    {
        _visible = showByDefault;
        _displayRect = new Rect(offset.x, offset.y, 320, 200);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            _visible = !_visible;
    }

    void OnGUI()
    {
        if (!_visible) return;

        // 延迟创建 GUIStyle（OnGUI 中访问 GUI.skin 才安全）
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label);
            _style.fontSize = fontSize;
            _style.richText = true;
        }
        if (_bgStyle == null)
        {
            _bgStyle = new GUIStyle(GUI.skin.box);
            var bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0, 0, 0, bgAlpha));
            bgTex.Apply();
            _bgStyle.normal.background = bgTex;
        }

        var net = SpacetimeDBNetworkManager.Instance;
        if (net == null)
        {
            _displayText = "<color=red>SpacetimeDBNetworkManager 未找到</color>";
        }
        else
        {
            // 连接状态
            string statusColor = net.IsConnected ? "green" : "red";
            string statusText = net.IsConnected ? "CONNECTED" : "DISCONNECTED";

            // 模式
            string modeStr = net.connectionMode switch
            {
                SpacetimeDBNetworkManager.ConnectionMode.Local => "本机",
                SpacetimeDBNetworkManager.ConnectionMode.LAN => "局域网/Tunnel",
                SpacetimeDBNetworkManager.ConnectionMode.Cloud => "云端",
                _ => "未知"
            };

            // RTT 颜色
            string rttColor = _estimatedRtt <= 80 ? "green" : _estimatedRtt <= 200 ? "yellow" : "red";
            string rttText = _estimatedRtt > 0 ? $"{_estimatedRtt:F0}ms" : "测量中...";

            _displayText = $"<b>BallBattle-4 网络状态</b>\n" +
                          $"══════════════════\n" +
                          $"状态: <color={statusColor}>{statusText}</color>\n" +
                          $"模式: {modeStr}\n" +
                          $"地址: {net.ActiveUri}\n" +
                          $"模块: {net.ActiveModuleName}\n" +
                          $"RTT: <color={rttColor}>{rttText}</color>\n" +
                          $"══════════════════\n" +
                          $"<size=10>按 {toggleKey} 切换显示</size>";
        }

        GUI.Box(_displayRect, "", _bgStyle);
        GUI.Label(new Rect(_displayRect.x + 6, _displayRect.y + 4,
            _displayRect.width - 12, _displayRect.height - 8), _displayText, _style);
    }

    /// <summary>
    /// 由外部调用：记录一次 RTT 样本（毫秒）。
    /// PlayerInputController 可在发送方向时打时间戳，
    /// 在 CircleController 收到服务端位置更新时回算延迟。
    /// </summary>
    public void RecordRttSample(float rttMs)
    {
        // 指数移动平均平滑 RTT
        if (_estimatedRtt <= 0)
            _estimatedRtt = rttMs;
        else
            _estimatedRtt = _estimatedRtt * 0.8f + rttMs * 0.2f;
    }
}
