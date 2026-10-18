using UnityEngine;
using UnityEngine.UI;
using SpacetimeDB.Types;
using SpacetimeDB;
using System.Collections.Generic;
using System;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public InputField InputField;
    public GameObject canvasGo;
    public GameObject bulletPrefab;      // �ӵ�Ԥ���壨��ק��

    private static Dictionary<int, GameObject> Entities = new Dictionary<int, GameObject>();
    public static Dictionary<int, GameObject> Circles = new Dictionary<int, GameObject>();
    private static Dictionary<int, GameObject> Bullets = new Dictionary<int, GameObject>();

    private static Dictionary<int, List<int>> PlayerBallMap = new Dictionary<int, List<int>>();

    // ===== ÿ֡ÿʵ��ֻ�������һ�θ��� =====
    private static HashSet<int> entitiesUpdatedThisFrame = new HashSet<int>();

    // ===== ������Ч׷�� =====
    private static Dictionary<int, GameObject> shieldEffects = new Dictionary<int, GameObject>();

    private Identity localIdentity;
    public DbConnection Conn => SpacetimeDBNetworkManager.Instance?.Db;

    private bool isSubscribed = false;
    private bool _mainThreadSubscribed = false; // [����] ���̶߳��ı�־

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // ��� canvasGo ���ڣ���ʼ���أ��� LobbyUIController �����������ʾ��
        if (canvasGo != null) canvasGo.SetActive(false);
    }

    void Start()
    {
        Application.runInBackground = true;

        // �����Զ����ӡ��������� LobbyUIController ���ⲿ���ô���
        // �������ӳɹ��¼��������ӽ������ٶ��ı�
        SpacetimeDBNetworkManager.OnConnected += SubscribeToTables;
    }

    private void SubscribeToTables()
    {
        if (isSubscribed) return;
        if (Conn == null) return;
        if (Conn.Db == null) return; // ������δ��ȫ����

        // [Android �ؼ�] ��⵱ǰ�Ƿ��� Unity ���߳�
        // SpacetimeDB SDK �� Android �ϴӺ�̨�̵߳��� OnConnected �ص�
        // �¼����ģ�OnInsert += ...���ں�̨�߳̿��ܲ��ᴥ��
        // ��ˣ���̨�߳�ֱ�� return���� Update() �����߳�����
        // �������߳�ʱ���Ƴٵ���һ֡���߳�ִ��
        if (!IsMainThread())
        {
            Debug.Log("[GameManager] ��̨�߳��Ƴٶ��ģ��������߳�ִ��");
            return;  // �� ��Ҫ�� isSubscribed=true���ö�����
        }


        if (Conn.Identity.HasValue)
        {
            localIdentity = Conn.Identity.Value;
            Debug.Log($"�ѳɹ���ȡ����ֵ localIdentity: {localIdentity}");
        }

        Conn.Db.Food.OnInsert += OnFoodInserted;
        Conn.Db.Food.OnDelete += OnFoodDelete;
        Conn.Db.Entity.OnUpdate += OnEntityUpdated;
        Conn.Db.Circle.OnInsert += OnCircleInserted;
        Conn.Db.Circle.OnDelete += OnCircleDeleted;
        Conn.Db.Circle.OnUpdate += OnCircleUpdated;
        Conn.Db.Bullet.OnInsert += OnBulletInserted;
        Conn.Db.Bullet.OnDelete += OnBulletDeleted;
        Conn.Db.PlayerAmmo.OnInsert += OnPlayerAmmoInserted;
        Conn.Db.PlayerAmmo.OnUpdate += OnPlayerAmmoUpdated;
        Conn.Db.Shield.OnInsert += OnShieldInserted;
        Conn.Db.Shield.OnUpdate += OnShieldUpdated;
        Conn.Db.Shield.OnDelete += OnShieldDeleted;
        Conn.Db.SpeedBuff.OnInsert += OnSpeedBuffInserted;
        Conn.Db.SpeedBuff.OnDelete += OnSpeedBuffDeleted;

        isSubscribed = true;
        _mainThreadSubscribed = true;
        Debug.Log("[GameManager] �������߳�������б��¼�����");

        // ���� SubscribeToAllTables ����������������
        try
        {
            Conn.SubscriptionBuilder().SubscribeToAllTables();
            Debug.Log("[GameManager] �ѵ��� SubscribeToAllTables");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameManager] SubscribeToAllTables ʧ��: {e.Message}");
        }
    }

    /// <summary>
    /// ��⵱ǰ�Ƿ��� Unity ���̡߳�
    /// Android IL2CPP �� SpacetimeDB SDK ���ں�̨�̵߳��� OnConnected
    /// </summary>
    private static bool IsMainThread()
    {
#if UNITY_ANDROID || UNITY_IOS
        // �򵥷�����ͨ��һ����־λ��ÿ�� Update �������߳����ã�
        return _lastUpdateFrame == Time.frameCount;
#else
        return true; // PC �� OnConnected ��Զ�����߳�
#endif
    }

    private static int _lastUpdateFrame = -1;

    private void LateUpdate()
    {
        entitiesUpdatedThisFrame.Clear();
    }

    /// <summary>
    /// ͬ������ֶ��ƿ������ Rigidbody2D ������ײ������ SmoothDamp �񵴶�����
    /// ÿ֡������ͬ���������ص���⣬�ص������һ�롣
    private void OnEntityUpdated(EventContext context, Entity oldRow, Entity newRow)
    {
        if (entitiesUpdatedThisFrame.Contains(newRow.Id)) return;
        entitiesUpdatedThisFrame.Add(newRow.Id);

        // ���������
        if (Circles.TryGetValue(newRow.Id, out var go))
        {
            var ctrl = go.GetComponent<CircleController>();
            ctrl.SetTargetPos(new Vector3(newRow.Position.X, newRow.Position.Y, 0));
            ctrl.SetTargetScale(newRow.Mass);
            ctrl.SetHp(newRow.Hp, newRow.MaxHp);

            // ������������ʱͬ�� HUD
            if (ctrl.isLocalPlayer)
            {
                UpdateHudForLocalPlayer(newRow);
            }
        }

        // �����ӵ���SmoothDamp ��ֵ��
        if (Bullets.TryGetValue(newRow.Id, out var bulletGo))
        {
            var trgPos = new Vector3(newRow.Position.X, newRow.Position.Y, 0);
            var ctrl = bulletGo.GetComponent<BulletController>();
            if (ctrl != null)
                ctrl.SetTargetPos(trgPos);
            else
                bulletGo.transform.position = trgPos;
        }
    }

    private void OnCircleDeleted(EventContext context, Circle row)
    {
        if (Circles.Remove(row.EntityId, out var go))
        {
            Debug.Log($"[Circle �����] ���Circle ��ɾ����EntityId: {row.EntityId}");

            RemoveFromPlayerBallMap(row.PlayerId, row.EntityId);
            GameObject.Destroy(go);

            // ��Ȿ������Ƿ�������û��ʣ�����壩
            CheckLocalPlayerDeath();
        }
    }

    private void OnCircleUpdated(EventContext ctx, Circle oldC, Circle newC)
    {
        if (Circles.TryGetValue(newC.EntityId, out var go))
        {
            var ctrl = go.GetComponent<CircleController>();

            // Ƥ������UpdatePlayerSkin �����ִ���ʱ����
            if (oldC.SkinIndex != newC.SkinIndex)
                ctrl.ApplySkinByIndex(newC.SkinIndex);

            if (newC.IsMerging)
            {
                // ������� splitFromEntityId ����˺ϲ�Ŀ����Ĵ��� entity_id
                Transform mergeTarget = null;
                if (newC.SplitFromEntityId != 0 && Circles.TryGetValue(newC.SplitFromEntityId, out var targetGo))
                {
                    mergeTarget = targetGo.transform;
                }

                if (mergeTarget != null)
                {
                    ctrl.StartMergeAnim(mergeTarget);
                }
                else
                {
                    // �Ҳ���Ŀ�꣨Ŀ���ѱ�ɾ�����쳣����ֱ��֪ͨ�������ɺϲ�
                    var conndb = SpacetimeDBNetworkManager.Instance?.Db;
                    if (conndb != null)
                    {
                        conndb.Reducers.FinishMerge(newC.EntityId);
                    }
                }
            }
        }
    }

    /// <summary>
    /// ��ָ��������������scale ��󣩵���
    /// </summary>
    private Transform FindBiggestBallOfPlayer(int playerId)
    {
        Transform biggest = null;
        float maxScale = 0f;

        foreach (var kv in Circles)
        {
            var ctr = kv.Value.GetComponent<CircleController>();
            if (ctr.playerId == playerId)
            {
                float s = kv.Value.transform.localScale.x;
                if (s > maxScale)
                {
                    maxScale = s;
                    biggest = kv.Value.transform;
                }
            }
        }
        return biggest;
    }

    private void OnCircleInserted(EventContext context, Circle row)
    {
        var entity = Conn.Db.Entity.Id.Find(row.EntityId);
        var player = Conn.Db.LoggedInPlayer.PlayerId.Find(row.PlayerId)
                     ?? new Player { Name = "Unknown" };

        GameObject circleGo = PrefabsManager.Instance.SpawnCircle(
            row.EntityId, entity.Position.X, entity.Position.Y, entity.Mass, player.Name
        );
        Circles.Add(row.EntityId, circleGo);

        var controller = circleGo.GetComponent<CircleController>();
        controller.entityId = row.EntityId;
        controller.playerId = row.PlayerId;
        if (entity != null) controller.SetHp(entity.Hp, entity.MaxHp); // ��ʼ�� HP
        controller.SetTargetScale(entity.Mass); // �ؼ����趨 scale Ŀ��ֵ������ƽ������������Ĭ�� 1

        RegisterPlayerBall(row.PlayerId, row.EntityId);

        if (row.IsSplitting)
        {
            GameObject sourceGo = null;
            Vector3 serverEndPos = new Vector3(entity.Position.X, entity.Position.Y, 0);

            if (Circles.TryGetValue(row.SplitFromEntityId, out sourceGo))
            {
                controller.StartSplitAnim(sourceGo.transform.position, serverEndPos);
            }
            else
            {
                controller.SetTargetPos(serverEndPos);
            }
        }
        else
        {
            controller.SetTargetPos(new Vector3(entity.Position.X, entity.Position.Y, 0));
        }

        // ��ײ��ͬ�����������ƣ���ͬ�����͸������������ɣ�
        SetupCrossPlayerCollisionIgnore(controller);

        // Ƥ������ݷ����ͬ���� skin_index Ӧ�õ������򣨱��غ�Զ�̣�
        controller.ApplySkinByIndex(row.SkinIndex);

        if (player.Identity == localIdentity)
        {
            CameraContoller.Instance.AddFollowTarget(circleGo.transform);
            controller.isLocalPlayer = true;
            controller.ApplyLocalPlayerVisual(); // Start() ִ��ʱ isLocalPlayer ���� false�����ﲹ��

            // Ϊ��������������׼����ָʾ��
            var aimIndicator = circleGo.GetComponent<AimIndicator>();
            if (aimIndicator == null) aimIndicator = circleGo.AddComponent<AimIndicator>();
            aimIndicator.isLocalPlayer = true;
            aimIndicator.SetActive(true);
        }
    }

    private void RegisterPlayerBall(int playerId, int entityId)
    {
        if (!PlayerBallMap.ContainsKey(playerId))
        {
            PlayerBallMap[playerId] = new List<int>();
        }
        if (!PlayerBallMap[playerId].Contains(entityId))
        {
            PlayerBallMap[playerId].Add(entityId);
        }
    }

    private void RemoveFromPlayerBallMap(int playerId, int entityId)
    {
        if (PlayerBallMap.TryGetValue(playerId, out var list))
        {
            list.Remove(entityId);
            if (list.Count == 0)
            {
                PlayerBallMap.Remove(playerId);
            }
        }
    }

    /// <summary>
    /// ����Բ-Բ֮��һ�ɽ���������ײ������ͬ��һ��ǲ�ͬ��ң���
    /// ����/�ϲ��ж���ȫ�ڷ���ˣ�MergePlayerCheck �� dist<=ra+rb �ж�����
    /// �ͻ���Բ-Բ�������������ص�����һ�����ƿ� �� ˲��/������
    /// </summary>
    private void SetupCrossPlayerCollisionIgnore(CircleController newController)
    {
        var thisCol = newController.GetComponent<CircleCollider2D>();
        if (thisCol == null) return;

        foreach (var kv in Circles)
        {
            if (kv.Key == newController.entityId) continue;
            var otherCtrl = kv.Value.GetComponent<CircleController>();
            if (otherCtrl == null) continue;

            // Բ��Բ֮��һ�� IgnoreCollision(true)��ͬ���/��ͬ��Ҷ����������ײ
            var otherCol = kv.Value.GetComponent<CircleCollider2D>();
            if (otherCol != null)
                Physics2D.IgnoreCollision(thisCol, otherCol, true);
        }
    }

    // ===== ʳ����� =====
    private void OnFoodDelete(EventContext ctx, Food deletedFood)
    {
        if (Entities.Remove(deletedFood.EntityId, out var go))
        {
            GameObject.Destroy(go);
        }
    }

    private void OnFoodInserted(EventContext ctx, Food newFood)
    {
        var entity = Conn.Db.Entity.Id.Find(newFood.EntityId);
        if (entity == null) return;

        GameObject foodGo;
        if (newFood.FoodType == 1)
            foodGo = PrefabsManager.Instance.SpawnHealthOrb(newFood.EntityId, entity.Position.X, entity.Position.Y, entity.Mass);
        else if (newFood.FoodType == 2)
            foodGo = PrefabsManager.Instance.SpawnSplitOrb(newFood.EntityId, entity.Position.X, entity.Position.Y, entity.Mass);
        else if (newFood.FoodType == 3)
            foodGo = PrefabsManager.Instance.SpawnShieldOrb(newFood.EntityId, entity.Position.X, entity.Position.Y, entity.Mass);
        else if (newFood.FoodType == 4)
            foodGo = PrefabsManager.Instance.SpawnSpeedOrb(newFood.EntityId, entity.Position.X, entity.Position.Y, entity.Mass);
        else
            foodGo = PrefabsManager.Instance.SpawnFood(newFood.EntityId, entity.Position.X, entity.Position.Y, entity.Mass);

        Entities.Add(newFood.EntityId, foodGo);
    }

    // ===== �ӵ���� =====
    private void OnBulletInserted(EventContext ctx, Bullet newBullet)
    {
        var entity = Conn.Db.Entity.Id.Find(newBullet.EntityId);
        if (entity == null) return;

        // ���ӵ�����ѡԤ���壺���ѵ���ר��Ԥ���壬��ͨ����Ĭ��Ԥ����
        GameObject bulletGo;
        bool isSplit = newBullet.BulletType == 1;
        GameObject chosenPrefab = null;
        if (isSplit && PrefabsManager.Instance != null && PrefabsManager.Instance.splitBulletPrefab != null)
            chosenPrefab = PrefabsManager.Instance.splitBulletPrefab;
        else if (bulletPrefab != null)
            chosenPrefab = bulletPrefab;

        if (chosenPrefab != null)
        {
            bulletGo = Instantiate(chosenPrefab);
        }
        else
        {
            bulletGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(bulletGo.GetComponent<Collider>()); // �Ƴ���ײ��
            var sr = bulletGo.AddComponent<SpriteRenderer>();
            var tex = new Texture2D(16, 16);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float dx = x - 7.5f, dy = y - 7.5f;
                    tex.SetPixel(x, y, (dx * dx + dy * dy) <= 56f ? Color.yellow : Color.clear);
                }
            tex.Apply();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f));
            var ctrl = bulletGo.AddComponent<BulletController>();
            ctrl.entityId = newBullet.EntityId;
        }
        bulletGo.name = "Bullet" + newBullet.EntityId;
        bulletGo.transform.position = new Vector3(entity.Position.X, entity.Position.Y, 0);
        

        // �ӵ���ײ����Ϊ Trigger + Rigidbody Kinematic����ֹ�ƶ����嵼�¶���
        foreach (var col in bulletGo.GetComponentsInChildren<Collider2D>())
        {
            col.isTrigger = true;
        }
        var rb2d = bulletGo.GetComponent<Rigidbody2D>();
        if (rb2d != null)
        {
            rb2d.isKinematic = true;
        }

        // ȷ���� BulletController
        var bulletCtrl = bulletGo.GetComponent<BulletController>();
        if (bulletCtrl == null) bulletCtrl = bulletGo.AddComponent<BulletController>();
        bulletCtrl.entityId = newBullet.EntityId;
        bulletCtrl.ownerPlayerId = newBullet.OwnerPlayerId; // ��ֹ����

        // ���������̸��ݷ���˵� dir ���ó�ʼ���򣬱��ⷢ��˲�䳯���λ
        bulletCtrl.InitializeDirection(new Vector2(newBullet.DirX, newBullet.DirY));

        Bullets.Add(newBullet.EntityId, bulletGo);
    }

    private void OnBulletDeleted(EventContext ctx, Bullet deletedBullet)
    {
        if (Bullets.Remove(deletedBullet.EntityId, out var go))
        {
            Destroy(go);
        }
    }

    // ===== �ɰ�UI�����ݾɳ����� =====
    public void OnButtonEnterGameClick()
    {
        if (Conn == null)
        {
            Debug.LogError("SpacetimeDB ��δ���ӣ����ȵ�����Ӱ�ť��");
            return;
        }

        string playerName = InputField != null ? InputField.text.Trim() : "Player";
        if (string.IsNullOrEmpty(playerName)) playerName = "Player" + UnityEngine.Random.Range(100, 999);

        canvasGo.SetActive(false);
        Conn.Reducers.EnterGame(playerName);
    }

    // ===== ��ҩ��� =====
