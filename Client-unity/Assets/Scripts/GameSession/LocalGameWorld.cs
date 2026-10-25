using System;
using System.Collections.Generic;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;
using Random = System.Random;

public sealed class LocalGameWorld
{
    private const float WorldSize = 100f;
    private const float ServerDelta = 0.040f;
    private const float PrimaryPlayerMass = 5f;
    private const int BaseFoodCount = 600;
    private const int PrewarmFoodCount = 360;
    private const float FoodMassRatio = 0.3f;
    private const float StartPlayerSpeed = 16f;
    private const float SpeedDecayCoeff = 0.03f;
    private const float MinSpeedScale = 0.18f;
    private const float MinSplitMass = 10f;
    private const int MaxCirclesPerPlayer = 16;
    private const float BaseMergeSec = 2f;
    private const float SqrtDelayCoeff = 0.8f;
    private const float SeparationFactor = 0.5f;
    private const float HpBase = 50f;
    private const float HpMassCoeff = 0.5f;
    private const float HpMinRatio = 0.3f;
    private const float BulletMassCost = 0.8f;
    private const float BulletSpeed = 25f;
    private const double BulletLifetimeMs = 1500d;
    private const float BulletDamage = 8f;
    private const float BulletCooldownSec = 0.25f;
    private const float MinShootMass = 3f;
    private const float HealthOrbChance = 0.10f;
    private const float HealthOrbHeal = 15f;
    private const float SplitOrbChance = 0.08f;
    private const int MaxSplitAmmo = 5;
    private const float ShieldOrbChance = 0.07f;
    private const float ShieldAbsorb = 20f;
    private const double ShieldDurationMs = 10000d;
    private const float SpeedOrbChance = 0.07f;
    private const float SpeedMultiplier = 1.8f;
    private const double SpeedDurationMs = 8000d;

    private readonly LocalGameSession _session;
    private readonly Dictionary<int, Entity> _entities = new();
    private readonly Dictionary<int, Food> _foods = new();
    private readonly Dictionary<int, Circle> _circles = new();
    private readonly Dictionary<int, Bullet> _bullets = new();
    private readonly Dictionary<int, PlayerAmmo> _playerAmmo = new();
    private readonly Dictionary<int, Shield> _shields = new();
    private readonly Dictionary<int, SpeedBuff> _speedBuffs = new();
    private readonly Dictionary<int, Player> _players = new();
    private readonly Dictionary<int, double> _shootCooldowns = new();
    private readonly Dictionary<int, double> _splitFinishAtMs = new();
    private readonly Dictionary<int, double> _botRespawnAtMs = new();
    private readonly List<int> _botPlayerIds = new();
    private readonly Random _rng = new Random(240518);

    private int _nextEntityId = 1;
    private int _nextPlayerId = 1;
    private int _localPlayerId;
    private int _mainSkinIndex;
    private double _timeMs;
    private double _nextFoodAtMs;
    private double _nextMergeAtMs;
    private double _nextBotThinkAtMs;
    private string _localPlayerName;
    private bool _disposed;

    public LocalGameWorld(LocalGameSession session)
    {
        _session = session;
    }

    public bool IsStarted { get; private set; }
    public double SimulationTimeMs => _timeMs;
    public int LocalPlayerId => _localPlayerId;
    public int FoodCount => _foods.Count;

    public Entity FindEntity(int entityId)
        => _entities.TryGetValue(entityId, out var row) ? row : null;
    public Food FindFood(int entityId)
        => _foods.TryGetValue(entityId, out var row) ? row : null;
    public Circle FindCircle(int entityId)
        => _circles.TryGetValue(entityId, out var row) ? row : null;
    public Bullet FindBullet(int entityId)
        => _bullets.TryGetValue(entityId, out var row) ? row : null;
    public Shield FindShield(int entityId)
        => _shields.TryGetValue(entityId, out var row) ? row : null;
    public SpeedBuff FindSpeedBuff(int playerId)
        => _speedBuffs.TryGetValue(playerId, out var row) ? row : null;
    public PlayerAmmo FindPlayerAmmo(int playerId)
        => _playerAmmo.TryGetValue(playerId, out var row) ? row : null;
    public Player FindPlayerByPlayerId(int playerId)
        => _players.TryGetValue(playerId, out var row) ? row : null;

    public Player FindPlayerByIdentity(Identity identity)
    {
        foreach (var player in _players.Values)
        {
            if (player.Identity == identity)
                return player;
        }
        return null;
    }

    public void StartGame(string playerName, int botCount, int skinIndex)
    {
        if (IsStarted) return;
        IsStarted = true;
        _mainSkinIndex = Mathf.Max(0, skinIndex);
        _localPlayerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();

        for (int i = 0; i < PrewarmFoodCount; i++)
            SpawnFoodOne();

        _localPlayerId = CreatePlayer(_localPlayerName, _mainSkinIndex, false);
        SpawnPlayerCircle(_localPlayerId);

        botCount = Mathf.Clamp(botCount, 0, 12);
        for (int i = 0; i < botCount; i++)
        {
            int botId = CreatePlayer($"Bot {i + 1}", 1 + (i % 4), true);
            _botPlayerIds.Add(botId);
            SpawnPlayerCircle(botId);
        }

        _nextFoodAtMs = _timeMs + 1000d;
        _nextMergeAtMs = _timeMs + 1500d;
        _nextBotThinkAtMs = _timeMs + 250d;
    }

    public void EnterGame(string playerName)
    {
        if (!IsStarted)
        {
            StartGame(playerName, 5, _mainSkinIndex);
            return;
        }

        if (_localPlayerId == 0)
            _localPlayerId = CreatePlayer(playerName, _mainSkinIndex, false);

        UpdatePlayer(_localPlayerId, player =>
        {
            if (!string.IsNullOrWhiteSpace(playerName))
                player.Name = playerName.Trim();
        });

        if (GetPlayerCircles(_localPlayerId).Count == 0)
            SpawnPlayerCircle(_localPlayerId);
    }

    public void UpdatePlayerDir(int playerId, DbVector2 direction)
    {
        UpdatePlayer(playerId, player =>
        {
            player.Dir = new DbVector2(direction.X, direction.Y);
        });
    }

    public void UpdatePlayerSkin(int playerId, int skinIndex)
    {
        if (!_players.ContainsKey(playerId)) return;
        skinIndex = Mathf.Max(0, skinIndex);
        UpdatePlayer(playerId, player => player.SkinIndex = skinIndex);
        foreach (var circle in GetPlayerCircles(playerId))
            UpdateCircle(circle.EntityId, c => c.SkinIndex = skinIndex);
    }

