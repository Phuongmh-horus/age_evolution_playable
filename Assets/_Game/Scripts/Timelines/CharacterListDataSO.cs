using System.Collections.Generic;
using GamePlay.Characters;
using GamePlay.Crushers;
using GamePlay.Weapons;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "NewCharacterList", menuName = "Game Config/Character List", order = 3)]
public class CharacterListDataSO : ScriptableObject
{
    [Tooltip("Prefab của card")]
    public CardUnit CardPrefab;

    [System.Serializable]
    public struct CardData
    {
        [Tooltip("Material của card")]
        public Material Material;

        [Tooltip("Sprite của card")]
        public Sprite Sprite;
    }

    [System.Serializable]
    public class CharacterEntry
    {
        [Tooltip("ID này tự động cập nhật theo thứ tự danh sách. Không cần sửa tay.")]
        [SerializeField, HideInInspector] private int characterLevel;
        public int CharacterLevel => characterLevel;

        [Tooltip("Prefab của character")]
        public CharacterUnit CharacterPrefab;

        [Tooltip("Prefab của card")]
        public CardData CardData;

        [Tooltip("Prefab vũ khí sẽ được spawn vào tay")]
        public GameObject WeaponPrefab;

        [FormerlySerializedAs("Damage")]
        [Tooltip("Damage của unit")]
        public int UnitDamage;

        [Tooltip("Damage của vũ khí")]
        public int WeaponDamage;

        [Tooltip("Fire range của unit")]
        public int FireRange;

        // Chỉ cho phép CharacterListDataSO gán ID
        public void SetLevel(int level) => characterLevel = level;
    }

    [Header("Character List")]
    public List<CharacterEntry> Characters = new List<CharacterEntry>();

    [System.NonSerialized] private Dictionary<int, CharacterEntry> _characterLookup;

    // ========================================================================
    // 1. EDITOR LOGIC (Tự động đánh số ID)
    // ========================================================================

    private void OnValidate()
    {
        UpdateIds();
        NormalizeEntries();
    }

    [ContextMenu("Force Update IDs")]
    private void UpdateIds()
    {
        if (Characters == null) return;

        for (int i = 0; i < Characters.Count; i++)
        {
            var data = Characters[i];
            if (data != null)
            {
                // ID luôn bằng Index + 1
                data.SetLevel(i + 1);
            }
        }
    }

    private void NormalizeEntries()
    {
        if (Characters == null) return;

        for (int i = 0; i < Characters.Count; i++)
        {
            var data = Characters[i];
            if (data == null)
            {
                continue;
            }

            data.UnitDamage = Mathf.Max(0, data.UnitDamage);
            data.WeaponDamage = Mathf.Max(0, data.WeaponDamage);
            data.FireRange = Mathf.Max(0, data.FireRange);
        }
    }

    // ========================================================================
    // 2. RUNTIME LOGIC (Tối ưu Cache & Bộ nhớ)
    // ========================================================================

    private void OnEnable()
    {
        _characterLookup = null;
    }

    public void BuildCache()
    {
        if (_characterLookup == null)
            _characterLookup = new Dictionary<int, CharacterEntry>(Characters.Count);
        else
            _characterLookup.Clear();

        for (int i = 0; i < Characters.Count; i++)
        {
            var data = Characters[i];
            if (data != null)
            {
                _characterLookup[data.CharacterLevel] = data;
            }
        }
    }

    public Dictionary<int, CharacterEntry> GetCharacterLookup()
    {
        if (_characterLookup == null || _characterLookup.Count == 0)
            BuildCache();

        return _characterLookup;
    }

    public CharacterEntry GetCharacterByLevel(int level)
    {
        if (_characterLookup == null || _characterLookup.Count == 0)
            BuildCache();

        if (_characterLookup.TryGetValue(level, out var data))
            return data;

        // fallback: lấy level cao nhất đang có
        int maxKey = int.MinValue;
        foreach (var key in _characterLookup.Keys)
        {
            if (key > maxKey) maxKey = key;
        }

        if (maxKey != int.MinValue)
        {
            Debug.LogWarning($"Level {level} không tồn tại, sử dụng CharacterEntry của level {maxKey} (level cao nhất)");
            return _characterLookup[maxKey];
        }

        Debug.LogError($"No character data found for level {level}");
        return null;
    }
}