private void OnPlayerAmmoInserted(EventContext ctx, PlayerAmmo ammo)
    {
        UpdateHudAmmo(ammo);
        if (IsLocalPlayerId(ammo.PlayerId))
            GameFeel.Instance?.OnPickup(2);
    }

    private void OnPlayerAmmoUpdated(EventContext ctx, PlayerAmmo oldAmmo, PlayerAmmo newAmmo)
    {
        UpdateHudAmmo(newAmmo);
    }

private void OnShieldInserted(EventContext ctx, Shield shield)
    {
        UpdateHudShield(shield);
        if (IsLocalShield(shield.EntityId))
            GameFeel.Instance?.OnPickup(0);
    }

    private void OnShieldUpdated(EventContext ctx, Shield oldShield, Shield newShield)
    {
        UpdateHudShield(newShield);
    }

    private void OnShieldDeleted(EventContext ctx, Shield shield)
    {
        // �������飺�������岥 Break �������Ի٣�����˲�� Destroy��
        if (shieldEffects.TryGetValue(shield.EntityId, out var fx))
        {
            fx?.GetComponent<ShieldBuffController>()?.Break();
            shieldEffects.Remove(shield.EntityId);
        }

        // ��黤���Ƿ����ڱ������
        foreach (var kv in Circles)
        {
            var ctrl = kv.Value?.GetComponent<CircleController>();
            if (ctrl != null && ctrl.isLocalPlayer && ctrl.entityId == shield.EntityId)
            {
                HudController.Instance?.ClearShield();
                return;
            }
        }
    }

    // ===== �ٶ�Buff =====
