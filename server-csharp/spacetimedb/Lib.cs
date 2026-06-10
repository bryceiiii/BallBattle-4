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
    
    // HP 相关常量
    private static float HP_BASE = 50f;          // HP 基础值
    private static float HP_MASS_COEFF = 0.5f;   // HP 随质量增长系数
    private static float HP_MIN_RATIO = 0.3f;    // HP/mass 下限保护比

    // 子弹相关常量
    private static float BULLET_MASS_COST = 0.8f;    // 单发消耗质量
    private static float BULLET_SPEED = 25f;          // 子弹飞行速度
    private static double BULLET_LIFETIME_MS = 3000d; // 子弹存活时间
    private static float BULLET_DAMAGE = 8f;          // 普通子弹伤害
    private static float BULLET_COOLDOWN_SEC = 0.3f;  // 射击冷却
    private static int BULLET_MAX_PER_PLAYER = 5;     // 每玩家同时存在子弹上限
    private static float MIN_SHOOT_MASS = 3.0f;       // 最小射击质量

    // 特殊食物常量
    private static float HEALTH_ORB_CHANCE = 0.10f;   // 回血球生成概率（10%）
    private static float HEALTH_ORB_HEAL = 15f;        // 回血量
    private static float SPLIT_ORB_CHANCE = 0.08f;     // 分裂弹生成概率（8%）
    private static int MAX_SPLIT_AMMO = 5;             // 分裂弹最大存储数
    // food_type: 0=普通, 1=回血, 2=分裂弹

    [Table(Name = "entity", Public = true)]
    public partial struct Entity
    {
        [PrimaryKey, AutoInc]
        public int id;
        public float mass;      // 已经为 float
        public DbVector2 position;
        public float hp;        // 当前生命值
        public float max_hp;    // 生命上限 = 50 + mass * 0.5
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
        public int food_type;   // 0=普通, 1=回血球
    }
    [Table(Name = "bullet", Public = true)]
    public partial struct Bullet
    {
        [PrimaryKey]
        public int entity_id;          // 关联 entity 表
        public int owner_player_id;    // 发射者
        public float dir_x;            // 飞行方向（已归一化）
        public float dir_y;
        public double spawned_at_ms;   // 生成时间戳（毫秒）
        public int bullet_type;        // 0=普通, 1=分裂弹
    }
    [Table(Name = "player_ammo", Public = true)]
    public partial struct PlayerAmmo
    {
        [PrimaryKey]
        public int player_id;
        public int ammo_split;         // 分裂弹数量
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

    // 射击冷却追踪：player_id → 上次射击时间（毫秒）
    private static readonly System.Collections.Generic.Dictionary<int, double> _shootCooldowns = new();

    [Reducer]
    public static void ShootBullet(ReducerContext context, float dirX, float dirY, int bulletType)
    {
        var player = context.Db.logged_in_player.Identity.Find(context.Sender) ?? throw new Exception("未找到对应玩家");

        // 归一化方向
        float len = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (len < 0.001f) return;
        dirX /= len;
        dirY /= len;

        // 检查冷却
        double now = context.Timestamp.ToTimeSpanSinceUnixEpoch().TotalMilliseconds;
        if (_shootCooldowns.TryGetValue(player.player_id, out double lastShoot))
        {
            if (now - lastShoot < BULLET_COOLDOWN_SEC * 1000) return;
        }
        _shootCooldowns[player.player_id] = now;

        // 特殊子弹：读取弹药库存（分裂弹按球数消耗）
        int splitAmmoLeft = 0;
        if (bulletType == 1)
        {
            var ammo = context.Db.player_ammo.player_id.Find(player.player_id);
            if (ammo == null || ammo.Value.ammo_split <= 0) return;
            splitAmmoLeft = ammo.Value.ammo_split;
        }

        // 统计当前场上子弹数
        int bulletCount = 0;
        foreach (var b in context.Db.bullet.Iter())
            if (b.owner_player_id == player.player_id) bulletCount++;

        // 遍历玩家所有非合并中的球，每球发射一发
        foreach (var c in context.Db.circle.player_id.Filter(player.player_id))
        {
            if (c.isMerging) continue;

            var entNullable = context.Db.entity.id.Find(c.entity_id);
            if (entNullable == null) continue;
            var ent = entNullable.Value;

            // 质量足够才能发射
            if (ent.mass < MIN_SHOOT_MASS) continue;

            // 子弹上限检查
            if (bulletCount >= BULLET_MAX_PER_PLAYER) break;
            bulletCount++;

            // 特殊子弹：每个发射的球消耗 1 弹药
            if (bulletType == 1)
            {
                if (splitAmmoLeft <= 0) break;
                splitAmmoLeft--;
            }

            // 扣质量（每个球独立消耗）
            ent.mass -= BULLET_MASS_COST;
            if (ent.mass < 0.1f) ent.mass = 0.1f;
            UpdateHpAfterMassChange(ref ent);
            context.Db.entity.id.Update(ent);

            // 生成子弹
            var bulletEntity = context.Db.entity.Insert(new Entity
            {
                mass = 0.3f,
                position = new DbVector2(ent.position.x, ent.position.y),
                hp = 0,
                max_hp = 0
            });
            context.Db.bullet.Insert(new Bullet
            {
                entity_id = bulletEntity.id,
                owner_player_id = player.player_id,
                dir_x = dirX,
                dir_y = dirY,
                spawned_at_ms = now,
                bullet_type = bulletType
            });
            Log.Info($"[射击] 玩家 {player.player_id} 实体 {c.entity_id} 发射子弹 {bulletEntity.id} 类型 {bulletType}");
        }

        // 更新弹药（在循环后写入，确保原子性）
        if (bulletType == 1)
        {
            var ammo = context.Db.player_ammo.player_id.Find(player.player_id);
            if (ammo != null)
            {
                var a = ammo.Value;
                a.ammo_split = splitAmmoLeft;
                context.Db.player_ammo.player_id.Update(a);
            }
        }
    }

    // 调试用：对指定 entity 扣血（测试 HP 条用）
    [Reducer]
    public static void DebugDamage(ReducerContext context, int entityId, float amount)
    {
        var entityNullable = context.Db.entity.id.Find(entityId);
        if (entityNullable == null) return;
        var entity = entityNullable.Value;
        entity.hp -= amount;
        if (entity.hp < 0) entity.hp = 0;
        // 用 UpdateHpAfterMassChange 确保 max_hp 和下限保护也走一遍
        UpdateHpAfterMassChange(ref entity);
        context.Db.entity.id.Update(entity);
        Log.Info($"[调试] 实体 {entityId} 扣血 {amount}，剩余 HP: {entity.hp}/{entity.max_hp}");
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
            position = new DbVector2(x,y),//调用构造函数创建DbVector2实例
            hp = ComputeMaxHp(PRIMARY_PLAYER_MASS),
            max_hp = ComputeMaxHp(PRIMARY_PLAYER_MASS)
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

            // 随机生成特殊食物
            double roll = context.Rng.NextDouble();
            int foodType;
            if (roll < HEALTH_ORB_CHANCE)
            {
                foodType = 1; foodCurrentMass = 1.5f; // 回血球
            }
            else if (roll < HEALTH_ORB_CHANCE + SPLIT_ORB_CHANCE)
            {
                foodType = 2; foodCurrentMass = 1.8f; // 分裂弹（略大）
            }
            else
            {
                foodType = 0;
            }

            var entity = context.Db.entity.Insert(new Entity
            {
                mass = foodCurrentMass,
                position = new DbVector2(x, y)
            });
            context.Db.food.Insert(new Food
            {
                entity_id = entity.id,
                food_type = foodType
            });
            foodCount++;
        }
    }
    [Reducer]
    public static void MoveAllPlayer(ReducerContext context, MoveAllPlayerTimer timer)
    {
        // 第一阶段：移动所有玩家球（跳过正在合并动画中的球）
        foreach(var circle in context.Db.circle.Iter())
        {
            if (circle.isMerging) continue; // 合并动画中不移动

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
        var healGains = new System.Collections.Generic.Dictionary<int, float>(); // 回血
        var splitAmmoGains = new System.Collections.Generic.Dictionary<int, int>(); // 玩家 → 分裂弹数量
        var entitiesToDelete = new System.Collections.Generic.HashSet<int>();

        // 【性能优化】将 playerBalls 字典构建提前到循环外，只构建一次
        // 原代码在 circleA 循环内重复构建，导致O(n?)无效开销
        Dictionary<int, List<(Entity entity, int eid)>> playerBalls = new Dictionary<int, List<(Entity, int)>>();
        foreach (var cir in context.Db.circle.Iter())
        {
            if (cir.isMerging) continue; // 合并动画中不参与聚拢
            var ent = context.Db.entity.id.Find(cir.entity_id);
            if (ent == null) continue;
            if (!playerBalls.ContainsKey(cir.player_id))
                playerBalls[cir.player_id] = new List<(Entity, int)>();
            playerBalls[cir.player_id].Add((ent.Value, cir.entity_id));
        }

        // 重新遍历所有的 玩家球(circle) 去检测覆盖
        foreach(var circleA in context.Db.circle.Iter())
        {
            if (circleA.isMerging) continue; // 合并动画中不参与吞噬

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
                    var foodNullable = context.Db.food.entity_id.Find(entityB.id);
                    bool isFood = foodNullable != null;
                    bool isOtherPlayer = circleBNullable != null;

                    if (isFood || (isOtherPlayer && entityA.mass > entityB.mass))
                    {
                        // 标记 B 被吃掉
                        entitiesToDelete.Add(entityB.id);

                        // 记录A应该增加的质量
                        if (!massGains.ContainsKey(entityA.id))
                            massGains[entityA.id] = 0;
                        massGains[entityA.id] += entityB.mass;

                        // 回血球：记录回血量
                        if (isFood && foodNullable.Value.food_type == 1)
                        {
                            if (!healGains.ContainsKey(entityA.id))
                                healGains[entityA.id] = 0;
                            healGains[entityA.id] += HEALTH_ORB_HEAL;
                        }
                        // 分裂弹：给玩家增加分裂弹药
                        if (isFood && foodNullable.Value.food_type == 2)
                        {
                            if (!splitAmmoGains.ContainsKey(circleA.player_id))
                                splitAmmoGains[circleA.player_id] = 0;
                            splitAmmoGains[circleA.player_id] += 1;
                        }
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
        // 增重 + HP 更新 + 回血
        foreach(var kvp in massGains)
        {
            var entityToGainNullable = context.Db.entity.id.Find(kvp.Key);
            if (entityToGainNullable != null)
            {
                var entityToGain = entityToGainNullable.Value;
                entityToGain.mass += kvp.Value;
                // 质量变化后更新 max_hp，缓冲保护 hp 不越界
                UpdateHpAfterMassChange(ref entityToGain);
                context.Db.entity.id.Update(entityToGain);
            }
        }
        // 回血
        foreach(var kvp in healGains)
        {
            var entityToHealNullable = context.Db.entity.id.Find(kvp.Key);
            if (entityToHealNullable != null)
            {
                var entityToHeal = entityToHealNullable.Value;
                entityToHeal.hp += kvp.Value;
                if (entityToHeal.hp > entityToHeal.max_hp)
                    entityToHeal.hp = entityToHeal.max_hp;
                context.Db.entity.id.Update(entityToHeal);
            }
        }
        // 分裂弹拾取：增加玩家的分裂弹药（上限 MAX_SPLIT_AMMO）
        foreach(var kvp in splitAmmoGains)
        {
            var ammo = context.Db.player_ammo.player_id.Find(kvp.Key);
            if (ammo == null)
            {
                context.Db.player_ammo.Insert(new PlayerAmmo { player_id = kvp.Key, ammo_split = 1 });
            }
            else
            {
                var a = ammo.Value;
                a.ammo_split = Math.Min(a.ammo_split + kvp.Value, MAX_SPLIT_AMMO);
                context.Db.player_ammo.player_id.Update(a);
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

        // 第四阶段：子弹移动 + 碰撞检测
        double now = context.Timestamp.ToTimeSpanSinceUnixEpoch().TotalMilliseconds;
        var bulletsToDelete = new System.Collections.Generic.HashSet<int>();
        var bulletHits = new System.Collections.Generic.Dictionary<int, float>(); // entity_id → damage
        var splitHits = new System.Collections.Generic.HashSet<int>();           // 被分裂弹命中的 circle entity_id

        foreach (var bullet in context.Db.bullet.Iter())
        {
            var bulletEntNullable = context.Db.entity.id.Find(bullet.entity_id);
            if (bulletEntNullable == null)
            {
                bulletsToDelete.Add(bullet.entity_id);
                continue;
            }
            var bulletEnt = bulletEntNullable.Value;

            // 过期判定
            if (now - bullet.spawned_at_ms >= BULLET_LIFETIME_MS)
            {
                bulletsToDelete.Add(bullet.entity_id);
                continue;
            }

            // 移动子弹
            float moveStep = 0.05f * BULLET_SPEED; // 与 MoveAllPlayer 的步长一致（50ms）
            float origX = bulletEnt.position.x, origY = bulletEnt.position.y;
            bulletEnt.position.x += bullet.dir_x * moveStep;
            bulletEnt.position.y += bullet.dir_y * moveStep;
            ClampEntityToBounds(ref bulletEnt);
            context.Db.entity.id.Update(bulletEnt);

            // 碰撞检测：检查移动前、中、后三点防止子弹跳帧穿透（子弹速度太快时可能跳过球）
            float midX = (origX + bulletEnt.position.x) * 0.5f;
            float midY = (origY + bulletEnt.position.y) * 0.5f;
            // 先收集所有非发射者的球，然后检测三个采样点
            foreach (var circle in context.Db.circle.Iter())
            {
                if (circle.isMerging) continue;
                if (bulletsToDelete.Contains(bullet.entity_id)) break;
                if (circle.player_id == bullet.owner_player_id) continue;

                var targetEntNullable = context.Db.entity.id.Find(circle.entity_id);
                if (targetEntNullable == null) continue;
                var targetEnt = targetEntNullable.Value;
                float targetRadius = MassToDiameter(targetEnt.mass) / 2f;
                float r2 = targetRadius * targetRadius;

                // 三个采样点：移动前→中点→移动后，任意一点命中即触发
                float d0 = (origX - targetEnt.position.x) * (origX - targetEnt.position.x)
                         + (origY - targetEnt.position.y) * (origY - targetEnt.position.y);
                float dm = (midX - targetEnt.position.x) * (midX - targetEnt.position.x)
                         + (midY - targetEnt.position.y) * (midY - targetEnt.position.y);
                float d1 = (bulletEnt.position.x - targetEnt.position.x) * (bulletEnt.position.x - targetEnt.position.x)
                         + (bulletEnt.position.y - targetEnt.position.y) * (bulletEnt.position.y - targetEnt.position.y);

                if (d0 <= r2 || dm <= r2 || d1 <= r2)
                {
                    bulletsToDelete.Add(bullet.entity_id);
                    if (bullet.bullet_type == 1) // 分裂弹 → 强制目标分裂
                    {
                        splitHits.Add(circle.entity_id);
                    }
                    else // 普通子弹 → 伤害
                    {
                        if (!bulletHits.ContainsKey(circle.entity_id))
                            bulletHits[circle.entity_id] = 0;
                        bulletHits[circle.entity_id] += BULLET_DAMAGE;
                    }
                    break;
                }
            }
        }

        // 统一应用伤害
        foreach (var kv in bulletHits)
        {
            var targetNullable = context.Db.entity.id.Find(kv.Key);
            if (targetNullable == null) continue;
            var target = targetNullable.Value;
            target.hp -= kv.Value;
            if (target.hp < 0) target.hp = 0;
            // 伤害不调用 UpdateHpAfterMassChange（其 HP_MIN_RATIO 保护会阻止死亡）
            // 只确保 hp 不超过上限
            if (target.hp > target.max_hp) target.hp = target.max_hp;
            context.Db.entity.id.Update(target);

            // HP=0 则死亡：散落食物、删除实体
            if (target.hp <= 0)
            {
                Log.Info($"[击杀] 实体 {target.id} 被子弹打死！");
                // 散落食物
                int dropCount = 3 + (int)(target.mass * 0.05f);
                if (dropCount > 8) dropCount = 8;
                for (int i = 0; i < dropCount; i++)
                {
                    float rx = (float)(context.Rng.NextDouble() - 0.5) * 3f;
                    float ry = (float)(context.Rng.NextDouble() - 0.5) * 3f;
                    var foodEnt = context.Db.entity.Insert(new Entity
                    {
                        mass = target.mass * 0.2f / dropCount,
                        position = new DbVector2(target.position.x + rx, target.position.y + ry),
                        hp = 0,
                        max_hp = 0
                    });
                    context.Db.food.Insert(new Food { entity_id = foodEnt.id });
                }
                // 删除死亡玩家的 circle 和 entity
                if (context.Db.circle.entity_id.Find(target.id) != null)
                    context.Db.circle.entity_id.Delete(target.id);
                context.Db.entity.id.Delete(target.id);
            }
        }

        // 分裂弹效果：对命中的球强制分裂
        foreach (var targetEid in splitHits)
        {
            var tgtCircleNullable = context.Db.circle.entity_id.Find(targetEid);
            if (tgtCircleNullable == null) continue;
            var tgtEntNullable = context.Db.entity.id.Find(targetEid);
            if (tgtEntNullable == null) continue;
            var tgtEnt = tgtEntNullable.Value;
            if (tgtEnt.mass < MIN_SPLIT_MASS) continue;

            // 执行分裂（与 SplitPlayer 同样的逻辑）
            float halfMass = tgtEnt.mass / 2f;
            float halfHp = tgtEnt.hp / 2f;
            tgtEnt.mass = halfMass;
            tgtEnt.hp = halfHp;
            tgtEnt.max_hp = ComputeMaxHp(halfMass);
            float minHp = halfMass * HP_MIN_RATIO;
            if (tgtEnt.hp < minHp) tgtEnt.hp = minHp;
            context.Db.entity.id.Update(tgtEnt);

            // 分裂方向：随机
            float angle = (float)(context.Rng.NextDouble() * MathF.PI * 2);
            float dx = MathF.Cos(angle), dy = MathF.Sin(angle);
            float offset = MassToDiameter(halfMass) + 1.0f;
            var newEnt = context.Db.entity.Insert(new Entity
            {
                mass = halfMass,
                position = new DbVector2(tgtEnt.position.x + dx * offset, tgtEnt.position.y + dy * offset),
                hp = halfHp,
                max_hp = ComputeMaxHp(halfMass)
            });
            context.Db.circle.Insert(new Circle
            {
                entity_id = newEnt.id,
                player_id = tgtCircleNullable.Value.player_id,
                touchStartMs = 0,
                isMerging = false,
                isSplitting = true,
                splitFromEntityId = targetEid
            });
        }

        // 删除过期/命中的子弹
        foreach (var bid in bulletsToDelete)
        {
            context.Db.bullet.entity_id.Delete(bid);
            context.Db.entity.id.Delete(bid);
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

    // ===== HP 工具函数 =====
    /// <summary>
    /// 根据质量计算 HP 上限。公式：HP_BASE + mass * HP_MASS_COEFF
    /// </summary>
    public static float ComputeMaxHp(float mass)
    {
        return HP_BASE + mass * HP_MASS_COEFF;
    }

    /// <summary>
    /// 质量变化后更新 max_hp，并确保 hp 不越界。
    /// </summary>
    public static void UpdateHpAfterMassChange(ref Entity entity)
    {
        float newMax = ComputeMaxHp(entity.mass);
        entity.max_hp = newMax;
        // 缓冲保护：HP 不低于 mass * HP_MIN_RATIO
        float minHp = entity.mass * HP_MIN_RATIO;
        if (entity.hp < minHp) entity.hp = minHp;
        // 上限：hp 不能超过新的 max_hp
        if (entity.hp > newMax) entity.hp = newMax;
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
                float halfHp = entity.hp / 2f; // HP 同质量一样对半分
                
                // 1. 将旧实体质量减半，HP 减半
                entity.mass = halfMass;
                entity.hp = halfHp;
                entity.max_hp = ComputeMaxHp(halfMass);
                // 下限保护
                float minHp = halfMass * HP_MIN_RATIO;
                if (entity.hp < minHp) entity.hp = minHp;
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
                    position = new DbVector2(entity.position.x + dirX * offset, entity.position.y + dirY * offset),
                    hp = halfHp,
                    max_hp = ComputeMaxHp(halfMass)
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
        double now = context.Timestamp.ToTimeSpanSinceUnixEpoch().TotalMilliseconds;

        foreach (var player in context.Db.logged_in_player.Iter())
        {
            // 重新读取最新 circle 列表（上一轮 isMerging 标记后可能已删除）
            var list = new List<Circle>();
            foreach (var c in context.Db.circle.player_id.Filter(player.player_id)) list.Add(c);
            if (list.Count <= 1) continue;

            // 缓存 entity_id → Entity
            var circleEntDic = new Dictionary<int, Entity>();
            foreach (var cir in list)
            {
                var ent = context.Db.entity.id.Find(cir.entity_id);
                if (ent != null) circleEntDic[cir.entity_id] = ent.Value;
            }

            // ===== 第一阶段：更新贴合标记与计时 =====
            for (int a = 0; a < list.Count; a++)
            {
                Circle ca = list[a];
                if (ca.isMerging) continue; // 已在合并动画中，跳过

                Entity ea;
                if (!circleEntDic.TryGetValue(ca.entity_id, out ea)) continue;

                bool isTouch = false;
                for (int b = 0; b < list.Count; b++)
                {
                    if (a == b) continue;
                    Circle cb = list[b];
                    if (cb.isMerging) continue; // 正在合并动画中的球不算贴合
                    Entity eb;
                    if (!circleEntDic.TryGetValue(cb.entity_id, out eb)) continue;

                    float ra = MassToDiameter(ea.mass) / 2;
                    float rb = MassToDiameter(eb.mass) / 2;
                    float dx = ea.position.x - eb.position.x;
                    float dy = ea.position.y - eb.position.y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    if (dist <= ra + rb + 0.03f)
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

            // 从表重新读取以获取最新的 touchStartMs
            for (int i = 0; i < list.Count; i++)
            {
                var updated = context.Db.circle.entity_id.Find(list[i].entity_id);
                if (updated != null) list[i] = updated.Value;
            }

            // ===== 第二阶段：逐对合并（每次只合并一对） =====
            for (int a = 0; a < list.Count; a++)
            {
                Circle ca = list[a];
                if (ca.isMerging) continue; // 已在合并动画中

                Entity ea;
                if (!circleEntDic.TryGetValue(ca.entity_id, out ea)) continue;

                float needSecA = BASE_MERGE_SEC + MathF.Sqrt(ea.mass) * SQRT_DELAY_COEFF;
                bool aReady = ca.touchStartMs > 0 && (now - ca.touchStartMs) >= (long)(needSecA * 1000);
                if (!aReady) continue;

                for (int b = 0; b < list.Count; b++)
                {
                    if (a == b) continue;
                    Circle cb = list[b];
                    if (cb.isMerging) continue;

                    Entity eb;
                    if (!circleEntDic.TryGetValue(cb.entity_id, out eb)) continue;

                    float needSecB = BASE_MERGE_SEC + MathF.Sqrt(eb.mass) * SQRT_DELAY_COEFF;
                    bool bReady = cb.touchStartMs > 0 && (now - cb.touchStartMs) >= (long)(needSecB * 1000);
                    if (!bReady) continue;

                    // 检查是否贴合
                    float ra = MassToDiameter(ea.mass) / 2f;
                    float rb_radius = MassToDiameter(eb.mass) / 2f;
                    float dx = ea.position.x - eb.position.x;
                    float dy = ea.position.y - eb.position.y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist > ra + rb_radius + 0.03f) continue;

                    // 大吞小：标记小球为 isMerging，等客户端动画完成后通过 FinishMerge 真正删除
                    int smallEntityId = ea.mass >= eb.mass ? cb.entity_id : ca.entity_id;
                    int bigEntityId = ea.mass >= eb.mass ? ca.entity_id : cb.entity_id;

                    // 把小球的质量先加到大球上，HP 合并（相加，上限为新 max_hp）
                    var bigEnt = circleEntDic[bigEntityId];
                    var smallEnt = circleEntDic[smallEntityId];
                    float mergedMass = bigEnt.mass + smallEnt.mass;
                    float mergedHp = bigEnt.hp + smallEnt.hp;
                    float newMaxHp = ComputeMaxHp(mergedMass);
                    bigEnt.mass = mergedMass;
                    bigEnt.hp = Math.Min(mergedHp, newMaxHp);
                    bigEnt.max_hp = newMaxHp;
                    context.Db.entity.id.Update(bigEnt);

                    // 标记小球为合并动画中（不再参与移动/贴合判定/吞噬）
                    // splitFromEntityId 复用为合并目标的 entity_id，客户端据此飞向正确的大球
                    var smallCircle = context.Db.circle.entity_id.Find(smallEntityId).Value;
                    smallCircle.isMerging = true;
                    smallCircle.touchStartMs = 0;
                    smallCircle.splitFromEntityId = bigEntityId; // 告诉客户端合并目标是谁
                    context.Db.circle.entity_id.Update(smallCircle);

                    // 只合并这一对，下一轮 MergePlayerCheck 再处理下一对
                    return; // ← 关键：每次定时器触发只合并一对
                }
            }
        }
    }

    /// <summary>
    /// 客户端合并动画完成后调用，真正删除小球。
    /// 由客户端 CircleController 在合并动画播完后调用。
    /// </summary>
    [Reducer]
    public static void FinishMerge(ReducerContext context, int smallEntityId)
    {
        var circle = context.Db.circle.entity_id.Find(smallEntityId);
        if (circle == null) return; // 已被删除或不存在

        var player = context.Db.logged_in_player.player_id.Find(circle.Value.player_id);
        if (player == null) return;

        // 安全检查：只有该球的所属玩家才能触发删除
        // （防止其他客户端恶意发送）
        // SpacetimeDB 的 Reducer 由 context.Sender 标识调用者
        var senderPlayer = context.Db.logged_in_player.Identity.Find(context.Sender);
        if (senderPlayer == null) return;
        if (senderPlayer.Value.player_id != circle.Value.player_id) return;

        // 删除小球
        context.Db.circle.entity_id.Delete(smallEntityId);
        context.Db.entity.id.Delete(smallEntityId);
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
