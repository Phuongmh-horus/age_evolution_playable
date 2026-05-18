using System.Collections.Generic;
using GamePlay.Characters;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "NewEra", menuName = "Game Config/Era", order = 2)]
public class EraDataSO : ScriptableObject
{
    [Header("Era Information")]
    [Tooltip("ID duy nhất của era")]
    public int EraIndex;

    [Tooltip("Tên era")]
    public string EraName;

    [FormerlySerializedAs("Background")]
    [Tooltip("Background/Theme của era")]
    public Sprite BackgroundSprite;

    [Tooltip("Luợng cash reset khi bắt đầu era")]
    public int CashOnStartEra;

    [Header("Map Configuration")]
    [Tooltip("Map configuration cho era này")]
    public MapDataSO MapData;

    [Tooltip("Object cờ")]
    public MilestoneOnMap Milestone;

    [Header("Content Configuration")]
    [Tooltip("Danh sách contents trong era (mỗi content là 1 ScriptableObject)")]
    public List<ContentDataSO> Contents = new List<ContentDataSO>();

    [Header("Characters Configuration")]
    [Tooltip("Danh sách characters có thể chơi trong era này")]
    public CharacterListDataSO CharacterList;

    [Header("Turntable Configuration")]
    [Tooltip("Bàn xoay của era")]
    public TurntableDataSO Turntable;

    [Header("Evolution Configuration")]
    [Tooltip("Cấu hình evolution/level-up cho era này")]
    public EvolutionConfigSO EvolutionConfig;



    [Header("Income Configuration")]
    [Tooltip("Cấu hình nâng cấp nhân vật của item nhà máy")]
    public CharacterFactoryUpgradeDataSO CharacterFactoryUpgradeConfig;

    [Header("Income Configuration")]
    [Tooltip("Cấu hình dữ liệu của item theo Era")]
    public ItemDataSO ItemConfig;

    private Dictionary<int, ContentDataSO> _contentCache;

    private void OnEnable()
    {
        BuildContentCache();
    }

    private void OnValidate()
    {
        BuildContentCache();
    }

    private void BuildContentCache()
    {
        int id = 0;
        _contentCache = new Dictionary<int, ContentDataSO>();
        if (Contents != null)
        {
            foreach (var content in Contents)
            {
                content.ContentId = id;
                if (content != null && !_contentCache.ContainsKey(content.ContentId))
                {
                    _contentCache[content.ContentId] = content;
                }

                id++;
            }
        }
    }

    /// <summary>
    /// Lấy tổng số contents
    /// </summary>
    public int GetTotalContents()
    {
        return Contents.Count;
    }

    /// <summary>
    /// Lấy content theo ID
    /// </summary>
    public ContentDataSO GetContentById(int id)
    {
        if (_contentCache == null)
        {
            BuildContentCache();
        }

        _contentCache.TryGetValue(id, out ContentDataSO content);
        return content;
    }

    /// <summary>
    /// Lấy content theo index
    /// </summary>
    public ContentDataSO GetContent(int index)
    {
        if (index >= 0 && index < Contents.Count)
            return Contents[index];
        return null;
    }
}