    public void ShootBullet(int playerId, float targetX, float targetY, int bulletType)
    {
        var player = FindPlayerByPlayerId(playerId);
        if (player == null) return;
        if (Mathf.Abs(targetX) < 0.001f && Mathf.Abs(targetY) < 0.001f) return;

        if (_shootCooldowns.TryGetValue(playerId, out var lastShoot)
            && (_timeMs - lastShoot) < BulletCooldownSec * 1000d)
            return;

        float length = Mathf.Sqrt(targetX * targetX + targetY * targetY);
        if (length < 0.001f) return;
        targetX /= length;
        targetY /= length;
        _shootCooldowns[playerId] = _timeMs;

        int splitAmmoLeft = 0;
        if (bulletType == 1)
        {
            var ammo = FindPlayerAmmo(playerId);
            if (ammo == null || ammo.AmmoSplit <= 0) return;
            splitAmmoLeft = ammo.AmmoSplit;
        }

        int bulletCount = 0;
        foreach (var bullet in _bullets.Values)
        {
            if (bullet.OwnerPlayerId == playerId)
                bulletCount++;
        }

        var playerCircles = GetPlayerCircles(playerId);
        int ballCount = 0;
        foreach (var circle in playerCircles)
        {
            if (!circle.IsMerging)
                ballCount++;
        }

        int maxBullets = Mathf.Clamp(ballCount * 3, 5, 24);
        foreach (var circle in playerCircles)
        {
            if (circle.IsMerging) continue;
            var entity = FindEntity(circle.EntityId);
            if (entity == null || entity.Mass < MinShootMass) continue;
            if (bulletCount >= maxBullets) break;
            if (bulletType == 1 && splitAmmoLeft <= 0) break;

            bulletCount++;
            if (bulletType == 1)
                splitAmmoLeft--;

            UpdateEntity(circle.EntityId, e =>
            {
                e.Mass = Mathf.Max(0.1f, e.Mass - BulletMassCost);
                UpdateHpAfterMassChange(e);
            });

            var bulletEntity = InsertEntity(new Entity
            {
                Mass = 0.3f,
                Position = new DbVector2(entity.Position.X, entity.Position.Y),
                Hp = 0,
                MaxHp = 0
            });
            InsertBullet(new Bullet
            {
                EntityId = bulletEntity.Id,
                OwnerPlayerId = playerId,
                DirX = targetX,
                DirY = targetY,
                SpawnedAtMs = _timeMs,
                BulletType = bulletType
            });
        }

        if (bulletType == 1)
        {
            UpdatePlayerAmmo(playerId, ammo =>
            {
                ammo.AmmoSplit = splitAmmoLeft;
                return ammo;
            }, !_playerAmmo.ContainsKey(playerId));
        }
    }

    public void SplitPlayer(int playerId)
    {
        if (!_players.ContainsKey(playerId)) return;
        var player = FindPlayerByPlayerId(playerId);
        var playerCircles = GetPlayerCircles(playerId);
        playerCircles.Sort((a, b) =>
        {
            var ea = FindEntity(a.EntityId);
            var eb = FindEntity(b.EntityId);
            return (eb != null ? eb.Mass : 0f).CompareTo(ea != null ? ea.Mass : 0f);
        });

        int remainingSlots = MaxCirclesPerPlayer - playerCircles.Count;
        if (remainingSlots <= 0) return;

        int splitCount = 0;
        foreach (var circle in playerCircles)
        {
            if (splitCount >= remainingSlots) break;
            var entity = FindEntity(circle.EntityId);
            if (entity == null || entity.Mass < MinSplitMass) continue;

            float halfMass = entity.Mass * 0.5f;
            float halfHp = entity.Hp * 0.5f;
            UpdateEntity(circle.EntityId, e =>
            {
                e.Mass = halfMass;
                e.Hp = Mathf.Max(halfHp, halfMass * HpMinRatio);
                e.MaxHp = ComputeMaxHp(halfMass);
            });

            float dirX = player.Dir.X;
            float dirY = player.Dir.Y;
            if (dirX == 0f && dirY == 0f) dirX = 1f;
            float length = Mathf.Sqrt(dirX * dirX + dirY * dirY);
            dirX /= length;
            dirY /= length;
            float offset = MassToDiameter(halfMass) + 1f;

            var newEntity = InsertEntity(new Entity
            {
                Mass = halfMass,
                Position = new DbVector2(
                    Mathf.Clamp(entity.Position.X + dirX * offset, 0.1f, WorldSize - 0.1f),
                    Mathf.Clamp(entity.Position.Y + dirY * offset, 0.1f, WorldSize - 0.1f)),
                Hp = Mathf.Max(halfHp, halfMass * HpMinRatio),
                MaxHp = ComputeMaxHp(halfMass)
            });

            var newCircle = new Circle
            {
                EntityId = newEntity.Id,
                PlayerId = playerId,
                TouchStartMs = 0,
                IsMerging = false,
                IsSplitting = true,
                SplitFromEntityId = entity.Id,
                SkinIndex = circle.SkinIndex
            };
            InsertCircle(newCircle);
            _splitFinishAtMs[newCircle.EntityId] = _timeMs + 320d;
            splitCount++;
        }
    }

    public void FinishMerge(int playerId, int smallEntityId)
    {
        var circle = FindCircle(smallEntityId);
        if (circle == null) return;
        DeleteCircle(smallEntityId);
        DeleteEntity(smallEntityId);
    }

    public void FinishSplitAnimation(int playerId, int entityId)
    {
        var circle = FindCircle(entityId);
        if (circle == null || !circle.IsSplitting) return;
        UpdateCircle(entityId, c => c.IsSplitting = false);
    }

    public void DebugDamage(int entityId, float amount)
    {
        var entity = FindEntity(entityId);
        if (entity == null) return;
        ApplyDamage(entityId, Mathf.Max(0f, amount), new HashSet<int>());
    }

    public void Tick()
    {
        if (_disposed || !IsStarted) return;
        _timeMs += ServerDelta * 1000d;

        RespawnBotsIfNeeded();
        if (_timeMs >= _nextBotThinkAtMs)
        {
            UpdateBotDecisions();
            _nextBotThinkAtMs += 200d;
        }

        MovePlayers();
        ResolveEating();
        TickBullets();
        ExpireBuffs();

        if (_timeMs >= _nextMergeAtMs)
        {
            MergePlayerCheck();
            _nextMergeAtMs += 1500d;
        }

        if (_timeMs >= _nextFoodAtMs)
        {
            SpawnFoodToTarget();
            _nextFoodAtMs += 1000d;
        }

        FinishSplitAnimations();
    }

    public void Dispose()
    {
        _disposed = true;
        _entities.Clear();
        _foods.Clear();
        _circles.Clear();
        _bullets.Clear();
        _playerAmmo.Clear();
        _shields.Clear();
        _speedBuffs.Clear();
        _players.Clear();
        _shootCooldowns.Clear();
        _splitFinishAtMs.Clear();
        _botRespawnAtMs.Clear();
        _botPlayerIds.Clear();
    }

