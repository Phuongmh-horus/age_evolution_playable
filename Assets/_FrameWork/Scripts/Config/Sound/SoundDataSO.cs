using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SoundDataSO", menuName = "ScriptableObjects/SoundSO", order = 10)]
public class SoundDataSO : ScriptableObject
{
    public List<SoundData> soundDataList = new List<SoundData>();

    // Cache dictionary for O(1) lookup
    private Dictionary<AudioClipName, SoundData> _soundDataCache;

    /// <summary>
    /// Find sound data by AudioClipName with O(1) lookup
    /// </summary>
    public SoundData GetSoundData(AudioClipName clipName)
    {
        // Lazy initialization - build cache only when needed
        if (_soundDataCache == null)
        {
            BuildCache();
        }

        _soundDataCache.TryGetValue(clipName, out var soundData);
        return soundData;
    }

    /// <summary>
    /// Find all sound data containing the search string (case-insensitive)
    /// </summary>
    public List<SoundData> GetSoundDataByName(string searchString)
    {
        var results = new List<SoundData>();

        if (string.IsNullOrEmpty(searchString))
        {
            return new List<SoundData>(soundDataList);
        }

        searchString = searchString.ToLower();

        foreach (var soundData in soundDataList)
        {
            if (soundData.Name.ToString().ToLower().Contains(searchString))
            {
                results.Add(soundData);
            }
        }

        return results;
    }

    /// <summary>
    /// Build cache dictionary from list. Call this if soundDataList is modified at runtime.
    /// </summary>
    public void RebuildCache()
    {
        BuildCache();
    }

    private void BuildCache()
    {
        _soundDataCache = new Dictionary<AudioClipName, SoundData>();

        if (soundDataList == null) return;

        foreach (var soundData in soundDataList)
        {
            if (!_soundDataCache.ContainsKey(soundData.Name))
            {
                _soundDataCache[soundData.Name] = soundData;
            }
            else
            {
                Debug.LogWarning($"[SoundDataSO] Duplicate sound data found: {soundData.Name}. Keeping first occurrence.");
            }
        }
    }

    private void OnValidate()
    {
        // Rebuild cache when inspector values change (Editor only)
        _soundDataCache = null;
    }
}
