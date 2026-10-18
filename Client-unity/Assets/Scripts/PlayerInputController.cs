using UnityEngine;
using SpacetimeDB.Types;

public class PlayerInputController : MonoBehaviour
{
    private DbVector2 lastSendDir = new DbVector2(0, 0);
    private float _sendDirCooldown; // ��ֹͬһ֡���ظ�����
    public static PlayerInputController Instance { get; private set; }
    public Vector2 CurrentDirection { get; private set; }
    public int SelectedAmmoType { get; private set; } = 0; // 0=��ͨ, 1=���ѵ�

    // ===== RTT ���� =====
    /// <summary>���һ�η����͵�ʱ��������� RTT ���㣩</summary>
    public static float LastDirSendTime { get; private set; } = 0f;

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }
    private void Update()
    {
        if (GameSessionManager.Current == null) return;

        HandleMovementInput();
        HandleSplitInput();
        HandleShootInput();
        HandleAmmoSwitch();
    }

    private void HandleMovementInput()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        if (Mathf.Abs(moveX) < 0.01f) moveX = 0;
        if (Mathf.Abs(moveY) < 0.01f) moveY = 0;

        Vector2 rawDir = new Vector2(moveX, moveY);
        CurrentDirection = rawDir;

        DbVector2 curDir = new DbVector2(moveX, moveY);
        if (curDir.X == lastSendDir.X && curDir.Y == lastSendDir.Y) return;

        GameSessionManager.Current.Actions.UpdatePlayerDir(curDir);
        lastSendDir = curDir;
        LastDirSendTime = Time.time;  // ��¼����ʱ�䣬���� RTT ����
    }

    private void HandleSplitInput()
    {
        if (!Input.GetKeyDown(KeyCode.Space)) return;

        // �ͻ������м�飺�Ѵ���������ֹ���ѣ��������������󣬲�������
        if (GameManager.GetLocalPlayerBallCount() >= 16)
            return;

        GameSessionManager.Current.Actions.SplitPlayer();

        // �����ָз��������ͻ��˱��֣�
        GameFeel.Instance?.OnSplit();
    }

    /// <summary>���ּ� 1-2 �л�����</summary>
    private void HandleAmmoSwitch()
    {
        for (int i = 1; i <= 2; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i))
            {
                SelectedAmmoType = i - 1; // 0=��ͨ, 1=���ѵ�
                HudController.Instance?.SelectAmmo(i - 1);
            }
        }
    }

    private void HandleShootInput()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        var session = GameSessionManager.Current;
        if (session == null) return;

        Vector3 ballPos = GameManager.GetLocalMainBallPosition();
        if (ballPos == Vector3.zero) return;

        var cam = Camera.main;
        if (cam == null) return;

        Vector3 mouseScreen = Input.mousePosition;
        mouseScreen.z = -cam.transform.position.z;
        Vector3 mouseWorld = cam.ScreenToWorldPoint(mouseScreen);
        mouseWorld.z = 0f;

        float dirX = mouseWorld.x - ballPos.x;
        float dirY = mouseWorld.y - ballPos.y;
        float len = Mathf.Sqrt(dirX * dirX + dirY * dirY);
        if (len < 0.001f) return;
        dirX /= len;
        dirY /= len;

        // �����ָз��������ͻ��˱��֣�����/����/��/��Ч����֡������Ч��
        GameFeel.Instance?.OnFire(new Vector2(dirX, dirY));

        // ���͹�һ���������������ڱ������򣩣������������ͳһ���˷���ƽ�����
        session.Actions.ShootBullet(dirX, dirY, SelectedAmmoType);
    }
}
