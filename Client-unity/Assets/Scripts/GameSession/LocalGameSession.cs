using System;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;

public sealed class LocalGameSession : IGameSession
{
    private const float FixedStepSeconds = 0.040f;
    private const float MaxFrameDelta = 0.25f;
    private const int MaxCatchUpSteps = 6;

    private readonly GameTables _tables = new();
    private readonly LocalGameWorld _world;
    private float _accumulator;
    private bool _disposed;

    public LocalGameSession()
    {
        _world = new LocalGameWorld(this);
        Actions = new LocalActions(this);
    }

    public bool IsReady => !_disposed;
    public bool IsLocal => true;
    public int LocalPlayerId => _world.LocalPlayerId;
    public Identity LocalIdentity => default;
    public IGameTables Tables => _tables;
    public IGameActions Actions { get; }

    public Entity FindEntity(int entityId) => _world.FindEntity(entityId);
    public Food FindFood(int entityId) => _world.FindFood(entityId);
    public Circle FindCircle(int entityId) => _world.FindCircle(entityId);
    public Bullet FindBullet(int entityId) => _world.FindBullet(entityId);
    public Shield FindShield(int entityId) => _world.FindShield(entityId);
    public SpeedBuff FindSpeedBuff(int playerId) => _world.FindSpeedBuff(playerId);
    public PlayerAmmo FindPlayerAmmo(int playerId) => _world.FindPlayerAmmo(playerId);
    public Player FindPlayerByIdentity(Identity identity) => _world.FindPlayerByIdentity(identity);
    public Player FindPlayerByPlayerId(int playerId) => _world.FindPlayerByPlayerId(playerId);
    public Player FindLocalPlayer() => _world.FindPlayerByPlayerId(LocalPlayerId);
    public bool IsLocalPlayerId(int playerId) => playerId != 0 && playerId == LocalPlayerId;
    public int FoodCount => _world.FoodCount;

    public void StartGame(string playerName, int botCount, int skinIndex)
    {
        if (_disposed) return;
        _world.StartGame(playerName, botCount, skinIndex);
    }

    public void Tick(float deltaTime)
    {
        if (!IsReady || !_world.IsStarted) return;

        _accumulator += Mathf.Clamp(deltaTime, 0f, MaxFrameDelta);
        int steps = 0;
        while (_accumulator >= FixedStepSeconds && steps < MaxCatchUpSteps)
        {
            _world.Tick();
            _accumulator -= FixedStepSeconds;
            steps++;
        }

        if (steps == MaxCatchUpSteps)
            _accumulator = 0f;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _world.Dispose();
        _tables.ClearSubscribers();
    }

    internal void RaiseEntityInsert(Entity row) => _tables.Entity.RaiseInsert(row);
    internal void RaiseEntityUpdate(Entity oldRow, Entity newRow) => _tables.Entity.RaiseUpdate(oldRow, newRow);
    internal void RaiseEntityDelete(Entity row) => _tables.Entity.RaiseDelete(row);
    internal void RaiseFoodInsert(Food row) => _tables.Food.RaiseInsert(row);
    internal void RaiseFoodDelete(Food row) => _tables.Food.RaiseDelete(row);
    internal void RaiseCircleInsert(Circle row) => _tables.Circle.RaiseInsert(row);
    internal void RaiseCircleUpdate(Circle oldRow, Circle newRow) => _tables.Circle.RaiseUpdate(oldRow, newRow);
    internal void RaiseCircleDelete(Circle row) => _tables.Circle.RaiseDelete(row);
    internal void RaiseBulletInsert(Bullet row) => _tables.Bullet.RaiseInsert(row);
    internal void RaiseBulletDelete(Bullet row) => _tables.Bullet.RaiseDelete(row);
    internal void RaisePlayerAmmoInsert(PlayerAmmo row) => _tables.PlayerAmmo.RaiseInsert(row);
    internal void RaisePlayerAmmoUpdate(PlayerAmmo oldRow, PlayerAmmo newRow)
        => _tables.PlayerAmmo.RaiseUpdate(oldRow, newRow);
    internal void RaiseShieldInsert(Shield row) => _tables.Shield.RaiseInsert(row);
    internal void RaiseShieldUpdate(Shield oldRow, Shield newRow) => _tables.Shield.RaiseUpdate(oldRow, newRow);
    internal void RaiseShieldDelete(Shield row) => _tables.Shield.RaiseDelete(row);
    internal void RaiseSpeedBuffInsert(SpeedBuff row) => _tables.SpeedBuff.RaiseInsert(row);
    internal void RaiseSpeedBuffDelete(SpeedBuff row) => _tables.SpeedBuff.RaiseDelete(row);
    internal void RaisePlayerInsert(Player row) => _tables.LoggedInPlayer.RaiseInsert(row);
    internal void RaisePlayerUpdate(Player oldRow, Player newRow) => _tables.LoggedInPlayer.RaiseUpdate(oldRow, newRow);

    private sealed class LocalActions : IGameActions
    {
        private readonly LocalGameSession _session;
        public LocalActions(LocalGameSession session) => _session = session;

        public void EnterGame(string playerName) => _session._world.EnterGame(playerName);
        public void UpdatePlayerDir(DbVector2 direction)
            => _session._world.UpdatePlayerDir(_session.LocalPlayerId, direction);
        public void SplitPlayer() => _session._world.SplitPlayer(_session.LocalPlayerId);
        public void ShootBullet(float targetX, float targetY, int bulletType)
            => _session._world.ShootBullet(_session.LocalPlayerId, targetX, targetY, bulletType);
        public void FinishMerge(int smallEntityId) => _session._world.FinishMerge(_session.LocalPlayerId, smallEntityId);
        public void FinishSplitAnimation(int entityId)
            => _session._world.FinishSplitAnimation(_session.LocalPlayerId, entityId);
        public void UpdatePlayerSkin(int skinIndex)
            => _session._world.UpdatePlayerSkin(_session.LocalPlayerId, skinIndex);
        public void DebugDamage(int entityId, float amount)
            => _session._world.DebugDamage(entityId, amount);
    }
}
