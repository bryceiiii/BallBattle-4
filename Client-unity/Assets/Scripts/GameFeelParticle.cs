using UnityEngine;

/// <summary>
/// 自驱动的表现层粒子（枪口闪光 / 命中爆炸碎片）。
/// 由 GameFeel 对象池管理，生命周期结束后通过 onDone 回收到池中。
/// 纯视觉，不参与任何物理/服务端逻辑。
/// </summary>
public class GameFeelParticle : MonoBehaviour
{
    private SpriteRenderer _sr;
    private Vector2 _vel;
    private float _age;
    private float _life;
    private float _drag;
    private Color _color;
    private Vector2 _startScale;
    private Vector2 _endScale;
    private System.Action<GameFeelParticle> _onDone;

    public void Init(Sprite sprite, Vector3 pos, Vector2 startScale, Vector2 endScale,
        Color color, Vector2 vel, float life, float drag, System.Action<GameFeelParticle> onDone)
    {
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        if (_sr == null) _sr = gameObject.AddComponent<SpriteRenderer>();
        _sr.sprite = sprite;
        _sr.color = color;
        _sr.sortingOrder = 100;

        transform.position = pos;
        transform.localScale = new Vector3(startScale.x, startScale.y, 1f);

        _vel = vel;
        _life = Mathf.Max(0.01f, life);
        _drag = drag;
        _color = color;
        _startScale = startScale;
        _endScale = endScale;
        _onDone = onDone;
        _age = 0f;

        gameObject.SetActive(true);
    }

    void Update()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / _life);

        if (t >= 1f)
        {
            _onDone?.Invoke(this);
            return;
        }

        // 移动（带阻力衰减）
        if (_drag > 0f)
            _vel = Vector2.Lerp(_vel, Vector2.zero, 1f - Mathf.Exp(-_drag * Time.deltaTime));
        transform.position += (Vector3)(_vel * Time.deltaTime);

        // 缩放 + 淡出
        Vector2 s = Vector2.Lerp(_startScale, _endScale, t);
        transform.localScale = new Vector3(s.x, s.y, 1f);

        var c = _color;
        c.a = _color.a * (1f - t);
        _sr.color = c;
    }
}
