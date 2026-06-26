using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// 游戏启动时自动从 GitHub Gist 获取最新的 Cloudflare Tunnel 服务器地址，
/// 并自动填入 IP 输入框，省去手动复制粘贴的麻烦。
///
/// 挂载到 LobbyCanvas 或任意 GameObject 上即可。
/// </summary>
public class ServerAddressAutoFetcher : MonoBehaviour
{
    [Header("Gist 配置")]
    [Tooltip("GitHub Gist raw URL，返回纯文本的服务器地址")]
    public string gistUrl = "https://gist.githubusercontent.com/raw/f4e5a31c5d3d9622ddb92bc44101177d/tunnel-url";

    [Header("UI 引用（可选，留空则自动查找）")]
    [Tooltip("IP 输入框")]
    public InputField ipInput;

    [Tooltip("端口输入框")]
    public InputField portInput;

    [Tooltip("状态提示文本")]
    public Text statusText;

    [Header("行为配置")]
    [Tooltip("获取成功后是否自动切换到 LAN 模式")]
    public bool autoSwitchToLAN = true;

    [Tooltip("获取成功后是否自动点击连接按钮")]
    public bool autoConnect = false;

    [Tooltip("是否在启动时自动获取")]
    public bool fetchOnStart = true;

    // ---- 引用缓存 ----
    private LobbyUIController _lobbyUI;
    private SpacetimeDBNetworkManager _networkManager;

    void Start()
    {
        if (fetchOnStart)
            StartCoroutine(FetchServerAddress());
    }

    /// <summary>
    /// 手动触发获取（可从外部调用）
    /// </summary>
    public void Refresh()
    {
        StartCoroutine(FetchServerAddress());
    }

    private IEnumerator FetchServerAddress()
    {
        // 自动查找引用
        EnsureReferences();

        if (statusText != null)
            statusText.text = "正在获取服务器地址...";

        Debug.Log($"[AutoFetcher] Fetching server URL from: {gistUrl}");

        using (var request = UnityWebRequest.Get(gistUrl))
        {
            // 禁用缓存，确保每次获取最新地址
            request.SetRequestHeader("Cache-Control", "no-cache, no-store");
            request.timeout = 10;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AutoFetcher] 获取失败: {request.error}");
                if (statusText != null)
                    statusText.text = "获取服务器地址失败，请手动输入IP";
                yield break;
            }

            string response = request.downloadHandler.text.Trim();
            Debug.Log($"[AutoFetcher] 获取到: {response}");

            // 解析 URL：提取域名，去掉协议和路径
            string host;
            int port;
            if (!TryParseUrl(response, out host, out port))
            {
                Debug.LogWarning($"[AutoFetcher] URL 格式无效: {response}");
                if (statusText != null)
                    statusText.text = "服务器地址格式错误";
                yield break;
            }

            // 填入 IP 输入框
            if (ipInput != null)
                ipInput.text = host;

            // 填入端口输入框
            if (portInput != null)
                portInput.text = port.ToString();

            Debug.Log($"[AutoFetcher] 已填入 -> IP: {host}, Port: {port}");

            if (statusText != null)
                statusText.text = $"服务器地址已加载: {host}";

            // 可选：自动切换到 LAN 模式
            if (autoSwitchToLAN && _lobbyUI != null)
            {
                // SelectMode(1) = LAN 模式（通过反射或公开方法调用）
                StartCoroutine(AutoSelectLANMode());
            }

            // 可选：自动连接
            if (autoConnect && _networkManager != null)
            {
                yield return new WaitForSeconds(0.5f);
                _networkManager.ConnectToLAN(host, port);
            }
        }
    }

    /// <summary>
    /// 自动切换到 LAN 模式（延迟一帧以确保 UI 初始化完毕）
    /// </summary>
    private IEnumerator AutoSelectLANMode()
    {
        yield return null; // 等一帧

        // 通过 LobbyUIController 的模式按钮触发 LAN 模式
        // 按钮索引：0=本机, 1=LAN, 2=云端
        if (_lobbyUI != null)
        {
            // 模拟点击 LAN 按钮
            if (_lobbyUI.modeLANBtn != null)
                _lobbyUI.modeLANBtn.onClick.Invoke();
            
            Debug.Log("[AutoFetcher] 已自动切换到 LAN 模式");
        }
    }

    /// <summary>
    /// 解析 URL：提取主机名和端口
    /// 支持格式：
    ///   https://xxx.trycloudflare.com         -> host=xxx.trycloudflare.com, port=443
    ///   http://192.168.1.1:3000               -> host=192.168.1.1, port=3000
    ///   xxx.trycloudflare.com                 -> host=xxx.trycloudflare.com, port=443
    /// </summary>
    private bool TryParseUrl(string url, out string host, out int port)
    {
        host = "";
        port = 443; // 默认 HTTPS 端口

        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            // 如果没有协议前缀，加上 https://
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            Uri uri = new Uri(url);
            host = uri.Host;

            if (uri.Port != 443 && uri.Port != 80)
                port = uri.Port;
            else if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                port = 443;
            else
                port = 80;

            return !string.IsNullOrEmpty(host);
        }
        catch (UriFormatException)
        {
            // 如果 URI 解析失败，尝试直接作为 hostname 处理
            string cleaned = url
                .Replace("https://", "")
                .Replace("http://", "")
                .Split('/')[0]     // 去掉路径
                .Trim();

            // 检查是否有端口号
            int colonIndex = cleaned.LastIndexOf(':');
            if (colonIndex > 0)
            {
                host = cleaned.Substring(0, colonIndex);
                if (!int.TryParse(cleaned.Substring(colonIndex + 1), out port))
                    port = 443;
            }
            else
            {
                host = cleaned;
                port = 443;
            }

            return !string.IsNullOrEmpty(host);
        }
    }

    /// <summary>
    /// 自动查找场景中的引用
    /// </summary>
    private void EnsureReferences()
    {
        if (_networkManager == null)
            _networkManager = SpacetimeDBNetworkManager.Instance;

        if (_lobbyUI == null)
            _lobbyUI = FindObjectOfType<LobbyUIController>();

        if (ipInput == null && _lobbyUI != null && _lobbyUI.ipInput != null)
            ipInput = _lobbyUI.ipInput;

        if (portInput == null && _lobbyUI != null && _lobbyUI.portInput != null)
            portInput = _lobbyUI.portInput;

        if (statusText == null && _lobbyUI != null && _lobbyUI.statusText != null)
            statusText = _lobbyUI.statusText;
    }
}
