using UnityEngine;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using UnityEngine.UI;
using UnityEditor.MemoryProfiler;

public class GameManager : MonoBehaviour
{
    public InputField InputField;
    public GameObject canvasGo;
    public enum Environment
    {
        Local,
        Cloud
    }

    [Header("服务器配置")]
    [Tooltip("选择要连接的服务器环境")]
    public Environment serverEnvironment = Environment.Local;

    // 本地服务器固定地址（使用 HTTP 协议，SDK会内部自动转 WS）
    const string LocalUri = "http://127.0.0.1:3000";

    // ✅ 恢复为 SpacetimeDB 官方唯一合法的测试网入口
    // 千万不要用自己拼接的极长带 Hash 的 wss 域名，系统的 DNS 解析根本不认识它！
    const string CloudUri = "wss://maincloud.spacetimedb.com";

    public static DbConnection Conn { get; private set; }

    void Start()
    {
        AuthToken.Init();

        // 动态选择模块名：本地用ballbattle4，云端用ballbattle4v2
        string moduleName = serverEnvironment == Environment.Cloud ? "ballbattle4v2" : "ballbattle4";

        string activeUri = serverEnvironment == Environment.Cloud ? CloudUri : LocalUri;
        Debug.Log($"[SpacetimeDB] 正在连接到 {serverEnvironment} 服务器: {activeUri} 模块: {moduleName}");

        DbConnectionBuilder<DbConnection> builder = DbConnection.Builder();
        builder.WithUri(activeUri);
        
        // 修复：由于你更新了 CLI 和自动生成的包代码，方法名变回或者被重命名为其他形式。
        // 根据你最新生成的 SpacetimeDBClient.g.cs 的环境基础要求，直接使用 WithModuleName，这是 2.2 版本以及很多版本的向后兼容接口。
        builder.WithDatabaseName(moduleName);

        builder.OnConnect(HandleConnect);
        builder.OnConnectError(HandleConnectError);

        // 已注释本地Token复用，避免本地Token连云端被401拒绝

        Conn = builder.Build();
    }

    private void HandleConnectError(Exception error)
    {
        Debug.LogError($"<color=red>❌ 连接 SpacetimeDB 服务器失败：{error.Message}</color>");
    }

    private void HandleConnect(DbConnection conn, Identity identity, string token)
    {
        Debug.Log("<color=green>✅ 成功连接 SpacetimeDB 服务器！</color>");
        AuthToken.SaveToken(token);
        print(token);
        print(identity);
    }
    public void OnButtonEnterGameClick()
    {
        canvasGo.SetActive(false);
        Conn.Reducers.EnterGame(InputField.text);
    }

    void Update()
    {
        Conn?.FrameTick();
    }

    private void OnDestroy()
    {
        Conn?.Disconnect();
    }
}