private void OnSpeedBuffInserted(EventContext ctx, SpeedBuff sb)
    {
        UpdateHudSpeedBuff(sb);
        if (IsLocalPlayerId(sb.PlayerId))
            GameFeel.Instance?.OnPickup(1);
    }

    private void OnSpeedBuffDeleted(EventContext ctx, SpeedBuff sb)
    {
        ClearHudSpeedBuff(sb);
    }

    private void UpdateHudSpeedBuff(SpeedBuff sb)
    {
        // �������Ƿ����ڱ������
        if (localIdentity != null)
        {
            var player = Conn.Db.LoggedInPlayer.Identity.Find(localIdentity);
            if (player != null && player.PlayerId == sb.PlayerId)
            {
                HudController.Instance?.SetSpeed(sb.ExpireAtMs);
            }
        }
    }

    private void ClearHudSpeedBuff(SpeedBuff sb)
    {
        if (localIdentity != null)
        {
            var player = Conn.Db.LoggedInPlayer.Identity.Find(localIdentity);
            if (player != null && player.PlayerId == sb.PlayerId)
            {
                HudController.Instance?.ClearSpeed();
            }
        }
    }

    private void UpdateHudShield(Shield shield)
    {
        // �ҵ�����������򣨱���/Զ�˶�Ҫ����Ч��Զ�˻���ԭ����������
        foreach (var kv in Circles)
        {
            var ctrl = kv.Value?.GetComponent<CircleController>();
            if (ctrl == null || ctrl.entityId != shield.EntityId) continue;

            // ������Ч��������ҵ��򶼴���
            if (PrefabsManager.Instance.shieldEffectPrefab != null && !shieldEffects.ContainsKey(shield.EntityId))
            {
                var fx = Instantiate(PrefabsManager.Instance.shieldEffectPrefab, kv.Value.transform);
                fx.transform.localPosition = Vector3.zero;
                shieldEffects[shield.EntityId] = fx;
            }

            // HUD ����ʱ/��ֵ��ֻ�б��������Ҫ
            if (ctrl.isLocalPlayer)
            {
                HudController.Instance?.SetShield(shield.ExpireAtMs);
                HudController.Instance?.SetShieldBar(shield.ShieldHp, shield.ShieldMax);
            }
            return;
        }
    }

    /// <summary>
    /// ����ǰ�Ƿ���Ż��ܣ��Է���� Shield ��Ϊ׼������ЧԤ�����Ƿ�ֵ�޹أ���
    /// </summary>
    public static bool HasShield(int ballEntityId)
        => SpacetimeDBNetworkManager.Instance?.Db?.Db?.Shield.EntityId.Find(ballEntityId) != null;

    /// <summary>
    /// ���ܱ��ӵ����У��û�����Ч��һ�£����ͻ��˵�֡���֣����ȷ���˻ذ�����
    /// �����Ƿ�������ˣ�û�л�����Ч�򷵻� false����
    /// </summary>
    public static bool FlashShieldHit(int ballEntityId, Color color, float duration)
    {
        if (!shieldEffects.TryGetValue(ballEntityId, out var fx) || fx == null) return false;
        var ctrl = fx.GetComponent<ShieldBuffController>();
        if (ctrl == null) return false;
        ctrl.FlashHit(color, duration);
        return true;
    }

    private void UpdateHudAmmo(PlayerAmmo ammo)
    {
        if (localIdentity != null)
        {
            var player = Conn.Db.LoggedInPlayer.Identity.Find(localIdentity);
            if (player != null && player.PlayerId == ammo.PlayerId)
            {
                int max = PrefabsManager.Instance != null ? PrefabsManager.Instance.maxSplitAmmo : 5;
                HudController.Instance?.SetAmmoCountMax(1, ammo.AmmoSplit, max);
            }
        }
    }

    void Update()
    {
        _lastUpdateFrame = Time.frameCount; // [���̱߳��] ÿ�� Update �����£��� IsMainThread() �ж�

        // [Android ����] ��̨�̶߳����¼�û��Чʱ�����߳����¶���
        if (!_mainThreadSubscribed && SpacetimeDBNetworkManager.Instance != null
            && SpacetimeDBNetworkManager.Instance.IsConnected)
        {
            Debug.Log("[GameManager] ���̶߳��ף����������ӵ�δ�����̶߳��ģ����¶���");
            // ���� isSubscribed���� SubscribeToTables ���ٽ���
            isSubscribed = false;
            SubscribeToTables();
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (Conn != null)
            {
                print($"��ǰʳ������: {Conn.Db.Food.Count}");
            }
        }

        // [����] H �����Լ������� 10 HP��������˿�Ѫ��ֻ��Ѫ����Ѫ/�������̣�
        if (Input.GetKeyDown(KeyCode.H))
        {
            if (Conn != null)
            {
                var mainBall = FindLocalMainBall();
                if (mainBall != null)
                {
                    var ctrl = mainBall.GetComponent<CircleController>();
                    if (ctrl != null)
                    {
                        Conn.Reducers.DebugDamage(ctrl.entityId, 10f);
                        Debug.Log($"[����] ���Լ������ {ctrl.entityId} ��Ѫ 10");
                    }
                }
                else
                {
                    Debug.LogWarning("[����] δ�ҵ�������ҵ���");
                }
            }
        }

        // [����] J �����Լ�����򴥷������ܻ����֣����� GameFeel ȫ�ף�����������˻ذ���
        // ����ɫ + ���� + ��֡ + ��ը���� + ��Ļ�� + ��Ч + �������죨���жܣ�����˳����Ѫ��Ѫ��Ҳ��
        if (Input.GetKeyDown(KeyCode.J))
        {
            var mainBall = FindLocalMainBall();
            if (mainBall != null)
            {
                var ctrl = mainBall.GetComponent<CircleController>();
                if (ctrl != null)
                {
                    // ģ���ӵ����Ҳ�����
                    GameFeel.Instance?.OnHit(mainBall.transform.position, ctrl, Vector2.right);

                    // ˳���߷���˿�Ѫ����Ѫ������+����Ҳ��Ч�������Ϸ����ʱ���ر����ճ���
                    if (Conn != null)
                        Conn.Reducers.DebugDamage(ctrl.entityId, 10f);

                    Debug.Log("[����] J �������������ܻ�Ч��");
                }
            }
            else
            {
                Debug.LogWarning("[����] δ�ҵ�������ҵ���");
            }
        }
    }

    private void OnDestroy()
    {
        SpacetimeDBNetworkManager.OnConnected -= SubscribeToTables;

        if (Conn != null)
        {
            Conn.Db.Food.OnInsert -= OnFoodInserted;
            Conn.Db.Food.OnDelete -= OnFoodDelete;
            Conn.Db.Entity.OnUpdate -= OnEntityUpdated;
            Conn.Db.Circle.OnInsert -= OnCircleInserted;
            Conn.Db.Circle.OnDelete -= OnCircleDeleted;
            Conn.Db.Circle.OnUpdate -= OnCircleUpdated;
            Conn.Db.Bullet.OnInsert -= OnBulletInserted;
            Conn.Db.Bullet.OnDelete -= OnBulletDeleted;
            Conn.Db.PlayerAmmo.OnInsert -= OnPlayerAmmoInserted;
            Conn.Db.PlayerAmmo.OnUpdate -= OnPlayerAmmoUpdated;
            Conn.Db.Shield.OnInsert -= OnShieldInserted;
            Conn.Db.Shield.OnUpdate -= OnShieldUpdated;
            Conn.Db.Shield.OnDelete -= OnShieldDeleted;
            Conn.Db.SpeedBuff.OnInsert -= OnSpeedBuffInserted;
            Conn.Db.SpeedBuff.OnDelete -= OnSpeedBuffDeleted;
        }
    }

    /// <summary>
    /// ��ȡ��������������������λ�á�
    /// �� PlayerInputController ���ⲿ������á�
    /// </summary>
    public static Vector3 GetLocalMainBallPosition()
    {
        if (Instance == null) return Vector3.zero;
        var t = Instance.FindLocalMainBall();
        return t != null ? t.position : Vector3.zero;
    }

    /// <summary>��ȡ������������ CircleController���� GameFeel ����/��ɫ�ȱ��ֲ���ã���</summary>
    public static CircleController GetLocalMainBallController()
    {
        if (Instance == null) return null;
        var t = Instance.FindLocalMainBall();
        return t != null ? t.GetComponent<CircleController>() : null;
    }

    /// <summary>��ȡ�������������� CircleController �б���� GameFeel �������/ǹ������ȱ��ֲ��������</summary>
    public static List<CircleController> GetLocalPlayerBalls()
    {
        var result = new List<CircleController>();
        foreach (var kv in Circles)
        {
            if (kv.Value == null) continue;
            var ctr = kv.Value.GetComponent<CircleController>();
            if (ctr != null && ctr.isLocalPlayer)
                result.Add(ctr);
        }
        return result;
    }

    private Transform FindLocalMainBall()
    {
        Transform mainBall = null;
        float maxMass = 0f;

        foreach (var kv in Circles)
        {
            var ctr = kv.Value.GetComponent<CircleController>();
            if (ctr.isLocalPlayer)
            {
                float mass = kv.Value.transform.localScale.x;
                if (mass > maxMass)
                {
                    maxMass = mass;
                    mainBall = kv.Value.transform;
                }
                if (mainBall == null)
                    mainBall = kv.Value.transform;
            }
        }
        return mainBall;
    }

    /// <summary>���������ʵ������ͬ���� HUD</summary>
    private void UpdateHudForLocalPlayer(Entity ent)
    {
        if (HudController.Instance == null) return;

        // ���ܱ������������������� HP
        float totalMass = 0f, totalHp = 0f, totalMaxHp = 0f;
        foreach (var kv in Circles)
        {
            var ctr = kv.Value.GetComponent<CircleController>();
            if (ctr != null && ctr.isLocalPlayer)
            {
                float d = kv.Value.transform.localScale.x; // d = diameter = sqrt(mass)/2
                totalMass += (d * 2f) * (d * 2f);          // mass = (2*diameter)^2
                totalHp += ctr.debugHp;
                totalMaxHp += ctr.debugMaxHp;
            }
        }

        HudController.Instance.SetHp(totalHp, totalMaxHp);
        HudController.Instance.SetMass(totalMass);
    }

    /// <summary>��鱾������Ƿ��Ѿ�û�����壨������</summary>
    private void CheckLocalPlayerDeath()
    {
        foreach (var kv in Circles)
        {
            if (kv.Value != null && kv.Value.GetComponent<CircleController>()?.isLocalPlayer == true)
                return; // ���б�����ҵ���û��
        }
        // �������û��ʣ������ �� ��ʾ��������
        HudController.Instance?.ShowDeathScreen();
    }

    /// <summary>��ȡ������ҵ�ǰ���������ϲ������е���</summary>
    public static int GetLocalPlayerBallCount()
    {
        int count = 0;
        foreach (var kv in Circles)
        {
            if (kv.Value != null && kv.Value.GetComponent<CircleController>()?.isLocalPlayer == true)
                count++;
        }
        return count;
    }

    /// <summary>���¿�ʼ��Ϸ���������水ť�ص���</summary>
    public void RespawnPlayer()
    {
        if (Conn != null)
        {
            string playerName = GetSavedPlayerName();
            if (string.IsNullOrEmpty(playerName))
                playerName = "Player" + UnityEngine.Random.Range(100, 999);
            Conn.Reducers.EnterGame(playerName);
        }
    }

    /// <summary>��ȡ�ѱ����������ƣ����� InputField �� �������������</summary>
    private string GetSavedPlayerName()
    {
        if (InputField != null && !string.IsNullOrEmpty(InputField.text.Trim()))
            return InputField.text.Trim();
        if (Conn != null && Conn.Identity.HasValue)
        {
            var p = Conn.Db.LoggedInPlayer.Identity.Find(Conn.Identity.Value);
            if (p != null && !string.IsNullOrEmpty(p.Name))
                return p.Name;
        }
        return null;
    }


    private bool IsLocalPlayerId(int playerId)
    {
        if (localIdentity == null || Conn == null || Conn.Db == null) return false;
        var player = Conn.Db.LoggedInPlayer.Identity.Find(localIdentity);
        return player != null && player.PlayerId == playerId;
    }

    private bool IsLocalShield(int entityId)
    {
        foreach (var kv in Circles)
        {
            var ctrl = kv.Value != null ? kv.Value.GetComponent<CircleController>() : null;
            if (ctrl != null && ctrl.isLocalPlayer && ctrl.entityId == entityId)
                return true;
        }
        return false;
    }
}
