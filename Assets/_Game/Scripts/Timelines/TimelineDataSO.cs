using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "NewTimeline", menuName = "Game Config/Timeline", order = 1)]
public class TimelineDataSO : ScriptableObject
{
    [Header("Timeline Information")]
    [Tooltip("ID duy nhất của timeline")]
    public int TimelineIndex;

    [Tooltip("Tên timeline")]
    public string TimelineName;

    [Tooltip("Mô tả timeline")]
    [TextArea(3, 5)]
    public string Description;

    [Header("Eras Configuration")]
    [Tooltip("Danh sách các eras trong timeline (mỗi era là 1 ScriptableObject)")]
    public List<EraDataSO> Eras = new List<EraDataSO>();

    [Header("Timeline Settings")]
    [Tooltip("Timeline đã unlock sẵn?")]
    public bool IsUnlockedByDefault = true;

    private Dictionary<int, EraDataSO> _eraCache;

    private void OnEnable()
    {
        BuildEraCache();
    }

    private void OnValidate()
    {
        // Tự động điền EraId dựa trên TimelineId và vị trí trong danh sách
        for (int i = 0; i < Eras.Count; i++)
        {
            if (Eras[i] != null)
            {
                Eras[i].EraIndex = i + 1;
            }
        }

        BuildEraCache();
    }

    private void BuildEraCache()
    {
        _eraCache = new Dictionary<int, EraDataSO>();
        if (Eras != null)
        {
            foreach (var era in Eras)
            {
                if (era && !_eraCache.ContainsKey(era.EraIndex))
                {
                    _eraCache[era.EraIndex] = era;
                }
            }
        }
    }

    /// <summary>
    /// Lấy tổng số eras
    /// </summary>
    public int GetTotalEras()
    {
        return Eras.Count;
    }

    /// <summary>
    /// Lấy era theo ID
    /// </summary>
    public EraDataSO GetEraById(int id)
    {
        if (_eraCache == null)
        {
            BuildEraCache();
        }

        _eraCache.TryGetValue(id, out EraDataSO era);
        return era;
    }

    /// <summary>
    /// Lấy era theo index
    /// </summary>
    public EraDataSO GetEra(int index)
    {
        if (index >= 0 && index < Eras.Count)
            return Eras[index];
        return null;
    }

    /// <summary>
    /// Lấy era đầu tiên
    /// </summary>
    public EraDataSO GetFirstEra()
    {
        return GetEra(0);
    }

    /// <summary>
    /// Lấy era cuối cùng
    /// </summary>
    public EraDataSO GetLastEra()
    {
        return GetEra(Eras.Count - 1);
    }

    /// <summary>
    /// Tìm era tiếp theo sau era hiện tại
    /// </summary>
    public EraDataSO GetNextEra(EraDataSO currentEra)
    {
        int currentIndex = Eras.IndexOf(currentEra);
        if (currentIndex >= 0 && currentIndex < Eras.Count - 1)
            return Eras[currentIndex + 1];
        return null;
    }
}
