using SpacetimeDB;
using SpacetimeDB.Internal.TableHandles;
using System;
using System.Diagnostics.Contracts;

public static partial class Module
{
    private static int WORLD_SIZE = 50;
    // 将质量相关的常量改为 float 类型
    private static float PRIMARY_PLAYER_MASS = 5.0f;
    private static int TARGET_FOOD_COUNT = 200;
    private static float FOOD_MASS = 2.0f;
    private static int START_PLAYER_SPEED = 13;
    private static float MIN_SPLIT_MASS = 10.0f; // 允许分裂的最小质量
    //合并配置
    private static int MERGE_CHECK_INTERVAL = 1500;
    private static float BASE_MERGE_SEC = 2.0f;    //基础贴合等待2秒
    private static float SQRT_DELAY_COEFF = 0.8f;// 平方根系数
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
        public float mass;      // 已经为 float
        public DbVector2 position;
    }
    [Type]
    public partial struct DbVector2
    {
        public float x;
        public float y;
        public DbVector2(float x, float y)
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
        // 新增：贴合开始时间，0=未贴合
        public double touchStartMs;
        // true=进入合并动画阶段，等待客户端动画完成再删
        public bool isMerging;
        public bool isSplitting; //新增：正在分裂动画标记
        public int splitFromEntityId; //新增：如果正在分裂，记录来源球的entity_id
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
        public DbVector2 dir;
    }
    [Reducer]
    public static void UpdatePlayerDir(ReducerContext context, DbVector2 dir)
    {
        var player = context.Db.logged_in_player.Identity.Find(context.Sender)??throw new Exception("未找到对应的玩家");
        player.dir = dir;
        context.Db.logged_in_player.Identity.Update(player);
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
            player_id = player.player_id,
            touchStartMs = 0,
            isMerging = false,
            isSplitting = false,
            splitFromEntityId = 0
        });

    }
    [Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext context)
    {
        context.Db.spawn_food_timer.Insert(new SpawnFoodTimer
        {
            schedule_at = new ScheduleAt.Interval(TimeSpan.FromMilliseconds(1000))
        });
        context.Db.move_all_player.Insert(new MoveAllPlayerTimer
        {
            schedule_at = new ScheduleAt.Interval(TimeSpan.FromMilliseconds(50))//1秒钟调用20次
        });
        // 自动合并开关：注释下面一行 = 关闭全局自动合并
        context.Db.merge_player_timer.Insert(new MergePlayerTimer
        {
            schedule_at = new ScheduleAt.Interval(TimeSpan.FromMilliseconds(MERGE_CHECK_INTERVAL))
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
        int foodCount = (int)context.Db.food.Count;
        if (foodCount < TARGET_FOOD_COUNT)
        {
            var x = context.Rng.Next(1, WORLD_SIZE - 1);
            var y = context.Rng.Next(1, WORLD_SIZE - 1);
            // 生成 1.0f 到 2.0f 之间的浮点数
            float randomFloat = (float)context.Rng.NextDouble(); // 0.0 到 1.0 之间
            float foodCurrentMass = 1.0f + randomFloat;

            var entity = context.Db.entity.Insert(new Entity
            {
                mass = foodCurrentMass,
                position = new DbVector2(x, y)
            });
            context.Db.food.Insert(new Food
            {
                entity_id = entity.id
            });
            foodCount++;
        }
    }
    [Reducer]
    public static void MoveAllPlayer(ReducerContext context, MoveAllPlayerTimer timer)
    {
        // 第一阶段：移动所有玩家球
        foreach(var circle in context.Db.circle.Iter())
        {
            var entityNullable = context.Db.entity.id.Find(circle.entity_id);
            if (entityNullable == null) continue; // 安全检查

            var playerNullable = context.Db.logged_in_player.player_id.Find(circle.player_id);
            if (playerNullable == null) continue;

            // 提取结构体进行修改
            var entity = entityNullable.Value;
            var player = playerNullable.Value;

            // 质量减速系数
            float speedScale = 1f / (entity.mass * 0.06f + 1f);
            float moveStep = 0.05f * START_PLAYER_SPEED * speedScale;
            entity.position.x += player.dir.x * moveStep;
            entity.position.y += player.dir.y * moveStep;

            // 【边界钳制】防止球移出世界边界，根除客户端穿墙抖动
            ClampEntityToBounds(ref entity);

            context.Db.entity.id.Update(entity);
        }

        // 第二阶段：检测吞噬并收集要删除的 ID 以及要增加的质量
        var massGains = new System.Collections.Generic.Dictionary<int, float>();
        var entitiesToDelete = new System.Collections.Generic.HashSet<int>();

        // 【性能优化】将 playerBalls 字典构建提前到循环外，只构建一次
        // 原代码在 circleA 循环内重复构建，导致O(n?)无效开销
        Dictionary<int, List<(Entity entity, int eid)>> playerBalls = new Dictionary<int, List<(Entity, int)>>();
        foreach (var cir in context.Db.circle.Iter())
        {
            var ent = context.Db.entity.id.Find(cir.entity_id);
            if (ent == null) continue;
            if (!playerBalls.ContainsKey(cir.player_id))
                playerBalls[cir.player_id] = new List<(Entity, int)>();
            playerBalls[cir.player_id].Add((ent.Value, cir.entity_id));
        }

        // 重新遍历所有的 玩家球(circle) 去检测覆盖
        foreach(var circleA in context.Db.circle.Iter())
        {
            var entityANullable = context.Db.entity.id.Find(circleA.entity_id);
            if (entityANullable == null || entitiesToDelete.Contains(circleA.entity_id)) continue;
            
            var entityA = entityANullable.Value;

            foreach(var entityB in context.Db.entity.Iter())
            {
                if (entityA.id == entityB.id) continue;
                if (entitiesToDelete.Contains(entityB.id)) continue; 

                // 防止同一个人自己的球之间互相吃（如果有分裂功能的话）
                var circleBNullable = context.Db.circle.entity_id.Find(entityB.id);
                if (circleBNullable != null && circleBNullable.Value.player_id == circleA.player_id) continue; 

                // A是玩家球，判断是否重叠覆盖B
                if (IsOverLapping(entityA, entityB))
                {
                    bool isFood = context.Db.food.entity_id.Find(entityB.id) != null;
                    bool isOtherPlayer = circleBNullable != null;

                    if (isFood || (isOtherPlayer && entityA.mass > entityB.mass))
                    {
                        // 标记 B 被吃掉
                        entitiesToDelete.Add(entityB.id);

                        // 记录A应该增加的质量
                        if (!massGains.ContainsKey(entityA.id))
                            massGains[entityA.id] = 0;
                        
                        massGains[entityA.id] += entityB.mass;
                    }
                }
            }
        }

        //===== 静止自动向中心聚拢（相切停移，无挤压）=====
        // 【性能优化】移到 circleA 循环外，只执行一次
        foreach (var kv in playerBalls)
        {
            int pid = kv.Key;
            var ballList = kv.Value;
            if (ballList.Count <= 1) continue;

            var p = context.Db.logged_in_player.player_id.Find(pid);
            if (p == null) continue;
            bool noInput = MathF.Abs(p.Value.dir.x) < 0.01f && MathF.Abs(p.Value.dir.y) < 0.01f;
            if (!noInput) continue; //移动跳过聚拢

            //计算群体中心
            float cenX = 0, cenY = 0;
            foreach (var b in ballList)
            {
                cenX += b.entity.position.x;
                cenY += b.entity.position.y;
            }
            cenX /= ballList.Count;
            cenY /= ballList.Count;

            //逐个球向中心移动
            for (int i = 0; i < ballList.Count; i++)
            {
                var item = ballList[i];
                Entity ent = item.entity;
                float speedScale = 1f / (ent.mass * 0.05f + 1f);
                float pull = 0.018f * speedScale;

                float dx = cenX - ent.position.x;
                float dy = cenY - ent.position.y;

                //计算本球半径
                float r1 = MassToDiameter(ent.mass) / 2f;
                bool canMove = true;

                //遍历同玩家其他球，判断距离，贴边就禁止移动防挤压
                for (int j = 0; j < ballList.Count; j++)
                {
                    if (i == j) continue;
                    var other = ballList[j];
                    float r2 = MassToDiameter(other.entity.mass) / 2f;
                    float dX = ent.position.x - other.entity.position.x;
                    float dY = ent.position.y - other.entity.position.y;
                    float dist = MathF.Sqrt(dX * dX + dY * dY);
                    float minDist = r1 + r2;

                    //距离≤相切距离，停止靠拢
                    if (dist <= minDist + 0.02f)
                    {
                        canMove = false;
                        break;
                    }
                }

                //没贴边才继续向中心移动
                if (canMove)
                {
                    ent.position.x += dx * pull;
                    ent.position.y += dy * pull;
                    ClampEntityToBounds(ref ent);
                    context.Db.entity.id.Update(ent);
                }
            }
        }

        // 第三阶段：统一处理数据的 更新 和 删除
        // 增重
        foreach(var kvp in massGains)
        {
            var entityToGainNullable = context.Db.entity.id.Find(kvp.Key);
            if (entityToGainNullable != null)
            {
                var entityToGain = entityToGainNullable.Value;
                entityToGain.mass += kvp.Value;
                context.Db.entity.id.Update(entityToGain);
            }
        }

        // 删除被吃掉的实体
        foreach(var deadId in entitiesToDelete)
        {
            // 如果它是食物，删食物表
            if (context.Db.food.entity_id.Find(deadId) != null)
            {
                context.Db.food.entity_id.Delete(deadId);
            }
            // 如果它是玩家，删圆圈表
            if (context.Db.circle.entity_id.Find(deadId) != null)
            {
                context.Db.circle.entity_id.Delete(deadId);
            }
            // 从基础实体表删除
            context.Db.entity.id.Delete(deadId);
        }
    }
    public static bool IsOverLapping(Entity entityA, Entity entityB)
    {
        // 计算 X 和 Y 的差值
        float dx = entityA.position.x - entityB.position.x;
        float dy = entityA.position.y - entityB.position.y;
        
        // 计算圆心之间的距离平方
        float distanceSquared = dx * dx + dy * dy;

        // 根据公式：直径 = MathF.Sqrt(mass) / 2f
        // 因此半径 = MathF.Sqrt(mass) / 4f
        // 这里以 entityA (试图吃掉对方的球) 的半径为判定范围
        float radiusA = MathF.Sqrt(entityA.mass) / 4f;
        
        // 半径的平方
        float radiusASquared = radiusA * radiusA;

        // 如果中心距离的平方小于（或者等于）A半径的平方，说明 B 的中心进入了 A 的内部
        return distanceSquared <= radiusASquared;
    }
    public static float MassToDiameter(float mass)
    {
        // 我们在原有公式的基础上，乘以一个可配置的视觉缩放系数
        return (MathF.Sqrt(mass) / 2f) ;
    }

    [Table(Name = "move_all_player", Scheduled = nameof(MoveAllPlayer), ScheduledAt = nameof(schedule_at))]
    public partial struct MoveAllPlayerTimer
    {
        [PrimaryKey, AutoInc]
        public ulong scheduled_id;
        public ScheduleAt schedule_at;
    }


    // 定时器表，记录何时需要生成食物
    [Table(Name = "spawn_food_timer", Scheduled = nameof(SpawnFood),ScheduledAt = nameof(schedule_at))]
    public partial struct SpawnFoodTimer
    {
        [PrimaryKey,AutoInc]
        public ulong scheduled_id;
        public ScheduleAt schedule_at;
    }
    [Table(Name = "merge_player_timer", Scheduled = nameof(MergePlayerCheck), ScheduledAt = nameof(schedule_at))]
    public partial struct MergePlayerTimer
    {
        [PrimaryKey, AutoInc]
        public ulong scheduled_id;
        public ScheduleAt schedule_at;
    }

    [Reducer]
    public static void SplitPlayer(ReducerContext context)
    {
        var player = context.Db.logged_in_player.Identity.Find(context.Sender) ?? throw new Exception("未找到对应玩家");

        // 将能够查询到的当前玩家所有的 Circle 放进列表
        var playerCircles = new System.Collections.Generic.List<Circle>();
        foreach (var circle in context.Db.circle.player_id.Filter(player.player_id))
        {
            playerCircles.Add(circle);
        }

        foreach (var circle in playerCircles)
        {
            var entityNullable = context.Db.entity.id.Find(circle.entity_id);
            if (entityNullable == null) continue;
            var entity = entityNullable.Value;

            // 只有质量大于指定阈值的球才能分裂
            if (entity.mass >= MIN_SPLIT_MASS)
            {
                float halfMass = entity.mass / 2f;
                
                // 1. 将旧实体质量减半
                entity.mass = halfMass;
                context.Db.entity.id.Update(entity);

                // 2. 根据玩家移动方向，计算新实体的生成偏移位置
                float dirX = player.dir.x;
                float dirY = player.dir.y;
                
                // 如果当前没有移动方向，默认向右分裂
                if (dirX == 0 && dirY == 0) dirX = 1f;

                // 归一化方向
                float length = MathF.Sqrt(dirX * dirX + dirY * dirY);
                dirX /= length;
                dirY /= length;

                // 新球在母球前方的边缘生成：通过质量推算半斤加上一定的间隙
                float offset = MassToDiameter(halfMass) + 1.0f;

                var newEntity = context.Db.entity.Insert(new Entity
                {
                    mass = halfMass,
                    position = new DbVector2(entity.position.x + dirX * offset, entity.position.y + dirY * offset)
                });

                // 3. 将新实体与玩家绑定
                context.Db.circle.Insert(new Circle
                {
                    entity_id = newEntity.id,
                    player_id = player.player_id,
                    touchStartMs = 0,
                    isMerging = false,
                    isSplitting = true, // 标记为正在分裂动画
                    splitFromEntityId = entity.id // 记录来源球的entity_id
                });
            }
        }
    }
    [Reducer]
    public static void MergePlayerCheck(ReducerContext context, MergePlayerTimer timer)
    {
        double now = context.Timestamp.ToTimeSpanSinceUnixEpoch().TotalMilliseconds;;
        foreach (var player in context.Db.logged_in_player.Iter())
        {
            var list = new List<Circle>();
            foreach (var c in context.Db.circle.player_id.Filter(player.player_id)) list.Add(c);
            if (list.Count <= 1) continue;

            bool staticState = MathF.Abs(player.dir.x) < 0.01f && MathF.Abs(player.dir.y) < 0.01f;
            if (!staticState) continue;//移动直接跳过合并

            //缓存Circle+Entity
            var circleEntDic = new Dictionary<Circle, Entity>();
            foreach (var cir in list)
                circleEntDic[cir] = context.Db.entity.id.Find(cir.entity_id).Value;

            List<Circle> circleList = circleEntDic.Keys.ToList();
            //更新贴合标记与计时
            for (int a = 0; a < circleList.Count; a++)
            {
                Circle ca = circleList[a];
                Entity ea = circleEntDic[ca];
                bool isTouch = false;

                for (int b = 0; b < circleList.Count; b++)
                {
                    if (a == b) continue;
                    Entity eb = circleEntDic[circleList[b]];
                    float ra = MassToDiameter(ea.mass) / 2;
                    float rb = MassToDiameter(eb.mass) / 2;
                    float dx = ea.position.x - eb.position.x;
                    float dy = ea.position.y - eb.position.y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    float minD = ra + rb;

                    if (dist <= minD + 0.03f)
                    {
                        isTouch = true;
                        break;
                    }
                }

                Circle editCircle = ca;
                if (isTouch)
                {
                    if (editCircle.touchStartMs == 0)
                        editCircle.touchStartMs = now;
                }
                else
                {
                    editCircle.touchStartMs = 0;
                }
                context.Db.circle.entity_id.Update(editCircle);
            }

            //合并
            var mainC = list[0];
            var mainE = circleEntDic[mainC];
            float total = mainE.mass;

            for (int i = 1; i < list.Count; i++)
            {
                var subC = list[i];
                var subE = circleEntDic[subC];
                if (subC.touchStartMs <= 0) continue;

                //非线性延时公式
                float needSec = BASE_MERGE_SEC + MathF.Sqrt(subE.mass) * SQRT_DELAY_COEFF;
                long needMs = (long)(needSec * 1000);
                double passMs = now - subC.touchStartMs;

                if (passMs >= needMs)
                {
                    total += subE.mass;
                    Circle markCircle = subC;
                    markCircle.isMerging = true;
                    context.Db.circle.entity_id.Update(markCircle);
                }
                //本轮结束后，统一删除所有标记isMerging的球
                var cir = list[i];
                if (cir.isMerging)
                {
                    context.Db.circle.entity_id.Delete(cir.entity_id);
                    context.Db.entity.id.Delete(cir.entity_id);
                }
                
            }
            mainE.mass = total;
            //重置主球计时
            mainC.touchStartMs = 0;
            context.Db.circle.entity_id.Update(mainC);
            context.Db.entity.id.Update(mainE);
        }
    }
    [Reducer]
    public static void FinishSplitAnimation(ReducerContext context, int entity_id)
    {
        // 获取当前玩家
        var player = context.Db.logged_in_player.Identity.Find(context.Sender) ?? throw new Exception("未找到对应玩家");

        // 查找对应的玩家球
        var circleNullable = context.Db.circle.entity_id.Find(entity_id);
        if (circleNullable != null)
        {
            var circle = circleNullable.Value;
            
            // 确保该球属于发送请求的玩家，并且确实处于分裂状态
            if (circle.player_id == player.player_id && circle.isSplitting)
            {
                circle.isSplitting = false;
                context.Db.circle.entity_id.Update(circle);
            }
        }
    }

    // SyncBallPos 已移除：服务端权威位置由 MoveAllPlayer 统一更新，
    // 客户端不再回传位置偏差。边界钳制由 ClampEntityToBounds 保证。

    /// <summary>
    /// 将实体位置钳制在世界边界内，确保球不越界。
    /// 这是根除客户端穿墙/抖动的关键：服务端保证位置永远合法，
    /// 客户端SmoothDamp到合法位置就不会触发物理碰撞。
    /// </summary>
    private static void ClampEntityToBounds(ref Entity entity)
    {
        float radius = MassToDiameter(entity.mass) / 2f;
        // 安全余量：0.01f防止浮点精度导致的边界外
        float margin = radius + 0.01f;
        entity.position.x = Math.Clamp(entity.position.x, margin, WORLD_SIZE - margin);
        entity.position.y = Math.Clamp(entity.position.y, margin, WORLD_SIZE - margin);
    }
}
