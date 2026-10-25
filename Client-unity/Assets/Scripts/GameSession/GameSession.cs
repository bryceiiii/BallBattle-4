using System;
using SpacetimeDB;
using SpacetimeDB.Types;

public interface IGameTable<T>
{
    event Action<T> OnInsert;
    event Action<T, T> OnUpdate;
    event Action<T> OnDelete;
}

public sealed class GameTable<T> : IGameTable<T>
{
    public event Action<T> OnInsert;
    public event Action<T, T> OnUpdate;
    public event Action<T> OnDelete;

    internal void RaiseInsert(T row) => OnInsert?.Invoke(row);
    internal void RaiseUpdate(T oldRow, T newRow) => OnUpdate?.Invoke(oldRow, newRow);
    internal void RaiseDelete(T row) => OnDelete?.Invoke(row);

    internal void Clear()
    {
        OnInsert = null;
        OnUpdate = null;
        OnDelete = null;
    }
}

public interface IGameTables
{
    IGameTable<Entity> Entity { get; }
    IGameTable<Food> Food { get; }
    IGameTable<Circle> Circle { get; }
    IGameTable<Bullet> Bullet { get; }
    IGameTable<PlayerAmmo> PlayerAmmo { get; }
    IGameTable<Shield> Shield { get; }
    IGameTable<SpeedBuff> SpeedBuff { get; }
    IGameTable<Player> LoggedInPlayer { get; }
    IGameTable<LaserBeam> LaserBeam { get; }
}

public interface IGameActions
{
    void EnterGame(string playerName);
    void UpdatePlayerDir(DbVector2 direction);
    void SplitPlayer();
    void ShootBullet(float targetX, float targetY, int bulletType);
    void FinishMerge(int smallEntityId);
    void FinishSplitAnimation(int entityId);
    void UpdatePlayerSkin(int skinIndex);
    void DebugDamage(int entityId, float amount);
}

public interface IGameSession : IDisposable
{
    bool IsReady { get; }
    bool IsLocal { get; }
    int LocalPlayerId { get; }
    double NowMs { get; }
    Identity LocalIdentity { get; }
    IGameTables Tables { get; }
    IGameActions Actions { get; }

    Entity FindEntity(int entityId);
    Food FindFood(int entityId);
    Circle FindCircle(int entityId);
    Bullet FindBullet(int entityId);
    Shield FindShield(int entityId);
    SpeedBuff FindSpeedBuff(int playerId);
    PlayerAmmo FindPlayerAmmo(int playerId);
    Player FindPlayerByIdentity(Identity identity);
    Player FindPlayerByPlayerId(int playerId);
    Player FindLocalPlayer();
    bool IsLocalPlayerId(int playerId);
    int FoodCount { get; }
    void Tick(float deltaTime);
}

public sealed class GameTables : IGameTables
{
    public GameTable<Entity> Entity { get; } = new();
    public GameTable<Food> Food { get; } = new();
    public GameTable<Circle> Circle { get; } = new();
    public GameTable<Bullet> Bullet { get; } = new();
    public GameTable<PlayerAmmo> PlayerAmmo { get; } = new();
    public GameTable<Shield> Shield { get; } = new();
    public GameTable<SpeedBuff> SpeedBuff { get; } = new();
    public GameTable<Player> LoggedInPlayer { get; } = new();
    public GameTable<LaserBeam> LaserBeam { get; } = new();

    IGameTable<Entity> IGameTables.Entity => Entity;
    IGameTable<Food> IGameTables.Food => Food;
    IGameTable<Circle> IGameTables.Circle => Circle;
    IGameTable<Bullet> IGameTables.Bullet => Bullet;
    IGameTable<PlayerAmmo> IGameTables.PlayerAmmo => PlayerAmmo;
    IGameTable<Shield> IGameTables.Shield => Shield;
    IGameTable<SpeedBuff> IGameTables.SpeedBuff => SpeedBuff;
    IGameTable<Player> IGameTables.LoggedInPlayer => LoggedInPlayer;
    IGameTable<LaserBeam> IGameTables.LaserBeam => LaserBeam;

    internal void ClearSubscribers()
    {
        Entity.Clear();
        Food.Clear();
        Circle.Clear();
        Bullet.Clear();
        PlayerAmmo.Clear();
        Shield.Clear();
        SpeedBuff.Clear();
        LoggedInPlayer.Clear();
        LaserBeam.Clear();
    }
}

public sealed class SpacetimeGameSession : IGameSession
{
    private readonly DbConnection _connection;
    private readonly GameTables _tables = new();
    private bool _disposed;

    public SpacetimeGameSession(DbConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Actions = new OnlineActions(connection);
        HookEvents();
    }

    public DbConnection Connection => _connection;
    public bool IsReady => !_disposed && _connection.Db != null;
    public bool IsLocal => false;
    public double NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public int LocalPlayerId
    {
        get
        {
            var player = FindLocalPlayer();
            return player != null ? player.PlayerId : 0;
        }
    }
    public Identity LocalIdentity => _connection.Identity ?? default;
    public IGameTables Tables => _tables;
    public IGameActions Actions { get; }