    private int CreatePlayer(string name, int skinIndex, bool isBot)
    {
        int playerId = _nextPlayerId++;
        var player = new Player
        {
            Identity = default,
            PlayerId = playerId,
            Name = string.IsNullOrWhiteSpace(name) ? $"Player {playerId}" : name.Trim(),
            Dir = new DbVector2(0, 0),
            RespawnMass = 0,
            InvincibleUntilMs = _timeMs + 5000d,
            SkinIndex = Mathf.Max(0, skinIndex)
        };
        _players[playerId] = player;
        _session.RaisePlayerInsert(ClonePlayer(player));

        if (!isBot && _localPlayerId == 0)
            _localPlayerId = playerId;
        return playerId;
    }

    private void SpawnPlayerCircle(int playerId)
    {
        var player = FindPlayerByPlayerId(playerId);
        if (player == null) return;

        float startMass = player.RespawnMass > 0
            ? Mathf.Max(PrimaryPlayerMass, (float)player.RespawnMass)
            : PrimaryPlayerMass;
        UpdatePlayer(playerId, p =>
        {
            p.RespawnMass = 0;
            p.InvincibleUntilMs = _timeMs + 5000d;
        });

        var entity = InsertEntity(new Entity
        {
            Mass = startMass,
            Position = SpawnPositionForPlayer(playerId),
            Hp = ComputeMaxHp(startMass),
            MaxHp = ComputeMaxHp(startMass)
        });
        InsertCircle(new Circle
        {
            EntityId = entity.Id,
            PlayerId = playerId,
            TouchStartMs = 0,
            IsMerging = false,
            IsSplitting = false,
            SplitFromEntityId = 0,
            SkinIndex = player.SkinIndex
        });
    }

    private DbVector2 SpawnPositionForPlayer(int playerId)
    {
        float angle = playerId * 2.399963f + (float)(_rng.NextDouble() * 0.35d);
        float radius = 24f + (float)(_rng.NextDouble() * 12d);
        return new DbVector2(
            Mathf.Clamp(50f + Mathf.Cos(angle) * radius, 4f, WorldSize - 4f),
            Mathf.Clamp(50f + Mathf.Sin(angle) * radius, 4f, WorldSize - 4f));
    }

    private void MovePlayers()
    {
        foreach (var circle in SnapshotCircles())
        {
            if (circle.IsMerging) continue;
            var entity = FindEntity(circle.EntityId);
            var player = FindPlayerByPlayerId(circle.PlayerId);
            if (entity == null || player == null) continue;

            float speedScale = 1f / (entity.Mass * SpeedDecayCoeff + 1f);
            speedScale = Mathf.Max(speedScale, MinSpeedScale);
            float step = ServerDelta * StartPlayerSpeed * speedScale;
            if (FindSpeedBuff(circle.PlayerId) != null)
                step *= SpeedMultiplier;

            UpdateEntity(circle.EntityId, e =>
            {
                e.Position.X += player.Dir.X * step;
                e.Position.Y += player.Dir.Y * step;
                ClampEntity(e);
            });
        }

        SeparateOwnedBalls();
        PullIdleOwnedBalls();
    }

    private void SeparateOwnedBalls()
    {
        var byPlayer = BuildPlayerBalls();
        foreach (var entry in byPlayer)
        {
            var balls = entry.Value;
            if (balls.Count <= 1) continue;
            for (int i = 0; i < balls.Count; i++)
            {
                var a = balls[i];
                var ea = FindEntity(a.EntityId);
                if (ea == null) continue;
                for (int j = i + 1; j < balls.Count; j++)
                {
                    var b = balls[j];
                    var eb = FindEntity(b.EntityId);
                    if (eb == null) continue;
                    float dx = ea.Position.X - eb.Position.X;
                    float dy = ea.Position.Y - eb.Position.Y;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float minDist = (MassToDiameter(ea.Mass) + MassToDiameter(eb.Mass)) * 0.5f;
                    if (dist >= minDist || dist < 0.001f) continue;

                    float overlap = minDist - dist;
                    float nx = dx / dist;
                    float ny = dy / dist;
                    float totalMass = Mathf.Max(0.001f, ea.Mass + eb.Mass);
                    float aShare = eb.Mass / totalMass;
                    float bShare = ea.Mass / totalMass;
                    float push = overlap * SeparationFactor;

                    UpdateEntity(ea.Id, e =>
                    {
                        e.Position.X += nx * push * aShare;
                        e.Position.Y += ny * push * aShare;
                        ClampEntity(e);
                    });
                    UpdateEntity(eb.Id, e =>
                    {
                        e.Position.X -= nx * push * bShare;
                        e.Position.Y -= ny * push * bShare;
                        ClampEntity(e);
                    });
                    ea = FindEntity(a.EntityId);
                    eb = FindEntity(b.EntityId);
                }
            }
        }
    }

    private void PullIdleOwnedBalls()
    {
        foreach (var entry in BuildPlayerBalls())
        {
            var player = FindPlayerByPlayerId(entry.Key);
            if (player == null) continue;
            if (Mathf.Abs(player.Dir.X) >= 0.01f || Mathf.Abs(player.Dir.Y) >= 0.01f) continue;

            var balls = entry.Value;
            if (balls.Count <= 1) continue;
            float centerX = 0f;
            float centerY = 0f;
            foreach (var circle in balls)
            {
                var entity = FindEntity(circle.EntityId);
                if (entity == null) continue;
                centerX += entity.Position.X;
                centerY += entity.Position.Y;
            }
            centerX /= balls.Count;
            centerY /= balls.Count;

            foreach (var circle in balls)
            {
                var entity = FindEntity(circle.EntityId);
                if (entity == null) continue;
                float speedScale = 1f / (entity.Mass * 0.05f + 1f);
                float pull = 0.018f * speedScale;
                bool canMove = true;
                foreach (var otherCircle in balls)
                {
                    if (otherCircle.EntityId == circle.EntityId) continue;
                    var other = FindEntity(otherCircle.EntityId);
                    if (other == null) continue;
                    float dist = Mathf.Sqrt(
                        Mathf.Pow(entity.Position.X - other.Position.X, 2f) +
                        Mathf.Pow(entity.Position.Y - other.Position.Y, 2f));
                    float minDist = (MassToDiameter(entity.Mass) + MassToDiameter(other.Mass)) * 0.5f;
                    if (dist <= minDist + 0.02f)
                    {
                        canMove = false;
                        break;
                    }
                }
                if (!canMove) continue;

                UpdateEntity(circle.EntityId, e =>
                {
                    e.Position.X += (centerX - e.Position.X) * pull;
                    e.Position.Y += (centerY - e.Position.Y) * pull;
                    ClampEntity(e);
                });
            }
        }
    }

