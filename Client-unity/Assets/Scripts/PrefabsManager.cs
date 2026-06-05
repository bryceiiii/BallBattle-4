using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PrefabsManager : MonoBehaviour
{
    public static PrefabsManager Instance { get; private set; }

    public GameObject foodPrefab;
    public GameObject circlePrefab;
    



    private void Awake()
    {
        Instance = this;
    }

    public GameObject SpawnFood(int entityId, float x, float y, float mass)
    {
        var food = Instantiate(foodPrefab, new Vector3(x, y, 0f), Quaternion.identity);
        var diameter = MassToDiameter(mass);

        // 此处的 transform.localScale 是直接覆盖，如果预制体原始尺寸太大，可以靠减小 visualScale 来平衡
        food.transform.localScale = new Vector3(diameter, diameter, 1f);
        food.name = "Food" + entityId;
        return food;
    }
    public GameObject SpawnCircle(int entityId, float x, float y, float mass,string name)
    {
        var circle = Instantiate(circlePrefab, new Vector3(x, y, 0f), Quaternion.identity);
        var diameter = MassToDiameter(mass);
        circle.transform.localScale = new Vector3(diameter, diameter, 1f);
        circle.name = name;
        circle.GetComponent<CircleController>()?.UpdateName(name);
        circle.GetComponent<CircleController>().entityId = entityId;
        return circle;
    }

    public float MassToDiameter(float mass)
    {
        // 我们在原有公式的基础上，乘以一个可配置的视觉缩放系数
        return (Mathf.Sqrt(mass) / 2f) ;
    }
}
