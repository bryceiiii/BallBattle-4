using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 皮肤管理器 —— 存储当前选中的皮肤，供 PrefabsManager/CircleController 查询并应用。
/// </summary>
public class SkinManager : MonoBehaviour
{
    public static SkinManager Instance { get; private set; }

    /// <summary>皮肤定义：名称 + Sprite + 可选颜色信息</summary>
    [System.Serializable]
    public class SkinDef
    {
        public string skinName;
        public Sprite sprite;
        public Color tintColor = Color.white;
    }

    [Header("可用皮肤列表（Inspector 拖入）")]
    [Tooltip("将所有皮肤 Sprite 拖入此列表。索引 0 为默认皮肤。")]
    public List<SkinDef> skins = new List<SkinDef>();

    /// <summary>当前选中的皮肤索引（-1 = 未选择）</summary>
    public int SelectedSkinIndex { get; private set; } = -1;

    /// <summary>当前选中的皮肤数据</summary>
    public SkinDef SelectedSkin
    {
        get
        {
            if (SelectedSkinIndex < 0 || SelectedSkinIndex >= skins.Count)
                return null;
            return skins[SelectedSkinIndex];
        }
    }

    /// <summary>皮肤切换事件（新索引, 皮肤数据）</summary>
    public event Action<int, SkinDef> OnSkinChanged;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 选择皮肤（由 UI 调用）。
    /// 返回是否选择成功。
    /// </summary>
    public bool SelectSkin(int index)
    {
        if (index < 0 || index >= skins.Count) return false;

        if (SelectedSkinIndex != index)
        {
            SelectedSkinIndex = index;
            Debug.Log($"[SkinManager] 选中皮肤 [{index}]: {skins[index].skinName}");
            OnSkinChanged?.Invoke(index, skins[index]);

            // 通知服务器皮肤变更
            var conn = SpacetimeDBNetworkManager.Instance?.Db;
            if (conn != null)
            {
                conn.Reducers.UpdatePlayerSkin(index);
            }
        }
        return true;
    }

    /// <summary>
    /// 将当前选中的皮肤应用到目标 GameObject（必须有 SpriteRenderer）。
    /// 供 PrefabsManager 或 GameManager 在创建玩家球后调用。
    /// </summary>
    public void ApplySkinTo(GameObject target)
    {
        if (SelectedSkin == null || target == null) return;

        var sr = target.GetComponent<SpriteRenderer>();
        if (sr != null && SelectedSkin.sprite != null)
        {
            sr.sprite = SelectedSkin.sprite;
            sr.color = SelectedSkin.tintColor;
        }
    }

    /// <summary>
    /// 是否有有效的皮肤被选中。
    /// </summary>
    public bool HasValidSelection => SelectedSkinIndex >= 0 && SelectedSkinIndex < skins.Count;

    /// <summary>根据索引获取皮肤定义（供远程玩家球查询）</summary>
    public SkinDef GetSkinByIndex(int index)
    {
        if (index < 0 || index >= skins.Count) return null;
        return skins[index];
    }
}