    private void ResolveEating()
    {
        var massGains = new Dictionary<int, float>();
        var healGains = new Dictionary<int, float>();
        var splitAmmoGains = new Dictionary<int, int>();
        var shieldGains = new Dictionary<int, float>();
        var speedBuffGains = new HashSet<int>();
        var entitiesToDelete = new HashSet<int>();
        var playerBalls = BuildPlayerBalls();
        var invinciblePlayers = new HashSet<int>();

        foreach (var player in _players.Values)
        {
            if (player.InvincibleUntilMs > 0 && player.InvincibleUntilMs > _timeMs)
                invinciblePlayers.Add(player.PlayerId);
        }

        foreach (var entry in playerBalls)
        {
            int playerId = entry.Key;
            foreach (var circle in entry.Value)
            {
                if (entitiesToDelete.Contains(circle.EntityId)) continue;
                var eater = FindEntity(circle.EntityId);
                if (eater == null) continue;

                foreach (var candidate in SnapshotEntities())
                {
                    if (candidate.Id == eater.Id || entitiesToDelete.Contains(candidate.Id)) continue;
                    bool isFood = _foods.TryGetValue(candidate.Id, out var food);
                    bool isPlayerBall = _circles.TryGetValue(candidate.Id, out var victimCircle);
                    if (isPlayerBall && victimCircle.PlayerId == playerId) continue;
                    if (!IsOverLapping(eater, candidate)) continue;
                    if (!isFood && (!isPlayerBall || eater.Mass <= candidate.Mass)) continue;
                    if (isPlayerBall && invinciblePlayers.Contains(victimCircle.PlayerId)) continue;

                    entitiesToDelete.Add(candidate.Id);
                    massGains.TryGetValue(eater.Id, out float gainedMass);
                    massGains[eater.Id] = gainedMass + candidate.Mass;

                    if (isFood && food.FoodType == 1)
                    {
                        healGains.TryGetValue(eater.Id, out float heal);
                        healGains[eater.Id] = heal + HealthOrbHeal;
                    }
                    else if (isFood && food.FoodType == 2)
                    {
                        splitAmmoGains.TryGetValue(playerId, out int ammo);
                        splitAmmoGains[playerId] = ammo + 1;
                    }
                    else if (isFood && food.FoodType == 3)
                    {
                        foreach (var ownedCircle in entry.Value)
                        {
                            shieldGains.TryGetValue(ownedCircle.EntityId, out float shield);
                            shieldGains[ownedCircle.EntityId] = shield + ShieldAbsorb;
                        }
                    }
                    else if (isFood && food.FoodType == 4)
                    {
                        speedBuffGains.Add(playerId);
                    }
                }
            }
        }

        foreach (var entry in massGains)
        {
            if (FindEntity(entry.Key) == null) continue;
            UpdateEntity(entry.Key, entity =>
            {
                entity.Mass += entry.Value;
                UpdateHpAfterMassChange(entity);
            });
        }

        foreach (var entry in healGains)
        {
            if (FindEntity(entry.Key) == null) continue;
            UpdateEntity(entry.Key, entity => entity.Hp = Mathf.Min(entity.Hp + entry.Value, entity.MaxHp));
        }

        foreach (var entry in splitAmmoGains)
            AddSplitAmmo(entry.Key, entry.Value);

        foreach (var entry in shieldGains)
            ApplyShield(entry.Key, entry.Value);

        foreach (int playerId in speedBuffGains)
            ApplySpeedBuff(playerId);

        foreach (int deadId in entitiesToDelete)
            DeleteEntityWithRelations(deadId);
    }

    private void TickBullets()
    {
        var bulletsToDelete = new HashSet<int>();
        var bulletHits = new Dictionary<int, float>();
        var splitHits = new HashSet<int>();

        foreach (var bullet in SnapshotBullets())
        {
            var bulletEntity = FindEntity(bullet.EntityId);
            if (bulletEntity == null)
            {
                bulletsToDelete.Add(bullet.EntityId);
                continue;
            }
            if (_timeMs - bullet.SpawnedAtMs >= BulletLifetimeMs)
            {
                bulletsToDelete.Add(bullet.EntityId);
                continue;
            }

            float originalX = bulletEntity.Position.X;
            float originalY = bulletEntity.Position.Y;
            UpdateEntity(bullet.EntityId, entity =>
            {
                entity.Position.X += bullet.DirX * ServerDelta * BulletSpeed;
                entity.Position.Y += bullet.DirY * ServerDelta * BulletSpeed;
                ClampEntity(entity);
            });
            bulletEntity = FindEntity(bullet.EntityId);
            if (bulletEntity == null) continue;
            float midX = (originalX + bulletEntity.Position.X) * 0.5f;
            float midY = (originalY + bulletEntity.Position.Y) * 0.5f;

            foreach (var circle in SnapshotCircles())
            {
                if (circle.IsMerging || circle.PlayerId == bullet.OwnerPlayerId) continue;
                var target = FindEntity(circle.EntityId);
                if (target == null) continue;
                float radius = MassToDiameter(target.Mass) * 0.5f;
                float radiusSq = radius * radius;
                if (PointInside(originalX, originalY, target, radiusSq)
                    || PointInside(midX, midY, target, radiusSq)
                    || PointInside(bulletEntity.Position.X, bulletEntity.Position.Y, target, radiusSq))
                {
                    bulletsToDelete.Add(bullet.EntityId);
                    if (bullet.BulletType == 1)
                    {
                        splitHits.Add(circle.EntityId);
                    }
                    else
                    {
                        bulletHits.TryGetValue(circle.EntityId, out float damage);
                        bulletHits[circle.EntityId] = damage + BulletDamage;
                    }
                    break;
                }
            }
        }

        var deadEntities = new HashSet<int>();
        foreach (var entry in bulletHits)
        {
            if (FindEntity(entry.Key) == null) continue;
            ApplyDamage(entry.Key, entry.Value, deadEntities);
        }

        foreach (int targetEntityId in splitHits)
            ForceSplitVictim(targetEntityId);

        foreach (int bulletId in bulletsToDelete)
        {
            DeleteBullet(bulletId);
            DeleteEntity(bulletId);
        }
    }

    private void ApplyDamage(int entityId, float damage, HashSet<int> deadEntities)
    {
        var target = FindEntity(entityId);
        if (target == null) return;
        var circle = FindCircle(entityId);
        if (circle != null)
        {
            var player = FindPlayerByPlayerId(circle.PlayerId);
            if (player != null && player.InvincibleUntilMs > _timeMs)
                return;
        }

        var shield = FindShield(entityId);
        if (shield != null)
        {
            float absorbed = Mathf.Min(shield.ShieldHp, damage);
            float remainingShield = shield.ShieldHp - absorbed;
            damage -= absorbed;
            if (remainingShield <= 0f)
            {
                DeleteShield(entityId);
            }
            else
            {
                UpdateShield(entityId, s => s.ShieldHp = remainingShield);
            }
        }

        bool killed = false;
        UpdateEntity(entityId, entity =>
        {
            entity.Hp = Mathf.Clamp(entity.Hp - damage, 0f, entity.MaxHp);
            killed = entity.Hp <= 0f;
        });
        if (killed && deadEntities.Add(entityId))
            KillPlayerBall(entityId);
    }