    public Entity FindEntity(int entityId) => _connection.Db.Entity.Id.Find(entityId);
    public Food FindFood(int entityId) => _connection.Db.Food.EntityId.Find(entityId);
    public Circle FindCircle(int entityId) => _connection.Db.Circle.EntityId.Find(entityId);
    public Bullet FindBullet(int entityId) => _connection.Db.Bullet.EntityId.Find(entityId);
    public Shield FindShield(int entityId) => _connection.Db.Shield.EntityId.Find(entityId);
    public SpeedBuff FindSpeedBuff(int playerId) => _connection.Db.SpeedBuff.PlayerId.Find(playerId);
    public PlayerAmmo FindPlayerAmmo(int playerId) => _connection.Db.PlayerAmmo.PlayerId.Find(playerId);

    public Player FindPlayerByIdentity(Identity identity)
        => _connection.Db.LoggedInPlayer.Identity.Find(identity);

    public Player FindPlayerByPlayerId(int playerId)
        => _connection.Db.LoggedInPlayer.PlayerId.Find(playerId);

    public Player FindLocalPlayer()
    {
        var identity = _connection.Identity;
        return identity.HasValue ? FindPlayerByIdentity(identity.Value) : null;
    }

    public bool IsLocalPlayerId(int playerId)
    {
        var player = FindLocalPlayer();
        return player != null && player.PlayerId == playerId;
    }

    public int FoodCount => (int)_connection.Db.Food.Count;

    public void Tick(float deltaTime)
    {
        // Network connections are ticked by SpacetimeDBNetworkManager.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnhookEvents();
        _tables.ClearSubscribers();
    }

    private void HookEvents()
    {
        _connection.Db.Entity.OnUpdate += OnEntityUpdated;
        _connection.Db.Food.OnInsert += OnFoodInserted;
        _connection.Db.Food.OnDelete += OnFoodDeleted;
        _connection.Db.Circle.OnInsert += OnCircleInserted;
        _connection.Db.Circle.OnUpdate += OnCircleUpdated;
        _connection.Db.Circle.OnDelete += OnCircleDeleted;
        _connection.Db.Bullet.OnInsert += OnBulletInserted;
        _connection.Db.Bullet.OnDelete += OnBulletDeleted;
        _connection.Db.PlayerAmmo.OnInsert += OnPlayerAmmoInserted;
        _connection.Db.PlayerAmmo.OnUpdate += OnPlayerAmmoUpdated;
        _connection.Db.Shield.OnInsert += OnShieldInserted;
        _connection.Db.Shield.OnUpdate += OnShieldUpdated;
        _connection.Db.Shield.OnDelete += OnShieldDeleted;
        _connection.Db.SpeedBuff.OnInsert += OnSpeedBuffInserted;
        _connection.Db.SpeedBuff.OnUpdate += OnSpeedBuffUpdated;
        _connection.Db.SpeedBuff.OnDelete += OnSpeedBuffDeleted;
        _connection.Db.LoggedInPlayer.OnInsert += OnPlayerInserted;
        _connection.Db.LoggedInPlayer.OnUpdate += OnPlayerUpdated;
        _connection.Db.LoggedInPlayer.OnDelete += OnPlayerDeleted;
        _connection.Db.LaserBeam.OnInsert += OnLaserBeamInserted;
    }

    private void UnhookEvents()
    {
        _connection.Db.Entity.OnUpdate -= OnEntityUpdated;
        _connection.Db.Food.OnInsert -= OnFoodInserted;
        _connection.Db.Food.OnDelete -= OnFoodDeleted;
        _connection.Db.Circle.OnInsert -= OnCircleInserted;
        _connection.Db.Circle.OnUpdate -= OnCircleUpdated;
        _connection.Db.Circle.OnDelete -= OnCircleDeleted;
        _connection.Db.Bullet.OnInsert -= OnBulletInserted;
        _connection.Db.Bullet.OnDelete -= OnBulletDeleted;
        _connection.Db.PlayerAmmo.OnInsert -= OnPlayerAmmoInserted;
        _connection.Db.PlayerAmmo.OnUpdate -= OnPlayerAmmoUpdated;
        _connection.Db.Shield.OnInsert -= OnShieldInserted;
        _connection.Db.Shield.OnUpdate -= OnShieldUpdated;
        _connection.Db.Shield.OnDelete -= OnShieldDeleted;
        _connection.Db.SpeedBuff.OnInsert -= OnSpeedBuffInserted;
        _connection.Db.SpeedBuff.OnUpdate -= OnSpeedBuffUpdated;
        _connection.Db.SpeedBuff.OnDelete -= OnSpeedBuffDeleted;
        _connection.Db.LoggedInPlayer.OnInsert -= OnPlayerInserted;
        _connection.Db.LoggedInPlayer.OnUpdate -= OnPlayerUpdated;
        _connection.Db.LoggedInPlayer.OnDelete -= OnPlayerDeleted;
        _connection.Db.LaserBeam.OnInsert -= OnLaserBeamInserted;
    }

