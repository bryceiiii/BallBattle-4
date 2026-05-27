using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PrefabsManager : MonoBehaviour
{
    public static PrefabsManager Instance { get; private set; }

    public GameObject foodPrefab;
    
    [Header("外观缩放设置")]
    [Tooltip("全局缩放系数。如果食物太大，请调小这个值（如 0.1 或更小）")]
    public float visualScale = 0.1f;

    private void Awake()
    {
        Instance = this;
    }

    public void SpawnFood(int entityId, float x, float y, float mass)
    {
        var food = Instantiate(foodPrefab, new Vector3(x, y, 0f), Quaternion.identity);
        var diameter = MassToDiameter(mass);

        // 此处的 transform.localScale 是直接覆盖，如果预制体原始尺寸太大，可以靠减小 visualScale 来平衡
        food.transform.localScale = new Vector3(diameter, diameter, 1f);
        food.name = "Food" + entityId;
    }
    
    public float MassToDiameter(float mass)
    {
        // 我们在原有公式的基础上，乘以一个可配置的视觉缩放系数
        return (Mathf.Sqrt(mass) / 2f) * visualScale;
    }
}
