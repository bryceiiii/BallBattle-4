using UnityEngine;

/// <summary>
/// 枪口火花特效外壳：由 GameFeel 对象池管理，复用美术序列帧预制体（自带动画）。
/// 每次播放强制把 Animator 重置到第 0 帧，计时到点后回调回收。
/// 纯视觉，不参与任何物理/服务端逻辑。
/// </summary>
public class MuzzleFlashFx : MonoBehaviour
{
    private Transform _tr;
    private Animator _anim;
    private SpriteRenderer _sr;

    private float _age;
    private float _life;
    private System.Action<MuzzleFlashFx> _onDone;

    /// <summary>预制体自带 Animator 动画的最长时长（读不到返回 fallback），供 GameFeel 自动算存活时间。</summary>
    public float AutoLife(float fallback)
    {
        CacheRefs();
        if (_anim != null && _anim.runtimeAnimatorController != null)
        {
            float len = 0f;
            foreach (var c in _anim.runtimeAnimatorController.animationClips)
                if (c != null && c.length > len) len = c.length;
            if (len > 0f) return len + 0.02f; // 留一点余量，保证末帧能显示
        }
        return fallback;
    }

    public void Play(Vector3 pos, float angleDeg, Vector3 scale, Color tint, float life,
        System.Action<MuzzleFlashFx> onDone)
    {
        CacheRefs();

        _tr.position = pos;
        _tr.rotation = Quaternion.Euler(0f, 0f, angleDeg);
        _tr.localScale = scale;

        if (_sr != null) _sr.color = tint;

        gameObject.SetActive(true);

        // 复用同一个 Animator：强制从头重播，否则第二次开火会停在末帧
        if (_anim != null)
        {
            _anim.enabled = true;
            _anim.Play(0, -1, 0f);
            _anim.Update(0f);
        }

        _life = Mathf.Max(0.01f, life);
        _age = 0f;
        _onDone = onDone;
    }

    void Update()
    {
        _age += Time.deltaTime;
        if (_age >= _life)
        {
            _onDone?.Invoke(this);
        }
    }

    void CacheRefs()
    {
        if (_tr == null) _tr = transform;
        if (_anim == null) _anim = GetComponentInChildren<Animator>(true);
        if (_sr == null) _sr = GetComponentInChildren<SpriteRenderer>(true);
    }
}
