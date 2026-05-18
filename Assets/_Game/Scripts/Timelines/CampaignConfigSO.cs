using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CampaignConfig", menuName = "Game Config/Campaign Config", order = 0)]
public class CampaignConfigSO : ScriptableObject
{
    [Header("Timelines Configuration")]
    [Tooltip("Danh sách tất cả timelines trong game")]
    public List<TimelineDataSO> Timelines = new List<TimelineDataSO>();

    [Header("Default Settings")]
    [Tooltip("Timeline mặc định khi bắt đầu game")]
    public TimelineDataSO DefaultTimeline;

    private Dictionary<int, TimelineDataSO> _timelineCacheById;

    private void OnValidate()
    {
        // Invalidate cache when data is changed in the editor
        _timelineCacheById = null;

        // Tự động điền TimelineId dựa trên vị trí trong danh sách
        for (int i = 0; i < Timelines.Count; i++)
        {
            if (Timelines[i] != null)
                Timelines[i].TimelineIndex = i;
        }

        // Tự động set default timeline nếu chưa có
        if (DefaultTimeline == null && Timelines.Count > 0)
        {
            DefaultTimeline = Timelines[0];
        }
    }

    private void BuildCache()
    {
        if (_timelineCacheById != null) return;

        if (Timelines == null)
        {
            _timelineCacheById = new Dictionary<int, TimelineDataSO>();
            return;
        }

        _timelineCacheById = new Dictionary<int, TimelineDataSO>(Timelines.Count);
        foreach (var timeline in Timelines)
        {
            if (!timeline) continue;

            // Cache by ID
            if (!_timelineCacheById.ContainsKey(timeline.TimelineIndex))
            {
                _timelineCacheById.Add(timeline.TimelineIndex, timeline);
            }
        }
    }

    /// <summary>
    /// Lấy timeline theo ID
    /// </summary>
    public TimelineDataSO GetTimelineById(int id)
    {
        BuildCache();

        if (_timelineCacheById == null) return null;

        _timelineCacheById.TryGetValue(id, out TimelineDataSO timeline);
        return timeline;
    }

    /// <summary>
    /// Lấy era theo ID
    /// </summary>
    public EraDataSO GetEraDataById(int timelineId, int eraId)
    {
        var timeline = GetTimelineById(timelineId);
        if (!timeline) return null;

        return timeline.GetEraById(eraId);
    }

    /// <summary>
    /// Lấy timeline theo index
    /// </summary>
    public TimelineDataSO GetTimeline(int index)
    {
        if (index >= 0 && index < Timelines.Count)
            return Timelines[index];
        return null;
    }

    /// <summary>
    /// Lấy tổng số timelines
    /// </summary>
    public int GetTotalTimelines()
    {
        return Timelines.Count;
    }

    /// <summary>
    /// Lấy timeline đầu tiên hoặc default timeline
    /// </summary>
    public TimelineDataSO GetStartingTimeline()
    {
        if (DefaultTimeline != null)
            return DefaultTimeline;

        return Timelines.Count > 0 ? Timelines[0] : null;
    }
}
