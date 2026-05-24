using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SpacetimeDB;
using SpacetimeDB.Types;

public class GameManager : MonoBehaviour
{
    const string ModuleName = "ballbattle4";
    const string ServerUri = "http://127.0.0.1:3000";
    public static DbConnection Conn{ get; private set; }
    // Start is called before the first frame update
    void Start()
    {
        DbConnectionBuilder<DbConnection> builder = DbConnection.Builder();
        builder.WithUri(ServerUri);
        builder.WithModuleName(ModuleName);
        Conn = builder.Build();

    }
    // Update is called once per frame
    void Update()
    {
        
    }
}
