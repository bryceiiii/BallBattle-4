using UnityEngine;

/// <summary>
/// 世界空间伤害飘字（轻量 TextMesh，对象池复用）。
/// 由 GameFeel 管理，向上飘 + 淡出，结束后回收。
/// </summary>
public class FloatingText : MonoBehaviour
{
    private TextMesh _tm;
    private float _age;
    private float _life;
    private Vector3 _vel;
    private Color _color;
    private System.Action<FloatingText> _onDone;

    public void Init(string text, Vector3 worldPos, Color color, Vector3 vel, float life, System.Action<FloatingText> onDone)
    {
        if (_tm == null)
        {
            _tm = GetComponent<TextMesh>();
            if (_tm == null) _tm = gameObject.AddComponent<TextMesh>();
        }

        _tm.text = text;
        _tm.fontSize = 24;
        _tm.characterSize = 0.08f;
        _tm.anchor = TextAnchor.MiddleCenter;
        _tm.alignment = TextAlignment.Center;
        _tm.color = color;
        if (_tm.font == null)
            _tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var mr = GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = 100;

        transform.position = worldPos;
        _color = color;
        _vel = vel;
        _life = Mathf.Max(0.01f, life);
        _age = 0f;
        _onDone = onDone;

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

        transform.position += _vel * Time.deltaTime;
        _vel = Vector3.Lerp(_vel, Vector3.zero, 1f - Mathf.Exp(-4f * Time.deltaTime));

        var c = _color;
        c.a = _color.a * (1f - t);
        _tm.color = c;
    }
}