    private void OnEntityUpdated(EventContext ctx, Entity oldRow, Entity newRow)
        => _tables.Entity.RaiseUpdate(oldRow, newRow);
    private void OnFoodInserted(EventContext ctx, Food row) => _tables.Food.RaiseInsert(row);
    private void OnFoodDeleted(EventContext ctx, Food row) => _tables.Food.RaiseDelete(row);
    private void OnCircleInserted(EventContext ctx, Circle row) => _tables.Circle.RaiseInsert(row);
    private void OnCircleUpdated(EventContext ctx, Circle oldRow, Circle newRow)
        => _tables.Circle.RaiseUpdate(oldRow, newRow);
    private void OnCircleDeleted(EventContext ctx, Circle row) => _tables.Circle.RaiseDelete(row);
    private void OnBulletInserted(EventContext ctx, Bullet row) => _tables.Bullet.RaiseInsert(row);
    private void OnBulletDeleted(EventContext ctx, Bullet row) => _tables.Bullet.RaiseDelete(row);
    private void OnPlayerAmmoInserted(EventContext ctx, PlayerAmmo row) => _tables.PlayerAmmo.RaiseInsert(row);
    private void OnPlayerAmmoUpdated(EventContext ctx, PlayerAmmo oldRow, PlayerAmmo newRow)
        => _tables.PlayerAmmo.RaiseUpdate(oldRow, newRow);
    private void OnShieldInserted(EventContext ctx, Shield row) => _tables.Shield.RaiseInsert(row);
    private void OnShieldUpdated(EventContext ctx, Shield oldRow, Shield newRow)
        => _tables.Shield.RaiseUpdate(oldRow, newRow);
    private void OnShieldDeleted(EventContext ctx, Shield row) => _tables.Shield.RaiseDelete(row);
    private void OnSpeedBuffInserted(EventContext ctx, SpeedBuff row) => _tables.SpeedBuff.RaiseInsert(row);
    private void OnSpeedBuffUpdated(EventContext ctx, SpeedBuff oldRow, SpeedBuff newRow)
        => _tables.SpeedBuff.RaiseUpdate(oldRow, newRow);
    private void OnSpeedBuffDeleted(EventContext ctx, SpeedBuff row) => _tables.SpeedBuff.RaiseDelete(row);
    private void OnPlayerInserted(EventContext ctx, Player row) => _tables.LoggedInPlayer.RaiseInsert(row);
    private void OnPlayerUpdated(EventContext ctx, Player oldRow, Player newRow)
        => _tables.LoggedInPlayer.RaiseUpdate(oldRow, newRow);
    private void OnPlayerDeleted(EventContext ctx, Player row) => _tables.LoggedInPlayer.RaiseDelete(row);
    private void OnLaserBeamInserted(EventContext ctx, LaserBeam row) => _tables.LaserBeam.RaiseInsert(row);

    private sealed class OnlineActions : IGameActions
    {
        private readonly DbConnection _connection;
        public OnlineActions(DbConnection connection) => _connection = connection;

        public void EnterGame(string playerName) => _connection.Reducers.EnterGame(playerName);
        public void UpdatePlayerDir(DbVector2 direction) => _connection.Reducers.UpdatePlayerDir(direction);
        public void SplitPlayer() => _connection.Reducers.SplitPlayer();
        public void ShootBullet(float targetX, float targetY, int bulletType)
            => _connection.Reducers.ShootBullet(targetX, targetY, bulletType);
        public void FinishMerge(int smallEntityId) => _connection.Reducers.FinishMerge(smallEntityId);
        public void FinishSplitAnimation(int entityId)
            => _connection.Reducers.FinishSplitAnimation(entityId);
        public void UpdatePlayerSkin(int skinIndex) => _connection.Reducers.UpdatePlayerSkin(skinIndex);
        public void DebugDamage(int entityId, float amount)
            => _connection.Reducers.DebugDamage(entityId, amount);
    }
}

public static class GameSessionManager
{
    public static IGameSession Current { get; private set; }
    public static event Action<IGameSession> Changed;

    public static SpacetimeGameSession EnsureOnline(DbConnection connection)
    {
        if (connection == null) return null;
        if (Current is SpacetimeGameSession online && online.Connection == connection)
            return online;

        Shutdown();
        online = new SpacetimeGameSession(connection);
        Current = online;
        Changed?.Invoke(Current);
        return online;
    }

    public static LocalGameSession CreateLocal()
    {
        Shutdown();
        var local = new LocalGameSession();
        Current = local;
        Changed?.Invoke(Current);
        return local;
    }

    public static LocalGameSession StartLocal(string playerName, int botCount, int skinIndex)
    {
        var local = CreateLocal();
        local.StartGame(playerName, botCount, skinIndex);
        return local;
    }

    public static void Shutdown()
    {
        if (Current == null) return;
        Current.Dispose();
        Current = null;
        Changed?.Invoke(null);
    }
}
