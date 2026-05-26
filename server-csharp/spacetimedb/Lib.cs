using SpacetimeDB;
using SpacetimeDB.Internal.TableHandles;
using System.Diagnostics.Contracts;

public static partial class Module
{
    private static int WORLD_SIZE = 50;
    private static int PRIMARY_PLAYER_MASS = 5;
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
        public DbVector2(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
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
        [SpacetimeDB.Index.BTree]
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
    [Reducer]
    public static void EnterGame(ReducerContext context,string name) 
    { 
        var player = context.Db.logged_in_player.Identity.Find(context.Sender)??throw new Exception("未找到对应的玩家");
        player.name = name;
        context.Db.logged_in_player.Identity.Update(player);

        var x = context.Rng.Next(1, WORLD_SIZE - 1);
        var y = context.Rng.Next(1, WORLD_SIZE - 1);

        //生成玩家球体
        var entity = context.Db.entity.Insert(new Entity
        {
            mass = PRIMARY_PLAYER_MASS,
            position = new DbVector2(x,y)//调用构造函数创建DbVector2实例
        });
        context.Db.circle.Insert(new Circle
        {
            entity_id = entity.id,//entity_id与Entity表的id相同,玩家球数据
            player_id = player.player_id
        });

    }
    [Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext context)
    {
        context.Db.spawn_food_timer.Insert(new SpawnFoodTimer
        {
            schedule_at = new ScheduleAt.Interval(TimeSpan.FromMilliseconds(1000))
        });
    }

    [Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext context)
    {
        var player = context.Db.logged_out_player.Identity.Find(context.Sender);
        if (player != null)
        {
            Log.Info("玩家已连接过，更新连接状态");
            context.Db.logged_out_player.Identity.Delete(context.Sender);
            context.Db.logged_in_player.Insert(player.Value);
        }
        else { 
            Log.Info("有新客户端连接");
            context.Db.logged_in_player.Insert(new Player
            {
                Identity = context.Sender,
                name = ""
            });
        }
    }
    [Reducer(ReducerKind.ClientDisconnected)]
    public static void ClientDisconnected(ReducerContext context)
    {
        var player = context.Db.logged_in_player.Identity.Find(context.Sender)??throw new Exception("未找到对应的玩家");
        context.Db.logged_in_player.Identity.Delete(context.Sender);
        Log.Info("有客户端断开连接");
        context.Db.logged_out_player.Insert(player);

        foreach (var circle in context.Db.circle.player_id.Filter(player.player_id)) { 
            var entity = context.Db.entity.id.Find(circle.entity_id)??throw new Exception("未找到对应的实体");//依据entity_id找到entity表中的记录，确保存在
            context.Db.circle.entity_id.Delete(circle.entity_id);
            context.Db.entity.id.Delete(entity.id);
            Log.Info($"已删除玩家离线留下的实体: entity_id = {circle.entity_id}");
        }


        //// 收集该玩家对应的所有实体 ID
        //var idsToDelete = new System.Collections.Generic.List<int>();
        //foreach (var circle in context.Db.circle.Iter())
        //{
        //    if (circle.player_id == player.player_id)
        //    {
        //        idsToDelete.Add(circle.entity_id);
        //    }
        //}

        //// 统一进行删除：同时删除 circle 和 entity 表中的数据
        //foreach (var id in idsToDelete)
        //{
        //    context.Db.circle.entity_id.Delete(id);
        //    context.Db.entity.id.Delete(id);
        //    Log.Info($"已删除玩家离线留下的实体: entity_id = {id}");
        //}
    }
    [Reducer]
    public static void SpawnFood(ReducerContext context,SpawnFoodTimer timer)
    {
        //Log.Info("生成食物");
    }
    // 定时器表，记录何时需要生成食物
    [Table(Name = "spawn_food_timer", Scheduled = nameof(SpawnFood),ScheduledAt = nameof(schedule_at))]
    public partial struct SpawnFoodTimer
    {
        [PrimaryKey,AutoInc]
        public ulong scheduled_id;
        public ScheduleAt schedule_at;
    }
}
