using SpacetimeDB;
using SpacetimeDB.Internal.TableHandles;
using System.Diagnostics.Contracts;

public static partial class Module
{
    [Table(Name ="test_table",Public = true)]
    public partial struct TestTable
    {
        [PrimaryKey, AutoInc]
        public int id;
        public string name;
    }
    [Table(Name = "entity", Public = true)]
    public partial struct Entity
    {
        [PrimaryKey, AutoInc]
        public int id;
        public int mass;
        public DbVector2 position;
    }
    [Type]
    public partial struct DbVector2
    {
        public float x;
        public float y;
    }
    [Table(Name = "food", Public = true)]
    public partial struct Food 
    {
        [PrimaryKey]
        public int entity_id;
    }
    [Table(Name = "circle", Public = true)]
    public partial struct Circle
    {
        [PrimaryKey]
        public int entity_id;
        public int player_id;
    }
    [Table(Name = "logged_in_player", Public = true)]
    [Table(Name = "logged_out_player", Public = true)]
    public partial struct Player
    {
        [PrimaryKey]
        public Identity Identity;
        [AutoInc,Unique]
        public int player_id;//控制多少个球
        public string name;
    }


    [Reducer]
    public static void Reducer1(ReducerContext context)
    {
        //Log.Info("我是一个Reducer，我被调用了！");
        ////context.Db.test_table.Insert(new TestTable { name = "hello" });
        ////foreach (var item in context.Db.test_table.Iter())
        ////{
        ////    Log.Info($"id: {item.id}, name: {item.name}");
        ////}
        //var item = context.Db.test_table.id.Find(2)??throw new Exception("未找到id为2的记录");
        //item.name = "world";
        //context.Db.test_table.id.Update(item);
        //Log.Info($"id: {item.id}, name: {item.name}");
        //context.Db.test_table.id.Delete(1);
    }

    // 重命名为不以 "On" 开头的方法名
    [Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext context)
    {
        //Log.Info("有客户端连接");
    }
}
