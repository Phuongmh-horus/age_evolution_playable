using System;
using UnityEngine;

namespace WeaponCraft
{
    [Serializable]
    public class WeaponItem : IEquatable<WeaponItem>
    {
        private static int _idCounter = 0;
        
        [SerializeField] private int _id;
        [SerializeField, Min(1)] private int tier = 1;
        [SerializeField] private string prefabKey;

        public int Id => _id;

        public int Tier
        {
            get => tier;
            set => tier = Mathf.Max(1, value);
        }

        public string PrefabKey
        {
            get => prefabKey;
            set => prefabKey = value;
        }

        public WeaponItem()
        {
            _id = ++_idCounter;
        }

        public WeaponItem(int tier, string prefabKey = null)
        {
            _id = ++_idCounter;
            Tier = tier;
            this.prefabKey = prefabKey;
        }

        public WeaponItem Clone()
        {
            return new WeaponItem(tier, prefabKey);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((WeaponItem)obj);
        }

        public bool Equals(WeaponItem other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return _id == other._id;
        }

        public override int GetHashCode()
        {
            return _id;
        }
    }
}