    private void KillPlayerBall(int targetEntityId)
    {
        var target = FindEntity(targetEntityId);
        var circle = FindCircle(targetEntityId);
        if (target == null || circle == null) return;

        var playerCircles = GetPlayerCircles(circle.PlayerId);
        float totalMass = 0f;
        foreach (var ownedCircle in playerCircles)
        {
            var ownedEntity = FindEntity(ownedCircle.EntityId);
            if (ownedEntity != null)
                totalMass += ownedEntity.Mass;
        }
        UpdatePlayer(circle.PlayerId, player =>
        {
            player.RespawnMass = Mathf.Max(PrimaryPlayerMass, totalMass * 0.1f);
        });

        int dropCount = Mathf.Clamp(3 + (int)(target.Mass * 0.05f), 3, 8);
        for (int i = 0; i < dropCount; i++)
        {
            float rx = (float)(_rng.NextDouble() - 0.5d) * 3f;
            float ry = (float)(_rng.NextDouble() - 0.5d) * 3f;
            InsertFoodEntity(
                Mathf.Max(0.2f, target.Mass * 0.2f / dropCount),
                new DbVector2(
                    Mathf.Clamp(target.Position.X + rx, 0.2f, WorldSize - 0.2f),
                    Mathf.Clamp(target.Position.Y + ry, 0.2f, WorldSize - 0.2f)),
                0);
        }

        DeleteCircle(targetEntityId);
        DeleteEntity(targetEntityId);
        if (_botPlayerIds.Contains(circle.PlayerId))
            _botRespawnAtMs[circle.PlayerId] = _timeMs + 2000d;
    }

    private void ForceSplitVictim(int targetEntityId)
    {
        var circle = FindCircle(targetEntityId);
        var entity = FindEntity(targetEntityId);
        if (circle == null || entity == null || entity.Mass < MinSplitMass) return;
        if (GetPlayerCircles(circle.PlayerId).Count >= MaxCirclesPerPlayer) return;

        float halfMass = entity.Mass * 0.5f;
        float halfHp = entity.Hp * 0.5f;
        UpdateEntity(targetEntityId, e =>
        {
            e.Mass = halfMass;
            e.Hp = Mathf.Max(halfHp, halfMass * HpMinRatio);
            e.MaxHp = ComputeMaxHp(halfMass);
        });

        float angle = (float)(_rng.NextDouble() * Mathf.PI * 2f);
        float dx = Mathf.Cos(angle);
        float dy = Mathf.Sin(angle);
        float offset = MassToDiameter(halfMass) + 1f;
        var newEntity = InsertEntity(new Entity
        {
            Mass = halfMass,
            Position = new DbVector2(
                Mathf.Clamp(entity.Position.X + dx * offset, 0.1f, WorldSize - 0.1f),
                Mathf.Clamp(entity.Position.Y + dy * offset, 0.1f, WorldSize - 0.1f)),
            Hp = Mathf.Max(halfHp, halfMass * HpMinRatio),
            MaxHp = ComputeMaxHp(halfMass)
        });
        InsertCircle(new Circle
        {
            EntityId = newEntity.Id,
            PlayerId = circle.PlayerId,
            TouchStartMs = 0,
            IsMerging = false,
            IsSplitting = true,
            SplitFromEntityId = targetEntityId,
            SkinIndex = circle.SkinIndex
        });
        _splitFinishAtMs[newEntity.Id] = _timeMs + 320d;
    }

