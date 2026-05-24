using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;

public class GameManager : MonoBehaviour
{
    const string ModuleName = "ballbattle4";
    const string ServerUri = "http://127.0.0.1:3000";
    public static DbConnection Conn{ get; private set; }
    // Start is called before the first frame update
    void Start()
    {
        // 必须先初始化 AuthToken，才能安全地访问 AuthToken.Token
        AuthToken.Init();

        DbConnectionBuilder<DbConnection> builder = DbConnection.Builder();
        builder.WithUri(ServerUri);
        builder.WithModuleName(ModuleName);

        builder.OnConnect(HandleConnect);
        // 如果之前已经连接过服务器并且保存了认证令牌，那么在重新连接时可以直接使用这个令牌进行认证，无需再次输入用户名和密码
        if (AuthToken.Token != "")
        {
            builder.WithToken(AuthToken.Token);
        }
        Conn = builder.Build();

    }

    private void HandleConnect(DbConnection conn, Identity identity, string token)
    {
        // 连接成功后会得到一个身份标识（Identity）和一个认证令牌（AuthToken）都是独一无二的身份认证的字符串，可以将它们保存起来以便后续使用
        AuthToken.SaveToken(token);
        print(token);
        print(identity);
    }

    // Update is called once per frame
    void Update()
    {
        // 必须在每帧驱动网络消息，否则任何回调（包含 OnConnect）都不会被触发
        Conn?.FrameTick();
    }
}
