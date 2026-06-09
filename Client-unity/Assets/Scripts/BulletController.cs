using UnityEngine;

/// <summary>
/// 子弹控制器。管理子弹外观。
/// 用户创建预制体挂载此脚本，GameManager 会实例化。
/// </summary>
public class BulletController : MonoBehaviour
{
    public int entityId;

    [Header("参数")]
    public float maxLifetime = 3f;       // 最大存活时间（与服务端一致）
    public float fadeDuration = 0.5f;    // 消失前淡出时间

    private float age = 0f;

    void Update()
    {
        age += Time.deltaTime;

        // 淡出
        if (age >= maxLifetime - fadeDuration)
        {
            float alpha = Mathf.Lerp(1f, 0f, (age - (maxLifetime - fadeDuration)) / fadeDuration);
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
            {
                var c = sr.color;
                sr.color = new Color(c.r, c.g, c.b, alpha);
            }
        }

        // 超时保护
        if (age >= maxLifetime + 1f)
            Destroy(gameObject);
    }
}