    private void MergePlayerCheck()
    {
        foreach (int playerId in new List<int>(_players.Keys))
        {
            var list = GetPlayerCircles(playerId);
            if (list.Count <= 1) continue;

            var entityById = new Dictionary<int, Entity>();
            foreach (var circle in list)
            {
                var entity = FindEntity(circle.EntityId);
                if (entity != null)
                    entityById[circle.EntityId] = entity;
            }

            for (int i = 0; i < list.Count; i++)
            {
                var circle = list[i];
                if (circle.IsMerging || !entityById.TryGetValue(circle.EntityId, out var entity)) continue;
                bool touching = false;
                for (int j = 0; j < list.Count; j++)
                {
                    if (i == j) continue;
                    var otherCircle = list[j];
                    if (otherCircle.IsMerging || !entityById.TryGetValue(otherCircle.EntityId, out var other)) continue;
                    float radiusA = MassToDiameter(entity.Mass) * 0.5f;
                    float radiusB = MassToDiameter(other.Mass) * 0.5f;
                    float dx = entity.Position.X - other.Position.X;
                    float dy = entity.Position.Y - other.Position.Y;
                    if (Mathf.Sqrt(dx * dx + dy * dy) <= radiusA + radiusB + 0.03f)
                    {
                        touching = true;
                        break;
                    }
                }
                double touchStart = touching
                    ? (circle.TouchStartMs > 0 ? circle.TouchStartMs : _timeMs)
                    : 0d;
                if (touchStart != circle.TouchStartMs)
                    UpdateCircle(circle.EntityId, c => c.TouchStartMs = touchStart);
            }

            list = GetPlayerCircles(playerId);
            entityById.Clear();
            foreach (var circle in list)
            {
                var entity = FindEntity(circle.EntityId);
                if (entity != null)
                    entityById[circle.EntityId] = entity;
            }

            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a.IsMerging || !entityById.TryGetValue(a.EntityId, out var ea)) continue;
                float needA = BaseMergeSec + Mathf.Sqrt(ea.Mass) * SqrtDelayCoeff;
                if (a.TouchStartMs <= 0 || _timeMs - a.TouchStartMs < needA * 1000d) continue;

                for (int j = 0; j < list.Count; j++)
                {
                    if (i == j) continue;
                    var b = list[j];
                    if (b.IsMerging || !entityById.TryGetValue(b.EntityId, out var eb)) continue;
                    float needB = BaseMergeSec + Mathf.Sqrt(eb.Mass) * SqrtDelayCoeff;
                    if (b.TouchStartMs <= 0 || _timeMs - b.TouchStartMs < needB * 1000d) continue;

                    float radiusA = MassToDiameter(ea.Mass) * 0.5f;
                    float radiusB = MassToDiameter(eb.Mass) * 0.5f;
                    float dx = ea.Position.X - eb.Position.X;
                    float dy = ea.Position.Y - eb.Position.Y;
                    if (Mathf.Sqrt(dx * dx + dy * dy) > radiusA + radiusB + 0.03f) continue;

                    int bigId = ea.Mass >= eb.Mass ? a.EntityId : b.EntityId;
                    int smallId = bigId == a.EntityId ? b.EntityId : a.EntityId;
                    var big = entityById[bigId];
                    var small = entityById[smallId];
                    float mergedMass = big.Mass + small.Mass;
                    float mergedHp = big.Hp + small.Hp;
                    UpdateEntity(bigId, entity =>
                    {
                        entity.Mass = mergedMass;
                        entity.MaxHp = ComputeMaxHp(mergedMass);
                        entity.Hp = Mathf.Min(mergedHp, entity.MaxHp);
                    });
                    UpdateCircle(smallId, circle =>
                    {
                        circle.IsMerging = true;
                        circle.TouchStartMs = 0;
                        circle.SplitFromEntityId = bigId;
                    });
                    return;
                }
            }
        }
    }

    private void ExpireBuffs()
    {
        foreach (int entityId in new List<int>(_shields.Keys))
        {
            var shield = FindShield(entityId);
            if (shield != null && _timeMs >= shield.ExpireAtMs)
                DeleteShield(entityId);
        }
        foreach (int playerId in new List<int>(_speedBuffs.Keys))
        {
            var buff = FindSpeedBuff(playerId);
            if (buff != null && _timeMs >= buff.ExpireAtMs)
                DeleteSpeedBuff(playerId);
        }
    }

    private void FinishSplitAnimations()
    {
        if (_splitFinishAtMs.Count == 0) return;
        var completed = new List<int>();
        foreach (var entry in _splitFinishAtMs)
        {
            if (_timeMs < entry.Value) continue;
            var circle = FindCircle(entry.Key);
            if (circle != null && circle.IsSplitting)
                UpdateCircle(entry.Key, c => c.IsSplitting = false);
            completed.Add(entry.Key);
        }
        foreach (int entityId in completed)
            _splitFinishAtMs.Remove(entityId);
    }

    private void SpawnFoodToTarget()
    {
        float totalMass = 0f;
        foreach (var entity in _entities.Values)
            totalMass += entity.Mass;
        int target = BaseFoodCount + (int)(totalMass * FoodMassRatio);
        for (int i = 0; i < 5 && _foods.Count < target; i++)
            SpawnFoodOne();
    }

    private void SpawnFoodOne()
    {
        float mass = 1f + (float)_rng.NextDouble();
        double roll = _rng.NextDouble();
        int foodType;
        if (roll < HealthOrbChance)
        {
            foodType = 1;
            mass = 2f;
        }
        else if (roll < HealthOrbChance + SplitOrbChance)
        {
            foodType = 2;
            mass = 2f;
        }
        else if (roll < HealthOrbChance + SplitOrbChance + ShieldOrbChance)
        {
            foodType = 3;
            mass = 2f;
        }
        else if (roll < HealthOrbChance + SplitOrbChance + ShieldOrbChance + SpeedOrbChance)
        {
            foodType = 4;
            mass = 2f;
        }
        else
        {
            foodType = 0;
        }

        InsertFoodEntity(mass, new DbVector2(
            1f + (float)_rng.NextDouble() * (WorldSize - 2f),
            1f + (float)_rng.NextDouble() * (WorldSize - 2f)), foodType);
    }

    private void InsertFoodEntity(float mass, DbVector2 position, int foodType)
    {
        var entity = InsertEntity(new Entity
        {
            Mass = mass,
            Position = position,
            Hp = 0,
            MaxHp = 0
        });
        InsertFood(new Food { EntityId = entity.Id, FoodType = foodType });
    }

    private void UpdateBotDecisions()
    {
        foreach (int playerId in _botPlayerIds)
        {
            var circles = GetPlayerCircles(playerId);
            if (circles.Count == 0) continue;
            var player = FindPlayerByPlayerId(playerId);
            if (player == null) continue;

            float ownMass = 0f;
            float ownX = 0f;
            float ownY = 0f;
            foreach (var circle in circles)
            {
                var entity = FindEntity(circle.EntityId);
                if (entity == null) continue;
                ownMass += entity.Mass;
                ownX += entity.Position.X;
                ownY += entity.Position.Y;
            }
            ownX /= Mathf.Max(1, circles.Count);
            ownY /= Mathf.Max(1, circles.Count);

            int dangerPlayerId = 0;
            float dangerMass = 0f;
            float dangerX = 0f;
            float dangerY = 0f;
            int preyPlayerId = 0;
            float preyDistance = float.MaxValue;
            float preyX = 0f;
            float preyY = 0f;

            foreach (var other in _players.Values)
            {
                if (other.PlayerId == playerId) continue;
                foreach (var otherCircle in GetPlayerCircles(other.PlayerId))
                {
                    var otherEntity = FindEntity(otherCircle.EntityId);
                    if (otherEntity == null) continue;
                    float dx = otherEntity.Position.X - ownX;
                    float dy = otherEntity.Position.Y - ownY;
                    float distSq = dx * dx + dy * dy;
                    if (otherEntity.Mass > ownMass * 1.15f && distSq < 18f * 18f)
                    {
                        if (dangerPlayerId == 0 || otherEntity.Mass > dangerMass)
                        {
                            dangerPlayerId = other.PlayerId;
                            dangerMass = otherEntity.Mass;
                            dangerX = otherEntity.Position.X;
                            dangerY = otherEntity.Position.Y;
                        }
                    }
                    var otherPlayer = FindPlayerByPlayerId(other.PlayerId);
                    if (otherPlayer != null && otherPlayer.InvincibleUntilMs > _timeMs)
                        continue;
                    if (otherEntity.Mass < ownMass * 0.8f && distSq < preyDistance && distSq < 18f * 18f)
                    {
                        preyPlayerId = other.PlayerId;
                        preyDistance = distSq;
                        preyX = otherEntity.Position.X;
                        preyY = otherEntity.Position.Y;
                    }
                }
            }

            float dirX;
            float dirY;
            if (dangerPlayerId != 0)
            {
                dirX = ownX - dangerX;
                dirY = ownY - dangerY;
            }
            else if (preyPlayerId != 0)
            {
                dirX = preyX - ownX;
                dirY = preyY - ownY;
            }
            else
            {
                var food = FindNearestFood(ownX, ownY);
                if (food != null)
                {
                    dirX = food.Position.X - ownX;
                    dirY = food.Position.Y - ownY;
                }
                else
                {
                    dirX = Mathf.Cos((float)(_timeMs * 0.001 + playerId));
                    dirY = Mathf.Sin((float)(_timeMs * 0.0013 + playerId));
                }
            }

            float length = Mathf.Sqrt(dirX * dirX + dirY * dirY);
            if (length < 0.001f)
            {
                dirX = 1f;
                dirY = 0f;
            }
            else
            {
                dirX /= length;
                dirY /= length;
            }
            UpdatePlayerDir(playerId, new DbVector2(dirX, dirY));

            if (_timeMs > 10000d && preyPlayerId != 0 && _rng.NextDouble() < 0.12d)
                ShootBullet(playerId, preyX - ownX, preyY - ownY, 0);

            if (_timeMs > 16000d && preyPlayerId != 0 && ownMass >= MinSplitMass
                && preyDistance > 6f * 6f && preyDistance < 18f * 18f
                && _rng.NextDouble() < 0.012d)
                SplitPlayer(playerId);
        }
    }

    private Entity FindNearestFood(float x, float y)
    {
        Entity best = null;
        float bestDistance = float.MaxValue;
        foreach (int foodId in _foods.Keys)
        {
            var entity = FindEntity(foodId);
            if (entity == null) continue;
            float dx = entity.Position.X - x;
            float dy = entity.Position.Y - y;
            float distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = entity;
        }
        return best;
    }

    private void RespawnBotsIfNeeded()
    {
        foreach (int playerId in _botPlayerIds)
        {
            if (GetPlayerCircles(playerId).Count > 0) continue;
            if (!_botRespawnAtMs.TryGetValue(playerId, out double respawnAt) || _timeMs < respawnAt)
                continue;
            _botRespawnAtMs.Remove(playerId);
            SpawnPlayerCircle(playerId);
        }
    }

    private void AddSplitAmmo(int playerId, int amount)
    {
        var existing = FindPlayerAmmo(playerId);
        if (existing == null)
        {
            InsertPlayerAmmo(new PlayerAmmo
            {
                PlayerId = playerId,
                AmmoSplit = Mathf.Min(amount, MaxSplitAmmo)
            });
            return;
        }
        UpdatePlayerAmmo(playerId, ammo =>
        {
            ammo.AmmoSplit = Mathf.Min(ammo.AmmoSplit + amount, MaxSplitAmmo);
            return ammo;
        });
    }

    private void ApplyShield(int entityId, float amount)
    {
        var existing = FindShield(entityId);
        if (existing == null)
        {
            InsertShield(new Shield
            {
                EntityId = entityId,
                ShieldHp = amount,
                ShieldMax = amount,
                ExpireAtMs = _timeMs + ShieldDurationMs
            });
            return;
        }
        UpdateShield(entityId, shield =>
        {
            shield.ShieldHp += amount;
            shield.ShieldMax += amount;
            shield.ExpireAtMs = _timeMs + ShieldDurationMs;
        });
    }

    private void ApplySpeedBuff(int playerId)
    {
        var existing = FindSpeedBuff(playerId);
        if (existing == null)
        {
            InsertSpeedBuff(new SpeedBuff
            {
                PlayerId = playerId,
                ExpireAtMs = _timeMs + SpeedDurationMs
            });
            return;
        }
        UpdateSpeedBuff(playerId, buff => buff.ExpireAtMs = _timeMs + SpeedDurationMs);
    }

    private List<Circle> GetPlayerCircles(int playerId)
    {
        var result = new List<Circle>();
        foreach (var circle in _circles.Values)
        {
            if (circle.PlayerId == playerId)
                result.Add(circle);
        }
        return result;
    }

    private Dictionary<int, List<Circle>> BuildPlayerBalls()
    {
        var result = new Dictionary<int, List<Circle>>();
        foreach (var circle in _circles.Values)
        {
            if (circle.IsMerging) continue;
            if (!result.TryGetValue(circle.PlayerId, out var list))
            {
                list = new List<Circle>();
                result[circle.PlayerId] = list;
            }
            list.Add(circle);
        }
        return result;
    }

    private List<Circle> SnapshotCircles() => new List<Circle>(_circles.Values);
    private List<Bullet> SnapshotBullets() => new List<Bullet>(_bullets.Values);
    private List<Entity> SnapshotEntities() => new List<Entity>(_entities.Values);

    private Entity InsertEntity(Entity entity)
    {
        entity.Id = _nextEntityId++;
        if (entity.Position == null) entity.Position = new DbVector2();
        _entities[entity.Id] = entity;
        _session.RaiseEntityInsert(CloneEntity(entity));
        return entity;
    }

    private void UpdateEntity(int entityId, Action<Entity> mutate)
    {
        if (!_entities.TryGetValue(entityId, out var current)) return;
        var oldRow = CloneEntity(current);
        var newRow = CloneEntity(current);
        mutate(newRow);
        _entities[entityId] = newRow;
        _session.RaiseEntityUpdate(oldRow, CloneEntity(newRow));
    }

    private void DeleteEntity(int entityId)
    {
        if (!_entities.TryGetValue(entityId, out var row)) return;
        _entities.Remove(entityId);
        _session.RaiseEntityDelete(CloneEntity(row));
    }

    private void InsertFood(Food food)
    {
        _foods[food.EntityId] = food;
        _session.RaiseFoodInsert(CloneFood(food));
    }

    private void DeleteFood(int entityId)
    {
        if (!_foods.TryGetValue(entityId, out var row)) return;
        _foods.Remove(entityId);
        _session.RaiseFoodDelete(CloneFood(row));
    }

    private void InsertCircle(Circle circle)
    {
        _circles[circle.EntityId] = circle;
        _session.RaiseCircleInsert(CloneCircle(circle));
    }

    private void UpdateCircle(int entityId, Action<Circle> mutate)
    {
        if (!_circles.TryGetValue(entityId, out var current)) return;
        var oldRow = CloneCircle(current);
        var newRow = CloneCircle(current);
        mutate(newRow);
        _circles[entityId] = newRow;
        _session.RaiseCircleUpdate(oldRow, CloneCircle(newRow));
    }

    private void DeleteCircle(int entityId)
    {
        if (!_circles.TryGetValue(entityId, out var row)) return;
        _circles.Remove(entityId);
        _session.RaiseCircleDelete(CloneCircle(row));
        if (_botPlayerIds.Contains(row.PlayerId)
            && GetPlayerCircles(row.PlayerId).Count == 0
            && !_botRespawnAtMs.ContainsKey(row.PlayerId))
        {
            _botRespawnAtMs[row.PlayerId] = _timeMs + 2000d;
        }
    }

    private void InsertBullet(Bullet bullet)
    {
        _bullets[bullet.EntityId] = bullet;
        _session.RaiseBulletInsert(CloneBullet(bullet));
    }

    private void DeleteBullet(int entityId)
    {
        if (!_bullets.TryGetValue(entityId, out var row)) return;
        _bullets.Remove(entityId);
        _session.RaiseBulletDelete(CloneBullet(row));
    }

    private void InsertPlayerAmmo(PlayerAmmo ammo)
    {
        _playerAmmo[ammo.PlayerId] = ammo;
        _session.RaisePlayerAmmoInsert(ClonePlayerAmmo(ammo));
    }

    private void UpdatePlayerAmmo(int playerId, Func<PlayerAmmo, PlayerAmmo> mutate, bool insertIfMissing = false)
    {
        if (!_playerAmmo.TryGetValue(playerId, out var current))
        {
            if (!insertIfMissing) return;
            current = new PlayerAmmo { PlayerId = playerId, AmmoSplit = 0 };
        }
        var oldRow = ClonePlayerAmmo(current);
        var newRow = mutate(ClonePlayerAmmo(current));
        _playerAmmo[playerId] = newRow;
        if (insertIfMissing && oldRow == null)
            _session.RaisePlayerAmmoInsert(ClonePlayerAmmo(newRow));
        else
            _session.RaisePlayerAmmoUpdate(oldRow, ClonePlayerAmmo(newRow));
    }

    private void InsertShield(Shield shield)
    {
        _shields[shield.EntityId] = shield;
        _session.RaiseShieldInsert(CloneShield(shield));
    }

    private void UpdateShield(int entityId, Action<Shield> mutate)
    {
        if (!_shields.TryGetValue(entityId, out var current)) return;
        var oldRow = CloneShield(current);
        var newRow = CloneShield(current);
        mutate(newRow);
        _shields[entityId] = newRow;
        _session.RaiseShieldUpdate(oldRow, CloneShield(newRow));
    }

    private void DeleteShield(int entityId)
    {
        if (!_shields.TryGetValue(entityId, out var row)) return;
        _shields.Remove(entityId);
        _session.RaiseShieldDelete(CloneShield(row));
    }

    private void InsertSpeedBuff(SpeedBuff buff)
    {
        _speedBuffs[buff.PlayerId] = buff;
        _session.RaiseSpeedBuffInsert(CloneSpeedBuff(buff));
    }

    private void UpdateSpeedBuff(int playerId, Action<SpeedBuff> mutate)
    {
        if (!_speedBuffs.TryGetValue(playerId, out var current)) return;
        var oldRow = CloneSpeedBuff(current);
        var newRow = CloneSpeedBuff(current);
        mutate(newRow);
        _speedBuffs[playerId] = newRow;
        _session.RaiseSpeedBuffUpdate(oldRow, CloneSpeedBuff(newRow));
    }

    private void DeleteSpeedBuff(int playerId)
    {
        if (!_speedBuffs.TryGetValue(playerId, out var row)) return;
        _speedBuffs.Remove(playerId);
        _session.RaiseSpeedBuffDelete(CloneSpeedBuff(row));
    }

    private void UpdatePlayer(int playerId, Action<Player> mutate)
    {
        if (!_players.TryGetValue(playerId, out var current)) return;
        var oldRow = ClonePlayer(current);
        var newRow = ClonePlayer(current);
        mutate(newRow);
        _players[playerId] = newRow;
        _session.RaisePlayerUpdate(oldRow, ClonePlayer(newRow));
    }

    private void DeleteEntityWithRelations(int entityId)
    {
        if (_foods.ContainsKey(entityId))
            DeleteFood(entityId);
        if (_circles.ContainsKey(entityId))
            DeleteCircle(entityId);
        if (_bullets.ContainsKey(entityId))
            DeleteBullet(entityId);
        DeleteEntity(entityId);
    }

    private static void ClampEntity(Entity entity)
    {
        float radius = MassToDiameter(entity.Mass) * 0.5f;
        float margin = radius + 0.01f;
        entity.Position.X = Mathf.Clamp(entity.Position.X, margin, WorldSize - margin);
        entity.Position.Y = Mathf.Clamp(entity.Position.Y, margin, WorldSize - margin);
    }

    private static bool IsOverLapping(Entity eater, Entity candidate)
    {
        float dx = eater.Position.X - candidate.Position.X;
        float dy = eater.Position.Y - candidate.Position.Y;
        float radius = Mathf.Sqrt(Mathf.Max(0f, eater.Mass)) * 0.25f;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static bool PointInside(float x, float y, Entity target, float radiusSq)
    {
        float dx = x - target.Position.X;
        float dy = y - target.Position.Y;
        return dx * dx + dy * dy <= radiusSq;
    }

    private static float MassToDiameter(float mass) => Mathf.Sqrt(Mathf.Max(0f, mass)) * 0.5f;
    private static float ComputeMaxHp(float mass) => HpBase + mass * HpMassCoeff;

    private static void UpdateHpAfterMassChange(Entity entity)
    {
        entity.MaxHp = ComputeMaxHp(entity.Mass);
        entity.Hp = Mathf.Max(entity.Hp, entity.Mass * HpMinRatio);
        entity.Hp = Mathf.Min(entity.Hp, entity.MaxHp);
    }

    private static Entity CloneEntity(Entity row) => row == null ? null : new Entity
    {
        Id = row.Id,
        Mass = row.Mass,
        Position = new DbVector2(row.Position.X, row.Position.Y),
        Hp = row.Hp,
        MaxHp = row.MaxHp
    };

    private static Food CloneFood(Food row) => row == null ? null : new Food
    {
        EntityId = row.EntityId,
        FoodType = row.FoodType
    };

    private static Circle CloneCircle(Circle row) => row == null ? null : new Circle
    {
        EntityId = row.EntityId,
        PlayerId = row.PlayerId,
        TouchStartMs = row.TouchStartMs,
        IsMerging = row.IsMerging,
        IsSplitting = row.IsSplitting,
        SplitFromEntityId = row.SplitFromEntityId,
        SkinIndex = row.SkinIndex
    };

    private static Bullet CloneBullet(Bullet row) => row == null ? null : new Bullet
    {
        EntityId = row.EntityId,
        OwnerPlayerId = row.OwnerPlayerId,
        DirX = row.DirX,
        DirY = row.DirY,
        SpawnedAtMs = row.SpawnedAtMs,
        BulletType = row.BulletType
    };

    private static PlayerAmmo ClonePlayerAmmo(PlayerAmmo row) => row == null ? null : new PlayerAmmo
    {
        PlayerId = row.PlayerId,
        AmmoSplit = row.AmmoSplit
    };

    private static Shield CloneShield(Shield row) => row == null ? null : new Shield
    {
        EntityId = row.EntityId,
        ShieldHp = row.ShieldHp,
        ShieldMax = row.ShieldMax,
        ExpireAtMs = row.ExpireAtMs
    };

    private static SpeedBuff CloneSpeedBuff(SpeedBuff row) => row == null ? null : new SpeedBuff
    {
        PlayerId = row.PlayerId,
        ExpireAtMs = row.ExpireAtMs
    };

    private static Player ClonePlayer(Player row) => row == null ? null : new Player
    {
        Identity = row.Identity,
        PlayerId = row.PlayerId,
        Name = row.Name,
        Dir = new DbVector2(row.Dir.X, row.Dir.Y),
        RespawnMass = row.RespawnMass,
        InvincibleUntilMs = row.InvincibleUntilMs,
        SkinIndex = row.SkinIndex
    };
}
